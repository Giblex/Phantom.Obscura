using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    /// <summary>
    /// The site an entry belongs to, at two levels of strictness.
    ///
    /// <see cref="RegistrableDomain"/> is the strict key: consolidation only ever merges
    /// entries that agree on it, so "example.com" and "example.co.uk" stay separate
    /// companies unless an explicit affiliation says otherwise.
    ///
    /// <see cref="SiteFamily"/> is the loose key, used purely to organise the review list
    /// into one card per website. Grouping for display never causes a merge.
    /// </summary>
    public sealed record SiteIdentity(
        string Host,
        string RegistrableDomain,
        string SiteFamily,
        string DisplayName)
    {
        public bool HasSite => !string.IsNullOrEmpty(RegistrableDomain);

        public static SiteIdentity None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>
    /// Resolves the website an entry belongs to, the way a password manager's site list
    /// does: strip the auth subdomain, reduce to the registrable domain using the public
    /// suffix, then fold well-known service families and regional storefronts together.
    /// </summary>
    public static class SiteIdentityResolver
    {

        /// <summary>
        /// Multi-label public suffixes. A registrable domain is the label immediately to
        /// the left of the suffix, so "amazon.co.uk" must not be reduced to "co.uk".
        /// This is a curated subset of the Public Suffix List covering the suffixes that
        /// actually show up in consumer vaults; anything unlisted falls back to the last
        /// two labels, which is correct for single-label TLDs such as ".com".
        /// </summary>
        private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.Ordinal)
        {
            "co.uk", "org.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk", "ac.uk", "gov.uk",
            "com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au", "asn.au",
            "co.nz", "net.nz", "org.nz", "govt.nz", "ac.nz",
            "com.br", "net.br", "org.br", "gov.br",
            "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
            "co.kr", "or.kr", "ne.kr", "go.kr",
            "co.in", "net.in", "org.in", "gen.in", "firm.in", "gov.in",
            "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn",
            "com.mx", "org.mx", "gob.mx",
            "com.ar", "net.ar", "org.ar", "gob.ar",
            "com.sg", "net.sg", "org.sg", "gov.sg",
            "com.hk", "net.hk", "org.hk", "gov.hk",
            "com.tw", "net.tw", "org.tw", "gov.tw",
            "co.za", "net.za", "org.za", "gov.za",
            "com.tr", "net.tr", "org.tr", "gov.tr",
            "co.il", "net.il", "org.il", "gov.il",
            "com.pl", "net.pl", "org.pl", "gov.pl",
            "com.es", "org.es", "gob.es",
            "co.id", "web.id", "or.id", "go.id",
            "com.my", "net.my", "org.my", "gov.my",
            "com.ph", "net.ph", "org.ph", "gov.ph",
            "com.vn", "net.vn", "org.vn", "gov.vn",
            "com.ua", "net.ua", "org.ua", "gov.ua",
            "com.sa", "net.sa", "org.sa", "gov.sa",
            "co.th", "in.th", "go.th",
            "com.pk", "net.pk", "org.pk", "gov.pk",
            "com.ng", "net.ng", "org.ng", "gov.ng",
            "com.eg", "net.eg", "org.eg", "gov.eg",
            "com.co", "net.co", "nom.co"
        };

        /// <summary>
        /// Subdomains that identify a sign-in endpoint rather than a distinct site, so
        /// "login.example.com" and "example.com" describe the same account.
        /// </summary>
        private static readonly HashSet<string> AuthSubdomains = new(StringComparer.Ordinal)
        {
            "www", "login", "signin", "sign-in", "logon", "auth", "sso", "account", "accounts",
            "id", "identity", "secure", "my", "portal", "app", "apps", "web", "m", "mobile",
            "console", "dashboard", "member", "members", "user", "users", "profile", "oauth"
        };

        /// <summary>
        /// Explicit service affiliations. Each entry maps a registrable domain onto the
        /// family it belongs to, so an account saved against one property of a service is
        /// recognised as the same site as another. Deliberately conservative and explicit:
        /// nothing is folded together by guesswork, because a wrong affiliation would
        /// invite merging two genuinely different accounts.
        /// </summary>
        private static readonly Dictionary<string, string> Affiliations = new(StringComparer.Ordinal)
        {
            ["google.com"] = "google",
            ["gmail.com"] = "google",
            ["googlemail.com"] = "google",
            ["youtube.com"] = "google",

            ["microsoft.com"] = "microsoft",
            ["live.com"] = "microsoft",
            ["outlook.com"] = "microsoft",
            ["hotmail.com"] = "microsoft",
            ["office.com"] = "microsoft",
            ["office365.com"] = "microsoft",
            ["msn.com"] = "microsoft",
            ["azure.com"] = "microsoft",

            ["apple.com"] = "apple",
            ["icloud.com"] = "apple",
            ["me.com"] = "apple",

            ["facebook.com"] = "meta",
            ["meta.com"] = "meta",
            ["instagram.com"] = "meta",
            ["messenger.com"] = "meta",
            ["whatsapp.com"] = "meta",

            ["amazon.com"] = "amazon",
            ["amazon.co.uk"] = "amazon",
            ["amazon.com.au"] = "amazon",
            ["amazon.ca"] = "amazon",
            ["amazon.de"] = "amazon",
            ["amazon.fr"] = "amazon",
            ["amazon.es"] = "amazon",
            ["amazon.it"] = "amazon",
            ["amazon.co.jp"] = "amazon",
            ["amazon.in"] = "amazon",
            ["aws.amazon.com"] = "amazon",

            ["ebay.com"] = "ebay",
            ["ebay.co.uk"] = "ebay",
            ["ebay.com.au"] = "ebay",
            ["ebay.de"] = "ebay",

            ["paypal.com"] = "paypal",
            ["paypal.me"] = "paypal",

            ["github.com"] = "github",
            ["githubusercontent.com"] = "github",

            ["atlassian.com"] = "atlassian",
            ["atlassian.net"] = "atlassian",
            ["jira.com"] = "atlassian",
            ["bitbucket.org"] = "atlassian",

            ["adobe.com"] = "adobe",
            ["adobelogin.com"] = "adobe",

            ["x.com"] = "x",
            ["twitter.com"] = "x"
        };

        public static SiteIdentity FromUrl(string? url)
        {
            var host = ExtractHost(url);
            if (string.IsNullOrEmpty(host))
                return SiteIdentity.None;

            var stripped = StripAuthSubdomains(host);
            var registrable = GetRegistrableDomain(stripped);

            if (string.IsNullOrEmpty(registrable))
                return SiteIdentity.None;

            var family = Affiliations.TryGetValue(registrable, out var affiliated)
                ? affiliated
                : registrable;

            return new SiteIdentity(host, registrable, family, BuildDisplayName(registrable, family));
        }

        /// <summary>
        /// Resolves the site for a credential, preferring its URL and falling back to any
        /// URL-shaped field for the entry type.
        /// </summary>
        public static SiteIdentity Resolve(Credential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);

            var fromUrl = FromUrl(credential.Url);
            if (fromUrl.HasSite)
                return fromUrl;

            if (credential.EntryType == EntryType.ApiKey)
            {
                var fromEndpoint = FromUrl(credential.ApiEndpoint);
                if (fromEndpoint.HasSite)
                    return fromEndpoint;

                var fromDocs = FromUrl(credential.ApiDocumentationUrl);
                if (fromDocs.HasSite)
                    return fromDocs;
            }

            // A title is often just the site written out ("github.com", "Amazon UK").
            var fromTitle = FromUrl(credential.Title);
            return fromTitle.HasSite ? fromTitle : SiteIdentity.None;
        }

        /// <summary>
        /// Pulls the host out of a URL, tolerating a missing scheme, credentials, a port,
        /// a path and a query string. Returns empty when the input is not host-shaped.
        /// </summary>
        public static string ExtractHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var trimmed = url.Trim();

            if (trimmed.Contains(' ', StringComparison.Ordinal) && !trimmed.Contains("://", StringComparison.Ordinal))
                return string.Empty;

            if (!trimmed.Contains("://", StringComparison.Ordinal))
                trimmed = "https://" + trimmed;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return string.Empty;

            var host = uri.Host.ToLowerInvariant().Trim('.');

            // A bare word ("Amazon") parses as a host but is not a site.
            if (!host.Contains('.', StringComparison.Ordinal))
                return string.Empty;

            return host;
        }

        /// <summary>
        /// Removes leading sign-in subdomains, never stripping so far that the registrable
        /// domain itself would be consumed.
        /// </summary>
        public static string StripAuthSubdomains(string host)
        {
            if (string.IsNullOrEmpty(host))
                return string.Empty;

            var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
            var suffixLabelCount = GetSuffixLabelCount(labels);

            // Keep at least the registrable domain: suffix labels plus one.
            var minimumLabels = suffixLabelCount + 1;

            while (labels.Count > minimumLabels && AuthSubdomains.Contains(labels[0]))
            {
                labels.RemoveAt(0);
            }

            return string.Join('.', labels);
        }

        /// <summary>
        /// Reduces a host to its registrable domain (the label to the left of the public
        /// suffix, plus the suffix): "a.b.example.co.uk" becomes "example.co.uk".
        /// </summary>
        public static string GetRegistrableDomain(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            var labels = host.Trim().ToLowerInvariant()
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (labels.Count < 2)
                return string.Empty;

            var suffixLabelCount = GetSuffixLabelCount(labels);
            var take = suffixLabelCount + 1;

            if (labels.Count < take)
                return string.Join('.', labels);

            return string.Join('.', labels.Skip(labels.Count - take));
        }

        private static int GetSuffixLabelCount(IReadOnlyList<string> labels)
        {
            if (labels.Count >= 3)
            {
                var lastTwo = $"{labels[^2]}.{labels[^1]}";
                if (MultiLabelSuffixes.Contains(lastTwo))
                    return 2;
            }

            return 1;
        }

        private static string BuildDisplayName(string registrableDomain, string family)
        {
            // For an affiliated family show the family name, since the individual domains
            // ("gmail.com", "youtube.com") are all the same account to the user.
            if (!string.Equals(registrableDomain, family, StringComparison.Ordinal))
                return Capitalise(family);

            var firstLabel = registrableDomain.Split('.').FirstOrDefault() ?? registrableDomain;
            return Capitalise(firstLabel);
        }

        private static string Capitalise(string value)
            => string.IsNullOrEmpty(value)
                ? value
                : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
