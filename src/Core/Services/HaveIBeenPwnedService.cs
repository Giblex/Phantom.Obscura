using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Network;

namespace PhantomVault.Core.Services;

/// <summary>
/// Checks passwords against the HaveIBeenPwned k-anonymity range API. All outbound
/// traffic is funnelled through <see cref="IInternetGateway"/> with explicit per-session
/// user consent, SPKI pinning, host allowlist, and audit logging. The cleartext
/// password is never transmitted — only the first 5 hex chars of SHA-1(password).
/// </summary>
public sealed class HaveIBeenPwnedService : IDisposable
{
    private readonly IInternetGateway _gateway;
    private readonly object _lock = new();
    private InternetAccessGrant? _activeGrant;
    private HttpClient? _client;
    private const int HashPrefixLength = 5;

    public HaveIBeenPwnedService(IInternetGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public bool IsInternetEnabled => _activeGrant?.IsLive ?? false;

    public async Task<bool> RequestInternetAccessAsync(CancellationToken ct = default)
    {
        if (IsInternetEnabled) return true;
        var grant = await _gateway.RequestAccessAsync(HibpGatewayPolicy.CreateRequest(), ct).ConfigureAwait(false);
        if (grant is null) return false;

        lock (_lock)
        {
            _client?.Dispose();
            _activeGrant = grant;
            _client = _gateway.CreateClient(grant);
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("PhantomVault/1.0");
        }
        return true;
    }

    public void DisableInternet()
    {
        InternetAccessGrant? grant;
        HttpClient? client;
        lock (_lock)
        {
            grant = _activeGrant;
            client = _client;
            _activeGrant = null;
            _client = null;
        }
        if (grant is not null) _gateway.Revoke(grant, "Caller disabled internet.");
        client?.Dispose();
    }

    public async Task<int> CheckPasswordBreachAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        HttpClient? client;
        lock (_lock) { client = _client; }
        if (client is null) return -1;

        try
        {
            var hash = ComputeSha1Hash(password);
            var prefix = hash[..HashPrefixLength];
            var suffix = hash[HashPrefixLength..];

            var url = $"https://{HibpGatewayPolicy.RangeHost}/range/{prefix}";
            var response = await client.GetStringAsync(url, ct).ConfigureAwait(false);

            foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length != 2) continue;
                if (string.Equals(parts[0].Trim(), suffix, StringComparison.OrdinalIgnoreCase))
                    return int.TryParse(parts[1].Trim(), out var n) ? n : 0;
            }
            return 0;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public async Task<Dictionary<string, int>> CheckPasswordsAsync(IEnumerable<string> passwords, CancellationToken ct = default)
    {
        var results = new Dictionary<string, int>();
        foreach (var password in passwords)
        {
            if (string.IsNullOrEmpty(password)) continue;
            results[password] = await CheckPasswordBreachAsync(password, ct).ConfigureAwait(false);
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
        return results;
    }

    public async Task<List<string>> CheckEmailBreachAsync(string email, string? apiKey = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(apiKey))
            return new List<string>();

        HttpClient? client;
        lock (_lock) { client = _client; }
        if (client is null) return new List<string>();

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://{HibpGatewayPolicy.BreachHost}/api/v3/breachedaccount/{Uri.EscapeDataString(email)}");
            request.Headers.Add("hibp-api-key", apiKey);

            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new List<string>();
            if (!response.IsSuccessStatusCode) return new List<string>();

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var breachNames = new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"""Name""\s*:\s*""([^""]+)""");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Groups.Count > 1) breachNames.Add(m.Groups[1].Value);
            }
            return breachNames;
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    public static BreachSeverity GetBreachSeverity(int breachCount)
    {
        if (breachCount < 0) return BreachSeverity.Unknown;
        if (breachCount == 0) return BreachSeverity.Safe;
        if (breachCount < 10) return BreachSeverity.Low;
        if (breachCount < 100) return BreachSeverity.Medium;
        if (breachCount < 1000) return BreachSeverity.High;
        return BreachSeverity.Critical;
    }

    public static string GetBreachMessage(int breachCount) => breachCount switch
    {
        < 0 => "Unable to verify password security",
        0 => "Password not found in known breaches",
        < 10 => $"Password found in {breachCount} breach(es). Consider changing it.",
        < 100 => $"Password found in {breachCount} breaches. Change it immediately.",
        < 1000 => $"Password found in {breachCount} breaches! Change it now!",
        _ => $"Password found in {breachCount:N0} breaches! Extremely compromised - change immediately!"
    };

    private static string ComputeSha1Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        try
        {
            var hash = SHA1.HashData(bytes);
            return Convert.ToHexString(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        DisableInternet();
    }
}

public enum BreachSeverity
{
    Unknown,
    Safe,
    Low,
    Medium,
    High,
    Critical
}
