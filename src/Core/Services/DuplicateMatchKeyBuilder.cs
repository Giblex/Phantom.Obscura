using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public enum DuplicateMatchStrength
    {

        Exact = 0,

        Strong = 1,

        Likely = 2
    }

    public sealed record DuplicateKey(EntryType EntryType, string Identity, string Account)
    {
        public string Display => string.IsNullOrEmpty(Account)
            ? $"{EntryType} | {Identity}"
            : $"{EntryType} | {Identity} | {Account}";

        public bool IsUsable => !string.IsNullOrEmpty(Identity) || !string.IsNullOrEmpty(Account);

        /// <summary>
        /// The website this key belongs to, used to organise the review list into one
        /// card per site. Empty for entries with no site (a PIN, a bank account).
        /// </summary>
        public string SiteFamily { get; init; } = string.Empty;

        public string SiteDisplayName { get; init; } = string.Empty;

        public bool HasSite => !string.IsNullOrEmpty(SiteFamily);
    }

    /// <summary>
    /// Builds the grouping key used by duplicate detection.
    ///
    /// Exact string equality finds almost nothing in a real vault: the same account is
    /// routinely saved as "GitHub" / "github.com" / "Github Login", with URLs that differ
    /// by scheme, "www.", a path, or a tracking query. So identity is reduced to the
    /// registrable-ish host where a URL exists and to a de-noised title otherwise, and the
    /// account is reduced to a canonical form (case, plus-addressing).
    /// </summary>
    public static class DuplicateMatchKeyBuilder
    {

        private static readonly string[] TitleNoiseWords =
        {
            "login", "log in", "logon", "signin", "sign in", "account", "accounts",
            "password", "credentials", "www", "com", "net", "org", "co", "app", "portal"
        };

        public static DuplicateKey Build(Credential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);

            var site = SiteIdentityResolver.Resolve(credential);
            var identity = BuildIdentity(credential, site);
            var account = BuildAccount(credential);

            return new DuplicateKey(credential.EntryType, identity, account)
            {
                SiteFamily = site.SiteFamily,
                SiteDisplayName = site.DisplayName
            };
        }

        private static string BuildIdentity(Credential credential, SiteIdentity site)
        {
            // Where the entry has a real website, that site is the identity: it survives
            // "www.", "login.", a path, a port and a regional storefront, which raw host
            // or title matching does not.
            if (site.HasSite && IsSiteBackedType(credential.EntryType))
                return site.SiteFamily;

            switch (credential.EntryType)
            {
                case EntryType.WiFi:
                    return NormalizeLoose(FirstNonEmpty(credential.WiFiSSID, credential.WiFiBSSID, credential.Title));

                case EntryType.Identity:
                    return NormalizeLoose(FirstNonEmpty(credential.IdDocumentType, credential.Title));

                case EntryType.CreditCard:
                    return NormalizeLoose(FirstNonEmpty(credential.CardType, credential.Title));

                case EntryType.BankAccount:
                    return NormalizeLoose(FirstNonEmpty(credential.BankName, credential.Title));

                case EntryType.TotpGenerator:
                    return NormalizeLoose(FirstNonEmpty(credential.TotpIssuer, credential.Title));

                case EntryType.PinCode:
                    return NormalizeLoose(FirstNonEmpty(credential.PinIssuer, credential.PinLabel, credential.Title));

                case EntryType.Contact:
                    return NormalizeLoose(FirstNonEmpty(credential.ContactFullName, credential.Title));

                case EntryType.ApiKey:
                {
                    var host = NormalizeUrlToHost(FirstNonEmpty(credential.ApiEndpoint, credential.ApiDocumentationUrl, credential.Url));
                    return !string.IsNullOrEmpty(host) ? host : NormalizeTitle(credential.Title);
                }

                default:
                {
                    var host = NormalizeUrlToHost(credential.Url);
                    return !string.IsNullOrEmpty(host) ? host : NormalizeTitle(credential.Title);
                }
            }
        }

        /// <summary>
        /// Entry types whose identity is genuinely a website. A credit card or bank
        /// account may carry a URL, but two cards on the same bank's site are not the
        /// same thing, so those types keep their own identity rules.
        /// </summary>
        private static bool IsSiteBackedType(EntryType entryType) => entryType switch
        {
            EntryType.Password => true,
            EntryType.ApiKey => true,
            EntryType.TotpGenerator => true,
            _ => false
        };

        private static string BuildAccount(Credential credential) => credential.EntryType switch
        {
            EntryType.Contact => NormalizeAccount(FirstNonEmpty(credential.ContactEmail, credential.ContactPhone, credential.Username)),
            EntryType.TotpGenerator => NormalizeAccount(FirstNonEmpty(credential.TotpAccountName, credential.Username)),
            EntryType.Identity => NormalizeLoose(FirstNonEmpty(credential.IdNumber, credential.IdCardNumber)),
            EntryType.CreditCard => NormalizeLoose(credential.CardholderName),
            EntryType.BankAccount => NormalizeLoose(credential.BankAccountType),
            EntryType.WiFi => string.Empty,
            EntryType.PinCode => NormalizeLoose(credential.PinCategory),
            _ => NormalizeAccount(credential.Username)
        };

        /// <summary>
        /// Reduces a URL to its host, tolerating missing schemes, "www.", ports, paths and
        /// query strings. Returns empty when there is nothing URL-like to work with.
        /// </summary>
        public static string NormalizeUrlToHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var trimmed = url.Trim();

            if (!trimmed.Contains("://", StringComparison.Ordinal))
                trimmed = "https://" + trimmed;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return NormalizeLoose(url);

            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host[4..];

            return host;
        }

        /// <summary>
        /// Lowercases a title, drops punctuation, and removes filler words such as
        /// "login" or "account" so "GitHub Login" and "github" collapse together.
        /// Falls back to the de-punctuated title when stripping would empty it.
        /// </summary>
        public static string NormalizeTitle(string? title)
        {
            var cleaned = NormalizeLoose(title);
            if (string.IsNullOrEmpty(cleaned))
                return string.Empty;

            var words = cleaned
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !TitleNoiseWords.Contains(w, StringComparer.Ordinal))
                .ToList();

            var result = string.Join(" ", words);
            return string.IsNullOrEmpty(result) ? cleaned : result;
        }

        /// <summary>
        /// Canonicalises an account handle: case-folded, and for email addresses the
        /// "+tag" suffix is dropped so "me+shopping@x.com" matches "me@x.com".
        /// </summary>
        public static string NormalizeAccount(string? account)
        {
            if (string.IsNullOrWhiteSpace(account))
                return string.Empty;

            var trimmed = account.Trim().ToLowerInvariant();

            var at = trimmed.IndexOf('@');
            if (at <= 0)
                return trimmed;

            var local = trimmed[..at];
            var domain = trimmed[(at + 1)..];

            var plus = local.IndexOf('+');
            if (plus > 0)
                local = local[..plus];

            return $"{local}@{domain}";
        }

        /// <summary>
        /// Lowercases, replaces every non-alphanumeric run with a single space, and trims.
        /// </summary>
        public static string NormalizeLoose(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            var lastWasSpace = true;

            foreach (var c in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// How confident the grouping is, so the UI can rank obvious duplicates above
        /// merely-plausible ones instead of presenting them all as equal.
        /// </summary>
        public static DuplicateMatchStrength DetermineStrength(IReadOnlyList<Credential> group)
        {
            ArgumentNullException.ThrowIfNull(group);

            if (group.Count < 2)
                return DuplicateMatchStrength.Likely;

            var first = group[0];

            var identicalSurface = group.All(c =>
                string.Equals(Trim(c.Title), Trim(first.Title), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Trim(c.Username), Trim(first.Username), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Trim(c.Url), Trim(first.Url), StringComparison.OrdinalIgnoreCase));

            if (identicalSurface)
                return DuplicateMatchStrength.Exact;

            var key = Build(first);

            // Every member resolves to a real website and they agree on the account:
            // this is the "same login saved twice with different URLs" case.
            var allResolveToSameSite = group.All(c =>
            {
                var site = SiteIdentityResolver.Resolve(c);
                return site.HasSite && string.Equals(site.SiteFamily, key.SiteFamily, StringComparison.Ordinal);
            });

            if (allResolveToSameSite && !string.IsNullOrEmpty(key.Account))
                return DuplicateMatchStrength.Strong;

            var samePassword = !string.IsNullOrEmpty(first.Password) &&
                               group.All(c => string.Equals(c.Password, first.Password, StringComparison.Ordinal));

            if (samePassword && !string.IsNullOrEmpty(key.Identity))
                return DuplicateMatchStrength.Strong;

            return DuplicateMatchStrength.Likely;
        }

        public static string DescribeStrength(DuplicateMatchStrength strength) => strength switch
        {
            DuplicateMatchStrength.Exact => "Exact match — identical title, username and URL",
            DuplicateMatchStrength.Strong => "Strong match — same site and account",
            _ => "Likely match — review before consolidating"
        };

        private static string Trim(string? value) => (value ?? string.Empty).Trim();

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }
}
