using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhantomVault.UI.Services;

/// <summary>
/// Maps the many names one service goes by onto a single canonical name, so "gmail" and "google"
/// are understood to be the same thing.
///
/// Two features need this and they must agree: account consolidation (which tiles group together)
/// and search (what a query finds). Keeping one table means a brand added for one is immediately
/// understood by the other, instead of search knowing about a service that grouping does not.
///
/// This is a convenience layer only. Nothing here affects what is stored or how anything is
/// encrypted — getting an alias wrong makes a search less helpful, never less safe.
/// </summary>
public static class ServiceAliases
{
    /// <summary>
    /// Alternate name -> canonical service. Keys are already normalised (lower case, no
    /// punctuation), because that is the form <see cref="Canonicalise"/> looks them up in.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Google
        ["gmail"] = "google",
        ["googlemail"] = "google",
        ["google account"] = "google",
        ["google mail"] = "google",
        ["gdrive"] = "google",
        ["google drive"] = "google",
        ["youtube"] = "google",
        ["gsuite"] = "google",
        ["google workspace"] = "google",

        // Microsoft
        ["microsoftonline"] = "microsoft",
        ["office365"] = "microsoft",
        ["office 365"] = "microsoft",
        ["outlook"] = "microsoft",
        ["hotmail"] = "microsoft",
        ["live"] = "microsoft",
        ["msn"] = "microsoft",
        ["onedrive"] = "microsoft",
        ["xbox"] = "microsoft",
        ["azure"] = "microsoft",
        ["skype"] = "microsoft",

        // Apple
        ["icloud"] = "apple",
        ["itunes"] = "apple",
        ["apple id"] = "apple",
        ["appleid"] = "apple",
        ["app store"] = "apple",

        // Meta
        ["facebook"] = "meta",
        ["fb"] = "meta",
        ["instagram"] = "meta",
        ["insta"] = "meta",
        ["ig"] = "meta",
        ["whatsapp"] = "meta",
        ["messenger"] = "meta",
        ["threads"] = "meta",

        // Amazon
        ["aws"] = "amazon",
        ["amazon web services"] = "amazon",
        ["prime"] = "amazon",
        ["prime video"] = "amazon",
        ["audible"] = "amazon",
        ["kindle"] = "amazon",
        ["twitch"] = "amazon",

        // Other common consumer services with more than one name
        ["x"] = "twitter",
        ["x com"] = "twitter",
        ["ms teams"] = "microsoft",
        ["teams"] = "microsoft",
        ["gh"] = "github",
        ["atlassian"] = "jira",
        ["confluence"] = "jira",
        ["bitbucket"] = "jira",
        ["adobe creative cloud"] = "adobe",
        ["creative cloud"] = "adobe",
        ["photoshop"] = "adobe",
        ["paypal me"] = "paypal",
        ["disney plus"] = "disney",
        ["disneyplus"] = "disney",
        ["disney+"] = "disney",
        ["playstation"] = "sony",
        ["psn"] = "sony",
        ["ea play"] = "ea",
        ["origin"] = "ea",
        ["battle net"] = "blizzard",
        ["battlenet"] = "blizzard"
    };

    /// <summary>
    /// Canonical service -> every name it is known by. Built once from <see cref="Aliases"/> so the
    /// two directions cannot disagree.
    /// </summary>
    private static readonly Dictionary<string, string[]> Expansions =
        Aliases
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Key).Append(group.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Strips punctuation, case and the filler words people put in entry titles ("login",
    /// "account", "2FA") so "Google Account" and "google" normalise to the same string.
    /// </summary>
    public static string Normalise(string? value)
    {
        var normalised = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        normalised = Regex.Replace(
            normalised,
            "\\b(account|accounts|login|logins|password|credential|credentials|totp|2fa|authenticator|recovery|my)\\b",
            string.Empty,
            RegexOptions.IgnoreCase);
        return Regex.Replace(normalised, "\\s+", " ").Trim();
    }

    /// <summary>The canonical service name for a raw label, or the normalised label itself.</summary>
    public static string Canonicalise(string? value)
    {
        var key = Normalise(value);
        if (key.Length == 0)
            return string.Empty;

        if (Aliases.TryGetValue(key, out var alias))
            return alias;

        // "gmail work" and the like: the first word still identifies the service.
        int space = key.IndexOf(' ');
        if (space > 0)
        {
            var head = key[..space];
            if (Aliases.TryGetValue(head, out var headAlias))
                return headAlias;
        }

        return key;
    }

    /// <summary>Canonical form of an already-normalised key, for callers that normalise first.</summary>
    public static string CanonicaliseNormalised(string normalisedKey)
        => Aliases.TryGetValue(normalisedKey, out var alias) ? alias : normalisedKey;

    /// <summary>
    /// Every name the service behind this term is known by, including the term itself. Searching
    /// for "google" expands to gmail, youtube, drive and so on, so an entry titled "Gmail" is found.
    /// </summary>
    public static IReadOnlyList<string> Expand(string? value)
    {
        var canonical = Canonicalise(value);
        if (canonical.Length == 0)
            return Array.Empty<string>();

        return Expansions.TryGetValue(canonical, out var names)
            ? names
            : new[] { canonical };
    }

    /// <summary>
    /// True when two labels name the same service — directly, or through an alias.
    /// </summary>
    public static bool SameService(string? left, string? right)
    {
        var a = Canonicalise(left);
        var b = Canonicalise(right);
        return a.Length > 0 && b.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
