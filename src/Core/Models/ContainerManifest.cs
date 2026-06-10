using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{

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

