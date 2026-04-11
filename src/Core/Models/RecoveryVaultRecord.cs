using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Describes the recovery container bound to a vault setup.
    /// Stored as an individually encrypted USB sidecar artifact.
    /// </summary>
    public sealed class RecoveryVaultRecord
    {
        [JsonPropertyName("bindingId")]
        public string BindingId { get; set; } = string.Empty;

        [JsonPropertyName("bindingGuid")]
        public string BindingGuid { get; set; } = string.Empty;

        [JsonPropertyName("recoveryContainerPath")]
        public string RecoveryContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("recoveryVaultPath")]
        public string? RecoveryVaultPath { get; set; }
            = null;

        [JsonPropertyName("protectionTier")]
        public VaultProtectionTier ProtectionTier { get; set; }
            = VaultProtectionTier.StandardSecure;

        [JsonPropertyName("effectiveStorageTransport")]
        public VaultStorageTransport EffectiveStorageTransport { get; set; }
            = VaultStorageTransport.FileSystem;

        [JsonPropertyName("recoveryContainerSizeBytes")]
        public long RecoveryContainerSizeBytes { get; set; }
            = 0;

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; }
            = DateTimeOffset.UtcNow;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
            = null;
    }
}
