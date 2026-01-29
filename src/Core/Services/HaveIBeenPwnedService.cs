using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace PhantomVault.Core.Services;

/// <summary>
/// Service for checking passwords against the Have I Been Pwned (HIBP) database.
/// Uses k-anonymity model to protect password privacy - only first 5 hash chars sent.
/// API: https://haveibeenpwned.com/API/v3#PwnedPasswords
/// </summary>
public sealed class HaveIBeenPwnedService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string PwnedPasswordsApiUrl = "https://api.pwnedpasswords.com/range/";
    private const int HashPrefixLength = 5;

    public HaveIBeenPwnedService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Add user agent as required by HIBP API
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhantomVault-PasswordManager");
    }

    /// <summary>
    /// Check if a password has been exposed in known data breaches.
    /// Returns the number of times the password was found (0 = safe).
    /// </summary>
    /// <param name="password">The password to check</param>
    /// <returns>Number of breach occurrences (0 means password is safe)</returns>
    public async Task<int> CheckPasswordBreachAsync(string password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        try
        {
            // Hash the password with SHA-1 (HIBP uses SHA-1)
            var hash = ComputeSha1Hash(password);

            // Take first 5 characters (k-anonymity prefix)
            var hashPrefix = hash.Substring(0, HashPrefixLength);
            var hashSuffix = hash.Substring(HashPrefixLength);

            // Query HIBP API with prefix only
            var url = $"{PwnedPasswordsApiUrl}{hashPrefix}";
            var response = await _httpClient.GetStringAsync(url);

            // Parse response to find matching hash suffix
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    var returnedSuffix = parts[0].Trim();
                    var count = parts[1].Trim();

                    if (string.Equals(returnedSuffix, hashSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return int.TryParse(count, out var breachCount) ? breachCount : 0;
                    }
                }
            }

            // Password not found in breaches
            return 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HaveIBeenPwned] Breach check failed: {ex.Message}");
            // Return -1 to indicate check failure (not a breach, but couldn't verify)
            return -1;
        }
    }

    /// <summary>
    /// Batch check multiple passwords for breaches.
    /// Returns dictionary of password → breach count.
    /// </summary>
    public async Task<Dictionary<string, int>> CheckPasswordsAsync(IEnumerable<string> passwords)
    {
        var results = new Dictionary<string, int>();

        foreach (var password in passwords)
        {
            if (string.IsNullOrEmpty(password))
                continue;

            var count = await CheckPasswordBreachAsync(password);
            results[password] = count;

            // Add delay to respect API rate limits (HIBP recommends 1500ms between requests)
            await Task.Delay(1500);
        }

        return results;
    }

    /// <summary>
    /// Check if an email address has been involved in data breaches.
    /// NOTE: This requires an API key from HIBP. Free tier is limited.
    /// </summary>
    /// <param name="email">Email address to check</param>
    /// <param name="apiKey">HIBP API key (required for email breach checks)</param>
    /// <returns>List of breach names the email was found in</returns>
    public async Task<List<string>> CheckEmailBreachAsync(string email, string? apiKey = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new List<string>();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            System.Diagnostics.Debug.WriteLine("[HaveIBeenPwned] API key required for email breach checks");
            return new List<string>();
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://haveibeenpwned.com/api/v3/breachedaccount/{Uri.EscapeDataString(email)}");
            request.Headers.Add("hibp-api-key", apiKey);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Email not found in breaches (good!)
                return new List<string>();
            }

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[HaveIBeenPwned] Email check failed: {response.StatusCode}");
                return new List<string>();
            }

            var content = await response.Content.ReadAsStringAsync();

            // Parse JSON response to extract breach names
            // Note: This is a simplified implementation. Consider using System.Text.Json for production.
            var breachNames = new List<string>();
            var nameMatches = System.Text.RegularExpressions.Regex.Matches(content, @"""Name""\s*:\s*""([^""]+)""");
            foreach (System.Text.RegularExpressions.Match match in nameMatches)
            {
                if (match.Groups.Count > 1)
                {
                    breachNames.Add(match.Groups[1].Value);
                }
            }

            return breachNames;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HaveIBeenPwned] Email breach check failed: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Get security recommendation based on breach count.
    /// </summary>
    public static BreachSeverity GetBreachSeverity(int breachCount)
    {
        if (breachCount < 0)
            return BreachSeverity.Unknown;
        if (breachCount == 0)
            return BreachSeverity.Safe;
        if (breachCount < 10)
            return BreachSeverity.Low;
        if (breachCount < 100)
            return BreachSeverity.Medium;
        if (breachCount < 1000)
            return BreachSeverity.High;

        return BreachSeverity.Critical;
    }

    /// <summary>
    /// Get user-friendly message for breach result.
    /// </summary>
    public static string GetBreachMessage(int breachCount)
    {
        return breachCount switch
        {
            < 0 => "Unable to verify password security",
            0 => "Password not found in known breaches",
            < 10 => $"Password found in {breachCount} breach(es). Consider changing it.",
            < 100 => $"Password found in {breachCount} breaches. Change it immediately.",
            < 1000 => $"Password found in {breachCount} breaches! Change it now!",
            _ => $"Password found in {breachCount:N0} breaches! Extremely compromised - change immediately!"
        };
    }

    /// <summary>
    /// Compute SHA-1 hash of a password (required by HIBP API).
    /// </summary>
    private static string ComputeSha1Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Severity level of password breach exposure.
/// </summary>
public enum BreachSeverity
{
    Unknown,    // Could not check
    Safe,       // Not found in breaches
    Low,        // 1-9 occurrences
    Medium,     // 10-99 occurrences
    High,       // 100-999 occurrences
    Critical    // 1000+ occurrences
}
