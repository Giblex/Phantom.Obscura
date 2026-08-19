using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
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
        using var response = await SendAsync(new { action = "getTotpCode", reference }, ct);
        if (!IsSuccess(response.RootElement)) return null;
        var p = response.RootElement.GetProperty("payload");
        return new AttestorTotpCode(p.GetProperty("code").GetString() ?? "", p.GetProperty("period").GetInt32(), p.GetProperty("validFor").GetInt64());
    }

    public async Task<string> RegisterPasskeyAsync(string userId, string userName, string rpId, CancellationToken ct = default)
    {
        using var response = await SendAsync(new { action = "registerPasskey", userId, userName, rpId }, ct);
        EnsureSuccess(response.RootElement);
        return response.RootElement.GetProperty("payload").GetProperty("reference").GetString()
            ?? throw new InvalidDataException("Attestor returned no passkey reference.");
    }

    public async Task<bool> AssertPasskeyAsync(string reference, string rpId, CancellationToken ct = default)
    {
        using var response = await SendAsync(new { action = "assertPasskey", reference, rpId }, ct);
        return IsSuccess(response.RootElement) && response.RootElement.GetProperty("payload").GetProperty("authenticated").GetBoolean();
    }

    private static async Task<JsonDocument> SendAsync(object request, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2500, ct).ConfigureAwait(false);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), ct).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new IOException("Attestor closed the broker channel.");
        return JsonDocument.Parse(line);
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
