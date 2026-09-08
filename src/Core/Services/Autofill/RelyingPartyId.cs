using System;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Decides which relying party ID a passkey operation may legitimately run under, given
    /// the site actually being visited and the RP recorded on the credential.
    ///
    /// <para>
    /// This is a security boundary, not a formatting helper. A passkey assertion is a signed
    /// statement "the holder of this credential is present at <c>rpId</c>". If a page at
    /// <c>evil.example</c> could obtain an assertion scoped to <c>bank.example</c>, it could
    /// replay it to the bank and sign in as the user. The rule below is what stops that: an
    /// RP is accepted only when it equals the observed domain or is a registrable-domain
    /// suffix of it, per WebAuthn L2 §5.1.4.
    /// </para>
    ///
    /// <para>
    /// It lives in Core, and is shared, because it is applied on two independent paths — the
    /// desktop USB auto-inject flow and the browser extension relay. Two copies of a rule
    /// like this drift, and a drifted copy fails open.
    /// </para>
    /// </summary>
    public static class RelyingPartyId
    {
        /// <summary>
        /// The RP ID to use, or <c>null</c> when the stored credential does not belong to the
        /// site being visited and the operation must be refused.
        /// </summary>
        /// <param name="observedDomain">Host of the page requesting the operation.</param>
        /// <param name="storedRelyingPartyId">RP ID recorded on the credential, if any.</param>
        public static string? Resolve(string? observedDomain, string? storedRelyingPartyId)
        {
            var observed = Normalize(observedDomain);
            var stored = Normalize(storedRelyingPartyId);

            // No observed domain to check against — fall back to the stored value only if that
            // is all we have, which matches the native (non-browser) case.
            if (observed.Length == 0)
                return stored.Length == 0 ? null : stored;

            if (stored.Length == 0)
                return observed;

            if (string.Equals(stored, observed, StringComparison.Ordinal))
                return stored;

            // Registrable domain suffix: "example.com" is valid for "login.example.com".
            // The leading dot matters — without it "notexample.com" would match "example.com".
            if (observed.EndsWith("." + stored, StringComparison.Ordinal))
                return stored;

            return null;
        }

        /// <summary>
        /// Whether <paramref name="rpId"/> may be asserted by a page served from
        /// <paramref name="origin"/>. Used on the browser path, where the caller holds a
        /// trustworthy origin rather than a bare hostname.
        /// </summary>
        public static bool IsPermittedForOrigin(string? origin, string? rpId)
        {
            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(rpId))
                return false;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            // WebAuthn is restricted to secure contexts. localhost is treated as potentially
            // trustworthy so local development works without weakening the rule for the web.
            var isSecure = uri.Scheme == Uri.UriSchemeHttps
                           || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                           || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
            if (!isSecure)
                return false;

            return Resolve(uri.Host, rpId) != null;
        }

        private static string Normalize(string? value) =>
            (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
    }
}
