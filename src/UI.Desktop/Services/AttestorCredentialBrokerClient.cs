using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services;

/// <summary>
/// Secretless Obscura-side facade for Phantom Attestor. All returned identifiers are
/// opaque; TOTP seeds and passkey private material never come back over the pipe.
/// </summary>
public sealed class AttestorCredentialBrokerClient
{
    private const string PipeName = "PhantomAttestorCredentialBroker";

    /// <summary>
    /// Whether Attestor is reachable right now. Costs a pipe round-trip; see
    /// <see cref="IsPaired"/> for the cheap "is it installed at all" check.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try { using var response = await SendAsync(new { action = "ping" }, ct); return IsSuccess(response.RootElement); }
        catch { return false; }
    }

    public async Task<string> PutTotpAsync(string? reference, string issuer, string account,
        string secret, int digits, int period, string algorithm, CancellationToken ct = default)
    {
        using var response = await SendAsync(new { action = "putTotp", id = reference, issuer, account, secret, digits, period, algorithm }, ct);
        EnsureSuccess(response.RootElement);
        return response.RootElement.GetProperty("payload").GetProperty("reference").GetString()
            ?? throw new InvalidDataException("Attestor returned no TOTP reference.");
    }

    public async Task<AttestorTotpCode?> GetTotpCodeAsync(string reference, CancellationToken ct = default)
    {
        using var response = await TrySendAsync(new { action = "getTotpCode", reference }, ct);
        if (response == null || !IsSuccess(response.RootElement)) return null;
        var p = response.RootElement.GetProperty("payload");
        return new AttestorTotpCode(p.GetProperty("code").GetString() ?? "", p.GetProperty("period").GetInt32(), p.GetProperty("validFor").GetInt64());
    }

    /// <summary>
    /// Creates a passkey and returns only its opaque reference. For a vault-local credential
    /// that no website will verify; use <see cref="RegisterPasskeyForRelyingPartyAsync"/> when
    /// a site is actually registering the credential.
    /// </summary>
    public async Task<string> RegisterPasskeyAsync(string userId, string userName, string rpId, CancellationToken ct = default)
        => (await RegisterPasskeyForRelyingPartyAsync(userId, userName, rpId, challenge: null, ct)).Reference;

    /// <summary>
    /// Creates a passkey for a relying party, returning the material that party needs to
    /// complete registration.
    ///
    /// <paramref name="challenge"/> should be the site's own challenge. The public key comes
    /// back because a relying party cannot complete a ceremony with a credential ID alone —
    /// it has nothing to verify future assertions against.
    /// </summary>
    public async Task<AttestorPasskeyRegistration> RegisterPasskeyForRelyingPartyAsync(
        string userId, string userName, string rpId, byte[]? challenge = null, CancellationToken ct = default)
    {
        using var response = await SendAsync(new
        {
            action = "registerPasskey",
            userId,
            userName,
            rpId,
            challenge = challenge == null ? null : Convert.ToBase64String(challenge)
        }, ct);

        EnsureSuccess(response.RootElement);
        var p = response.RootElement.GetProperty("payload");

        var reference = p.GetProperty("reference").GetString()
            ?? throw new InvalidDataException("Attestor returned no passkey reference.");

        byte[]? publicKey = null;
        if (p.TryGetProperty("publicKey", out var pk) && pk.ValueKind == JsonValueKind.String)
            publicKey = Convert.FromBase64String(pk.GetString()!);

        byte[] credentialId = p.TryGetProperty("credentialId", out var cid) && cid.ValueKind == JsonValueKind.String
            ? Convert.FromBase64String(cid.GetString()!)
            : Array.Empty<byte>();

        return new AttestorPasskeyRegistration(reference, credentialId, publicKey);
    }

    /// <summary>
    /// Produces a relying-party assertion that can be relayed to a website.
    ///
    /// <paramref name="clientDataHash"/> must be the SHA-256 of the site's clientDataJSON
    /// (WebAuthn L2 §6.3.3). It is required rather than optional on purpose: the previous
    /// version generated its own challenge and returned only a boolean, which authenticated
    /// nobody to anything — a signature over a self-chosen challenge is unverifiable by the
    /// relying party, and the signature was thrown away regardless.
    /// </summary>
    public async Task<AttestorPasskeyAssertion> AssertPasskeyAsync(
        string reference, string rpId, byte[] clientDataHash, CancellationToken ct = default)
    {
        if (clientDataHash is not { Length: 32 })
            throw new ArgumentException("clientDataHash must be a 32-byte SHA-256 digest.", nameof(clientDataHash));

        using var response = await SendAsync(new
        {
            action = "assertPasskey",
            reference,
            rpId,
            clientDataHash = Convert.ToBase64String(clientDataHash)
        }, ct);

        EnsureSuccess(response.RootElement);
        var p = response.RootElement.GetProperty("payload");
        return new AttestorPasskeyAssertion(
            Convert.FromBase64String(p.GetProperty("credentialId").GetString() ?? ""),
            Convert.FromBase64String(p.GetProperty("authenticatorData").GetString() ?? ""),
            Convert.FromBase64String(p.GetProperty("signature").GetString() ?? ""));
    }

    /// <summary>
    /// Local presence check — "is the user here and does this credential still work". Cannot
    /// authenticate to a website; use <see cref="AssertPasskeyAsync"/> for that.
    /// </summary>
    public async Task<bool> VerifyPasskeyPresenceAsync(string reference, string rpId, CancellationToken ct = default)
    {
        using var response = await TrySendAsync(new { action = "assertPasskeyLocal", reference, rpId }, ct);
        return response != null
            && IsSuccess(response.RootElement)
            && response.RootElement.GetProperty("payload").GetProperty("verified").GetBoolean();
    }

    /// <summary>
    /// Whether a pairing with Phantom Attestor exists at all — a cheap file check, distinct
    /// from <see cref="IsAvailableAsync"/>, which costs a full pipe round-trip to establish
    /// that Attestor is also running right now.
    ///
    /// Attestor is an OPTIONAL companion: Obscura is the standalone product, and a vault needs
    /// Attestor only if it was configured to keep its passkey or TOTP material there. Use this
    /// to decide whether to OFFER an Attestor-backed affordance at all, and
    /// <see cref="IsAvailableAsync"/> before relying on one.
    /// </summary>
    public static bool IsPaired => AttestorPairingSecret.Exists();

    /// <summary>
    /// Sends a request, returning null when Attestor simply is not there.
    ///
    /// Read paths use this instead of <see cref="SendAsync"/>. "Attestor is not installed" is
    /// an ordinary, expected state on a standalone Obscura install, not an error: before this
    /// existed, a credential carrying an Attestor TOTP reference threw out of the autofill
    /// state machine — which has no handler — on any machine without Attestor. Write paths
    /// still use SendAsync and still throw, because a caller that just tried to STORE a secret
    /// must not be told it succeeded.
    /// </summary>
    private static async Task<JsonDocument?> TrySendAsync(object request, CancellationToken ct)
    {
        try
        {
            return await SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;   // never paired — Attestor has not run on this machine
        }
        catch (TimeoutException)
        {
            return null;   // installed but not running
        }
        catch (IOException)
        {
            return null;   // channel closed mid-exchange
        }
        catch (UnauthorizedAccessException)
        {
            return null;   // pipe owned by another user; CurrentUserOnly refused it
        }
    }

    private static async Task<JsonDocument> SendAsync(object request, CancellationToken ct)
    {
        // CurrentUserOnly makes the CLIENT verify the server is running as this same user.
        // Without it Obscura would connect to whatever process owns the pipe name — and
        // putTotp hands the raw TOTP seed straight down it, so a name-squatting server
        // running as another account could have harvested seeds as they were saved.
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2500, ct).ConfigureAwait(false);

        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(BuildAuthenticatedEnvelope(request).AsMemory(), ct).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new IOException("Attestor closed the broker channel.");
        return JsonDocument.Parse(line);
    }

    /// <summary>
    /// Wraps a request with proof that this process holds the Attestor pairing secret.
    ///
    /// The tag covers the exact body string that gets transmitted, so what Attestor verifies
    /// is what Attestor acts on. The timestamp and nonce make each envelope single-use inside
    /// a short freshness window, so a captured request cannot be replayed to, say, mint a
    /// fresh TOTP code later.
    /// </summary>
    private static string BuildAuthenticatedEnvelope(object request)
    {
        var body = JsonSerializer.Serialize(request);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var secret = AttestorPairingSecret.Read();
        try
        {
            using var hmac = new HMACSHA256(secret);
            var tag = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{body}"));
            return JsonSerializer.Serialize(new
            {
                body,
                ts = timestamp,
                nonce,
                mac = Convert.ToBase64String(tag)
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static bool IsSuccess(JsonElement root) => root.TryGetProperty("success", out var p) && p.GetBoolean();
    private static void EnsureSuccess(JsonElement root)
    {
        if (IsSuccess(root)) return;
        var reason = root.TryGetProperty("reason", out var p) ? p.GetString() : "unknown error";
        throw new InvalidOperationException($"Phantom Attestor rejected the request: {reason}");
    }
}

public sealed record AttestorTotpCode(string Code, int Period, long ValidForSeconds);

/// <summary>Registration material a relying party needs to store a new credential.</summary>
public sealed record AttestorPasskeyRegistration(string Reference, byte[] CredentialId, byte[]? PublicKey);

/// <summary>Relying-party assertion material, ready to be relayed to a website.</summary>
public sealed record AttestorPasskeyAssertion(byte[] CredentialId, byte[] AuthenticatorData, byte[] Signature);
