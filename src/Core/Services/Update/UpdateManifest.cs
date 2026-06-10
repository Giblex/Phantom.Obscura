using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Wire-format manifest. Bytes are signed exactly as fetched; do not
    /// re-canonicalize before verifying. After signature passes, this DTO
    /// is parsed for downstream consumers (see <see cref="UpdateInfo"/>).
    ///
    /// Schema 1 is the only currently-supported value. Future schema bumps
    /// require a code change to the verifier.
    /// </summary>
    public sealed class UpdateManifest
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        /// <summary>"stable" or "beta". Case-insensitive. Required.</summary>
        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        /// <summary>Three- or four-segment SemVer-shaped version. Required.</summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>UTC release timestamp. Required.</summary>
        [JsonPropertyName("releasedAtUtc")]
        public DateTime ReleasedAtUtc { get; set; }

        /// <summary>
        /// If present, clients running a version older than this must NOT auto-apply
        /// this update (they need a chained intermediate release first). Optional.
        /// </summary>
        [JsonPropertyName("minPreviousVersion")]
        public string? MinPreviousVersion { get; set; }

        [JsonPropertyName("asset")]
        public UpdateAssetManifest? Asset { get; set; }

        /// <summary>
        /// Optional Authenticode pinning. Even with Ed25519 signature on the
        /// manifest, we additionally pin the .exe's signer cert subject +
        /// SHA-256 thumbprint as defence in depth.
        /// </summary>
        [JsonPropertyName("authenticode")]
        public UpdateAuthenticodeManifest? Authenticode { get; set; }
    }

    public sealed class UpdateAssetManifest
    {
        /// <summary>Direct https URL. Host must be the configured update host.</summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>Expected file size in bytes. Required and bound-checked at download.</summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>Base64 SHA-256 of the asset bytes. Required.</summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    public sealed class UpdateAuthenticodeManifest
    {
        /// <summary>Expected signer Subject. Compared case-insensitive, exact match.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>SHA-256 thumbprint of the signing certificate, hex (no separators).</summary>
        [JsonPropertyName("thumbprintSha256")]
        public string? ThumbprintSha256 { get; set; }
    }
}
