using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Decides whether a browser-initiated passkey assertion may proceed, and with which
    /// credential.
    ///
    /// <para>
    /// Separated from the pipe handler so it can be tested. The handler around it does I/O —
    /// reads the request, talks to Attestor, serialises a reply — and mixing that with the
    /// entitlement decision left the decision itself unreachable from a test. Everything here
    /// is a pure function of the request and the vault contents.
    /// </para>
    ///
    /// <para>
    /// This is a pre-filter, not the last line of defence: Attestor independently re-checks
    /// that the credential it holds is registered to the relying party before it signs. The
    /// value of checking here is that a request which was never going to be legitimate never
    /// reaches the authenticator, so it cannot raise a Windows Hello prompt — an attacker who
    /// could provoke prompts at will could train the user to approve them.
    /// </para>
    /// </summary>
    public static class PasskeyAssertionGate
    {
        /// <summary>Why an assertion request was refused, or that it may proceed.</summary>
        public enum Outcome
        {
            /// <summary>A passkey was found and the page is entitled to it.</summary>
            Allowed,

            /// <summary>The request was malformed — no clientDataJSON to sign over.</summary>
            MissingClientData,

            /// <summary>The page's origin does not cover the relying party it asked for.</summary>
            OriginNotEntitled,

            /// <summary>Nothing in the vault is registered for this relying party.</summary>
            NoMatchingPasskey
        }

        /// <summary>The decision, plus the credential and effective RP ID when allowed.</summary>
        public sealed record Decision(Outcome Outcome, Credential? Credential, string RelyingPartyId)
        {
            public bool IsAllowed => Outcome == Outcome.Allowed;
        }

        /// <summary>
        /// Chooses the passkey that may answer this request.
        /// </summary>
        /// <param name="credentials">The unlocked vault's credentials.</param>
        /// <param name="origin">Page origin, established by the browser from the sender tab.</param>
        /// <param name="requestedRpId">
        /// RP ID the page asked for. When blank, WebAuthn defaults it to the origin's effective
        /// domain, and that default is applied here.
        /// </param>
        /// <param name="clientDataJson">The browser's clientDataJSON; only checked for presence.</param>
        public static Decision Evaluate(
            IEnumerable<Credential>? credentials,
            string? origin,
            string? requestedRpId,
            string? clientDataJson)
        {
            if (string.IsNullOrWhiteSpace(clientDataJson))
                return new Decision(Outcome.MissingClientData, null, string.Empty);

            var rpId = requestedRpId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rpId)
                && Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                rpId = originUri.Host;
            }

            if (!RelyingPartyId.IsPermittedForOrigin(origin, rpId))
                return new Decision(Outcome.OriginNotEntitled, null, rpId);

            // Most recently used first, so a site with several stored passkeys answers with the
            // one the user actually signs in with rather than whichever the vault lists first.
            var match = (credentials ?? Enumerable.Empty<Credential>())
                .Where(c => c.IsPasskey
                            && !string.IsNullOrWhiteSpace(c.AttestorPasskeyReference)
                            && RelyingPartyId.Resolve(ExtractHost(c.Url), rpId) != null)
                .OrderByDescending(c => c.LastUsedUtc ?? DateTime.MinValue)
                .FirstOrDefault();

            return match == null
                ? new Decision(Outcome.NoMatchingPasskey, null, rpId)
                : new Decision(Outcome.Allowed, match, rpId);
        }

        /// <summary>
        /// Host portion of a stored credential URL. Stored URLs are user-entered and are
        /// frequently bare hosts rather than absolute URLs, so a parse failure falls back to
        /// treating the value as a host rather than discarding the credential.
        /// </summary>
        private static string ExtractHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var value = url.Trim();

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;

            if (Uri.TryCreate("https://" + value, UriKind.Absolute, out var inferred)
                && !string.IsNullOrEmpty(inferred.Host))
            {
                return inferred.Host;
            }

            return value;
        }

        /// <summary>A message safe to hand back to the page for a refusal.</summary>
        public static string DescribeRefusal(Outcome outcome) => outcome switch
        {
            Outcome.MissingClientData => "Missing clientDataJSON",
            Outcome.OriginNotEntitled => "This site may not request a passkey for that relying party.",
            Outcome.NoMatchingPasskey => "No passkey in this vault is registered for this site.",
            _ => "The passkey request could not be completed."
        };
    }
}
