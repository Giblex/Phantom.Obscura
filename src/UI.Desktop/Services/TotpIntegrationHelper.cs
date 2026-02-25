using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhantomObscuraV6.UI.Desktop.Services;

/// <summary>
/// Cross-app TOTP integration helper for PhantomObscura.
/// Enables TOTP code generation in password entries and sync with PhantomAttestor.
/// </summary>
public class TotpIntegrationHelper
{
    /// <summary>
    /// Generates a TOTP code for the given secret at the current time
    /// </summary>
    public static string GenerateCode(string secret, int period = 30, int digits = 6, string algorithm = "SHA1")
    {
        try
        {
            // Decode base32 secret
            var secretBytes = Base32Decode(secret.Replace(" ", "").ToUpperInvariant());
            
            // Calculate counter from current time
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var counter = unixTimestamp / period;
            
            // Generate HOTP code
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);

            byte[] hash;
            using (var hmac = GetHmacAlgorithm(algorithm, secretBytes))
            {
                hash = hmac.ComputeHash(counterBytes);
            }

            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

            var otp = binary % (int)Math.Pow(10, digits);
            return otp.ToString().PadLeft(digits, '0');
        }
        catch
        {
            return new string('-', digits);
        }
    }

    /// <summary>
    /// Gets remaining seconds until next code
    /// </summary>
    public static int GetRemainingSeconds(int period = 30)
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return period - (int)(unixTimestamp % period);
    }

    /// <summary>
    /// Validates TOTP secret format
    /// </summary>
    public static bool IsValidSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var cleaned = secret.Replace(" ", "").ToUpperInvariant();
        
        // Base32 alphabet
        var validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        return cleaned.All(c => validChars.Contains(c));
    }

    private static HMAC GetHmacAlgorithm(string algorithm, byte[] key)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "SHA1" => new HMACSHA1(key),
            "SHA256" => new HMACSHA256(key),
            "SHA512" => new HMACSHA512(key),
            _ => new HMACSHA1(key)
        };
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32.Select(c =>
        {
            var idx = alphabet.IndexOf(c);
            return idx < 0 ? string.Empty : Convert.ToString(idx, 2).PadLeft(5, '0');
        }));

        var result = new List<byte>();
        for (int i = 0; i + 8 <= bits.Length; i += 8)
        {
            result.Add(Convert.ToByte(bits.Substring(i, 8), 2));
        }

        return result.ToArray();
    }
}

/// <summary>
/// TOTP field data for password entries in KeePass
/// Store this in custom string fields of KeePass entries
/// </summary>
public class TotpFieldData
{
    public string? TotpSecret { get; set; }
    public int TotpDigits { get; set; } = 6;
    public int TotpPeriod { get; set; } = 30;
    public string TotpAlgorithm { get; set; } = "SHA1";
    public string? TotpIssuer { get; set; }
    public string? TotpLinkedId { get; set; } // Link to SharedTotpEntry.Id

    /// <summary>
    /// Parses TOTP data from KeePass custom string field
    /// Field name: "TOTP_Settings"
    /// Format: secret|digits|period|algorithm|issuer|linkedId
    /// </summary>
    public static TotpFieldData? ParseFromKeePassString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split('|');
        if (parts.Length < 1)
            return null;

        return new TotpFieldData
        {
            TotpSecret = parts[0],
            TotpDigits = parts.Length > 1 && int.TryParse(parts[1], out var d) ? d : 6,
            TotpPeriod = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 30,
            TotpAlgorithm = parts.Length > 3 ? parts[3] : "SHA1",
            TotpIssuer = parts.Length > 4 ? parts[4] : null,
            TotpLinkedId = parts.Length > 5 ? parts[5] : null
        };
    }

    /// <summary>
    /// Serializes TOTP data to KeePass custom string field format
    /// </summary>
    public string SerializeToKeePassString()
    {
        return $"{TotpSecret}|{TotpDigits}|{TotpPeriod}|{TotpAlgorithm}|{TotpIssuer ?? ""}|{TotpLinkedId ?? ""}";
    }

    /// <summary>
    /// Generates current TOTP code
    /// </summary>
    public string GenerateCurrentCode()
    {
        if (string.IsNullOrWhiteSpace(TotpSecret))
            return "------";

        return TotpIntegrationHelper.GenerateCode(TotpSecret, TotpPeriod, TotpDigits, TotpAlgorithm);
    }
}
