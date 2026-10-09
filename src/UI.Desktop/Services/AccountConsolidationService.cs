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

    // Normalisation and aliasing live in ServiceAliases so grouping and search agree on what
    // counts as the same service. A brand added there is understood by both at once, rather
    // than search knowing about a service that grouping has never heard of.
    private static string NormalizeLabel(string? value) => ServiceAliases.Normalise(value);

    private static string ApplyAlias(string key) => ServiceAliases.CanonicaliseNormalised(key);
}
