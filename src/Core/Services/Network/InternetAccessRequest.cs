using System;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// A request to open a time-scoped, host-allowlisted internet-access grant.
    /// Every outbound HTTP call in Phantom Obscura is funnelled through one of these.
    /// </summary>
    public sealed class InternetAccessRequest
    {
        /// <summary>
        /// Stable identifier for the requesting feature, used in audit logs and the
        /// consent prompt (e.g. "icon-downloader", "hibp", "update-check").
        /// </summary>
        public required string FeatureId { get; init; }

        /// <summary>
        /// Human-readable reason shown to the user in the consent prompt.
        /// Must NOT contain credentials, identifiers, or PII. Audit redaction
        /// trusts this to be safe.
        /// </summary>
        public required string UserVisibleReason { get; init; }

        /// <summary>
        /// Hostnames the grant must authorise. Wildcards not supported by design —
        /// the user must see every exact host the feature intends to reach.
        /// </summary>
        public required IReadOnlyList<string> AllowedHosts { get; init; }

        /// <summary>
        /// SPKI (SubjectPublicKeyInfo) SHA-256 pins, base64. At least one pin per host
        /// must match the server certificate or the connection is rejected.
        /// </summary>
        public required IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; init; }

        /// <summary>How long the grant is valid for. Clamped to [1 min, 60 min].</summary>
        public required TimeSpan Ttl { get; init; }

        /// <summary>
        /// If true, the consent prompt offers a "Grant for session" option in addition
        /// to a one-shot grant. Use sparingly — only for features the user really does
        /// re-enter repeatedly (e.g. icon browsing).
        /// </summary>
        public bool AllowSessionGrant { get; init; } = false;

        /// <summary>Validate before passing to the gateway.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(FeatureId))
                throw new ArgumentException("FeatureId is required.", nameof(FeatureId));
            if (string.IsNullOrWhiteSpace(UserVisibleReason))
                throw new ArgumentException("UserVisibleReason is required.", nameof(UserVisibleReason));
            if (AllowedHosts is null || AllowedHosts.Count == 0)
                throw new ArgumentException("At least one host must be allowlisted.", nameof(AllowedHosts));
            if (AllowedHosts.Any(h => string.IsNullOrWhiteSpace(h) || h.Contains('*') || h.Contains('/')))
                throw new ArgumentException("Hosts must be exact (no wildcards, no paths).", nameof(AllowedHosts));
            if (SpkiPinsByHost is null)
                throw new ArgumentException("SpkiPinsByHost is required.", nameof(SpkiPinsByHost));
            foreach (var host in AllowedHosts)
            {
                if (!SpkiPinsByHost.TryGetValue(host, out var pins) || pins is null || pins.Count == 0)
                    throw new ArgumentException(
                        $"Every allowed host must have at least one SPKI pin. Missing: '{host}'.",
                        nameof(SpkiPinsByHost));
            }
            if (Ttl < TimeSpan.FromMinutes(1))
                throw new ArgumentException("TTL must be at least 1 minute.", nameof(Ttl));
            if (Ttl > TimeSpan.FromMinutes(60))
                throw new ArgumentException("TTL must be at most 60 minutes.", nameof(Ttl));
        }
    }

    /// <summary>
    /// A live grant returned by the gateway. Pass to <see cref="IInternetGateway.CreateClient"/>
    /// to obtain a real <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    public sealed class InternetAccessGrant
    {
        public Guid Id { get; }
        public string FeatureId { get; }
        public string UserVisibleReason { get; }
        public IReadOnlyList<string> AllowedHosts { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; }
        public DateTimeOffset GrantedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        public bool Revoked { get; private set; }
        public int RequestCount { get; private set; }

        internal InternetAccessGrant(
            string featureId,
            string userVisibleReason,
            IReadOnlyList<string> allowedHosts,
            IReadOnlyDictionary<string, IReadOnlyList<string>> spkiPinsByHost,
            TimeSpan ttl)
        {
            Id = Guid.NewGuid();
            FeatureId = featureId;
            UserVisibleReason = userVisibleReason;
            AllowedHosts = allowedHosts;
            SpkiPinsByHost = spkiPinsByHost;
            GrantedAtUtc = DateTimeOffset.UtcNow;
            ExpiresAtUtc = GrantedAtUtc.Add(ttl);
        }

        public bool IsLive => !Revoked && DateTimeOffset.UtcNow < ExpiresAtUtc;

        internal void RecordRequest() => RequestCount++;

        internal void MarkRevoked() => Revoked = true;

        internal void Extend(TimeSpan extra)
        {
            if (extra <= TimeSpan.Zero) return;
            var newExpiry = DateTimeOffset.UtcNow.Add(extra);
            if (newExpiry > ExpiresAtUtc) ExpiresAtUtc = newExpiry;
        }

        public InternetAccessGrantInfo ToInfo() => new(
            Id, UserVisibleReason, AllowedHosts, GrantedAtUtc, ExpiresAtUtc, Revoked, RequestCount);
    }
}
