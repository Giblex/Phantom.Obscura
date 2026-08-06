using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Licensing
{
    /// <summary>
    /// The signed payload of a premium license. These claims are serialized to
    /// canonical JSON, base64url-encoded, and Ed25519-signed by the offline
    /// licensing authority. The client only ever verifies — it never trusts
    /// unsigned or self-issued claims (except the env-gated dev authority).
    /// </summary>
    public sealed class LicenseClaims
    {
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; set; } = string.Empty;

        [JsonPropertyName("tier")]
        public PremiumTier Tier { get; set; } = PremiumTier.Free;

        /// <summary>
        /// USB/device binding identifier this license is issued for. Verified
        /// against the active vault's binding at unlock time. Null = unbound
        /// (not issued in production, but useful for dev/manual keys).
        /// </summary>
        [JsonPropertyName("usbBindingId")]
        public string? UsbBindingId { get; set; }

        [JsonPropertyName("issuedUtc")]
        public DateTimeOffset IssuedUtc { get; set; }

        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset ExpiresUtc { get; set; }

        /// <summary>
        /// Explicit feature grants. Empty means "all features for the tier".
        /// Stored as enum names so the token is forward-compatible.
        /// </summary>
        [JsonPropertyName("features")]
        public List<string> Features { get; set; } = new();
    }
}
