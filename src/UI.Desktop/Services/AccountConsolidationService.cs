using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services;

/// <summary>
/// Assigns a stable service identity to separately stored account tiles. This is
/// deliberately grouping, not record merging: passwords, TOTP secrets and history
/// remain in independent credentials and can never overwrite one another.
/// </summary>
public static class AccountConsolidationService
{
    public const string ServiceKeyField = "phantom.account-service";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail"] = "google",
        ["googlemail"] = "google",
        ["google account"] = "google",
        ["microsoftonline"] = "microsoft",
        ["office365"] = "microsoft"
    };

    public static void Consolidate(Credential candidate, IEnumerable<Credential> existingCredentials)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var serviceKey = ResolveServiceKey(candidate);
        if (string.IsNullOrWhiteSpace(serviceKey))
            return;

        candidate.CustomFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        candidate.CustomFields[ServiceKeyField] = serviceKey;

        var matches = existingCredentials
            .Where(existing => existing != null && !ReferenceEquals(existing, candidate))
            .Where(existing => string.Equals(ResolveServiceKey(existing), serviceKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return;

        var sameAccount = matches.FirstOrDefault(existing =>
            !string.IsNullOrWhiteSpace(candidate.Username) &&
            string.Equals(existing.Username.Trim(), candidate.Username.Trim(), StringComparison.OrdinalIgnoreCase));
        var canonical = sameAccount ?? matches[0];

        // Sharing the established title/icon makes the independent tiles visually
        // consolidate beneath the same service without combining their secrets.
        candidate.Title = canonical.Title;
        if (string.IsNullOrWhiteSpace(candidate.Icon) && !string.IsNullOrWhiteSpace(canonical.Icon))
            candidate.Icon = canonical.Icon;
        if (string.IsNullOrWhiteSpace(candidate.IconColor) && !string.IsNullOrWhiteSpace(canonical.IconColor))
            candidate.IconColor = canonical.IconColor;
    }

    public static string ResolveServiceKey(Credential credential)
    {
        if (credential.CustomFields != null &&
            credential.CustomFields.TryGetValue(ServiceKeyField, out var stored) &&
            !string.IsNullOrWhiteSpace(stored))
            return stored.Trim().ToLowerInvariant();

        var hostKey = GetHostKey(credential.Url);
        if (!string.IsNullOrWhiteSpace(hostKey))
            return ApplyAlias(hostKey);

        if (!string.IsNullOrWhiteSpace(credential.TotpIssuer))
            return ApplyAlias(NormalizeLabel(credential.TotpIssuer));

        return ApplyAlias(NormalizeLabel(credential.Title));
    }

    private static string GetHostKey(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return string.Empty;

        var value = rawUrl.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return string.Empty;

        var labels = uri.Host.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
            return labels.FirstOrDefault() ?? string.Empty;

        // The registrable label is sufficient for UI grouping here. It correctly
        // turns accounts.google.com and mail.google.com into the same service key.
        return labels[^2];
    }

    private static string NormalizeLabel(string? value)
    {
        var normalized = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        normalized = Regex.Replace(normalized,
            "\\b(account|accounts|login|password|credential|credentials|totp|2fa|authenticator|recovery)\\b",
            string.Empty,
            RegexOptions.IgnoreCase);
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private static string ApplyAlias(string key)
        => Aliases.TryGetValue(key, out var alias) ? alias : key;
}
