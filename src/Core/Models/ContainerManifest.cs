using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Container-level metadata stored in cleartext between the static header
    /// and the encrypted payload. Signed with HMAC-SHA256 derived from the
    /// container key for integrity verification.
    ///
    /// This is distinct from <see cref="VaultManifest"/> which holds vault
    /// configuration and is encrypted inside the payload.
    ///
    /// v4 format: [Static Header][ContainerManifest + HMAC][Encrypted Blocks][VaultManifest Footer]
    /// </summary>
    public sealed class ContainerManifest
    {
        [JsonPropertyName("containerId")]
        public Guid ContainerId { get; set; } = Guid.NewGuid();

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("encryptionAlgorithm")]
        public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";

        [JsonPropertyName("kdfAlgorithm")]
        public string KdfAlgorithm { get; set; } = "Argon2id";

        [JsonPropertyName("kdfIterations")]
        public int KdfIterations { get; set; }

        [JsonPropertyName("kdfMemoryKb")]
        public int KdfMemoryKb { get; set; }

        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;

        [JsonPropertyName("payloadSize")]
        public long PayloadSize { get; set; }

        /// <summary>
        /// Base64-encoded SHA-256 hash of all encrypted data block bytes
        /// (nonce + tag + ciphertext for each block). Verified before
        /// decryption to detect payload corruption or tampering.
        /// </summary>
        [JsonPropertyName("payloadHash")]
        public string PayloadHash { get; set; } = string.Empty;

        [JsonPropertyName("publicKeyId")]
        public string? PublicKeyId { get; set; }

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("deviceAttestation")]
        public string? DeviceAttestation { get; set; }
    }
}
