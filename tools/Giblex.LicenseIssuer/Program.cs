using System.Text.Json;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Licensing;

namespace Giblex.LicenseIssuer;

/// <summary>
/// Offline licence-signing authority for Phantom Obscura.
///
/// Mints the Ed25519-signed tokens the desktop client verifies. It exists so licences
/// can be issued today — by hand, or from a fulfilment script — without waiting on the
/// hosted backend, and so the signing key never has to live inside the app.
///
/// The private key is the whole security boundary: anyone holding it can grant Premium
/// to any device. Keep it on a machine that does not ship, pass it via environment
/// variable rather than a command-line argument where practical (arguments are visible
/// in process listings and shell history), and never commit it.
/// </summary>
internal static class Program
{
    private const string PrivateKeyEnvVar = "PHANTOM_LICENSE_SIGNING_KEY";

    private static int Main(string[] args)
    {
        try
        {
            return (args.FirstOrDefault()?.ToLowerInvariant()) switch
            {
                "keygen" => KeyGen(),
                "issue" => Issue(args),
                "verify" => Verify(args),
                _ => Usage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            Phantom Obscura licence issuer

              license-issuer keygen
                  Generates a fresh Ed25519 keypair. Prints the public key as a C#
                  byte array to paste into LicensePublicKey.cs, and the private key
                  as base64 to store as a server secret.

              license-issuer issue --binding <usbBindingId> --interval monthly|yearly
                                   [--id <licenceId>] [--days <n>] [--unbound]
                  Mints a signed licence token. Reads the private key from the
                  PHANTOM_LICENSE_SIGNING_KEY environment variable (base64).

              license-issuer verify --token <token> [--public <base64>]
                  Verifies a token against the public key embedded in this build
                  and prints its claims. Use to confirm a licence before sending it.

            Examples
              set PHANTOM_LICENSE_SIGNING_KEY=<base64>
              license-issuer issue --binding A1B2C3D4 --interval yearly
              license-issuer verify --token eyJ...
            """);
        return 2;
    }

    private static int KeyGen()
    {
        var (pub, priv) = LicenseTokenCodec.GenerateKeyPair();

        Console.WriteLine("Public key — replace the Placeholder array in");
        Console.WriteLine("src/Core/Services/Licensing/LicensePublicKey.cs:");
        Console.WriteLine();
        for (var i = 0; i < pub.Length; i += 16)
        {
            var row = pub.Skip(i).Take(16).Select(b => b.ToString());
            Console.WriteLine("            " + string.Join(", ", row) + (i + 16 < pub.Length ? "," : ""));
        }

        Console.WriteLine();
        Console.WriteLine("Public key (base64) — for 'verify --public' before you ship it:");
        Console.WriteLine();
        Console.WriteLine("    " + Convert.ToBase64String(pub));
        Console.WriteLine();
        Console.WriteLine("Private key (base64) — store as a server secret, never commit:");
        Console.WriteLine();
        Console.WriteLine("    " + Convert.ToBase64String(priv));
        Console.WriteLine();
        Console.WriteLine("Replacing the public key invalidates every licence already issued");
        Console.WriteLine("under the previous key. Only regenerate deliberately.");
        return 0;
    }

    private static int Issue(string[] args)
    {
        var privB64 = Environment.GetEnvironmentVariable(PrivateKeyEnvVar);
        if (string.IsNullOrWhiteSpace(privB64))
        {
            Console.Error.WriteLine($"error: {PrivateKeyEnvVar} is not set. Run 'keygen' first, or set it to your existing signing key.");
            return 1;
        }

        var binding = Arg(args, "--binding");
        var unbound = args.Contains("--unbound", StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(binding) && !unbound)
        {
            // A licence with no binding works on any device. That is occasionally wanted
            // for support cases, but it must be a deliberate choice rather than the
            // result of a forgotten argument.
            Console.Error.WriteLine("error: --binding is required (or pass --unbound to issue a device-independent licence).");
            return 1;
        }

        var interval = (Arg(args, "--interval") ?? "monthly").ToLowerInvariant();
        var daysArg = Arg(args, "--days");

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset expires;

        if (!string.IsNullOrWhiteSpace(daysArg) && int.TryParse(daysArg, out var days) && days > 0)
        {
            expires = now.AddDays(days);
        }
        else
        {
            expires = interval switch
            {
                "yearly" or "year" or "annual" => now.AddYears(1),
                "monthly" or "month" => now.AddMonths(1),
                _ => throw new ArgumentException($"unknown --interval '{interval}' (expected monthly or yearly)")
            };
        }

        var claims = new LicenseClaims
        {
            LicenseId = Arg(args, "--id") ?? Guid.NewGuid().ToString("N"),
            Tier = PremiumTier.Premium,
            UsbBindingId = unbound ? null : binding,
            IssuedUtc = now,
            ExpiresUtc = expires,
            // Empty means "everything the tier grants" — the client treats it that way,
            // so per-feature grants stay available without being required.
            Features = new List<string>()
        };

        var token = LicenseTokenCodec.CreateToken(claims, Convert.FromBase64String(privB64));

        Console.Error.WriteLine($"licence {claims.LicenseId}  tier=Premium  " +
                                $"binding={(claims.UsbBindingId ?? "<unbound>")}  " +
                                $"expires={expires:u}");
        // Token alone on stdout so it can be piped or captured cleanly.
        Console.WriteLine(token);
        return 0;
    }

    private static int Verify(string[] args)
    {
        var token = Arg(args, "--token");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("error: --token is required.");
            return 1;
        }

        // --public lets a token be checked against a key that is not (yet) the one
        // embedded in this build — necessary when rotating keys, because otherwise you
        // cannot confirm a freshly-minted licence until after you have shipped the new
        // public key. Falls back to the embedded key, which is the normal case.
        var publicOverride = Arg(args, "--public");
        byte[] publicKey;

        if (!string.IsNullOrWhiteSpace(publicOverride))
        {
            publicKey = Convert.FromBase64String(publicOverride);
            Console.Error.WriteLine("(verifying against the supplied --public key, not the embedded one)");
        }
        else if (LicensePublicKey.IsProvisioned)
        {
            publicKey = LicensePublicKey.Bytes.ToArray();
        }
        else
        {
            Console.Error.WriteLine("error: this build has no licence public key provisioned; pass --public <base64>.");
            return 1;
        }

        if (!LicenseTokenCodec.TryVerify(token, publicKey, out var claims) || claims is null)
        {
            Console.Error.WriteLine("INVALID — signature or structure rejected by the embedded public key.");
            return 1;
        }

        Console.WriteLine("VALID signature. Claims:");
        Console.WriteLine(JsonSerializer.Serialize(claims, new JsonSerializerOptions { WriteIndented = true }));

        var remaining = claims.ExpiresUtc - DateTimeOffset.UtcNow;
        Console.WriteLine(remaining > TimeSpan.Zero
            ? $"Expires in {(int)remaining.TotalDays} day(s)."
            : $"EXPIRED {(int)-remaining.TotalDays} day(s) ago.");
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
