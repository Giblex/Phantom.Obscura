using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{

    public sealed class StorageTierProvisioningRecord
    {
        [JsonPropertyName("protectionTier")]
        public VaultProtectionTier ProtectionTier { get; set; }
            = VaultProtectionTier.StandardSecure;

        [JsonPropertyName("effectiveTransport")]
        public VaultStorageTransport EffectiveTransport { get; set; }
            = VaultStorageTransport.FileSystem;

        [JsonPropertyName("requestedTransport")]
        public VaultStorageTransport? RequestedTransport { get; set; }
            = null;

        [JsonPropertyName("reversibleMigrationEnabled")]
        public bool ReversibleMigrationEnabled { get; set; }
            = true;

        [JsonPropertyName("currentRootContainerPath")]
        public string CurrentRootContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("currentVaultContainerPath")]
        public string CurrentVaultContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("currentObjectContainerPath")]
        public string CurrentObjectContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("currentRecoveryContainerPath")]
        public string CurrentRecoveryContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("currentMasterVolumePath")]
        public string? CurrentMasterVolumePath { get; set; }
            = null;

        [JsonPropertyName("recommendedRollbackTransport")]
        public VaultStorageTransport RecommendedRollbackTransport { get; set; }
            = VaultStorageTransport.FileSystem;

        [JsonPropertyName("recoveryWorkspacePath")]
        public string? RecoveryWorkspacePath { get; set; }
            = null;

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; }
            = DateTimeOffset.UtcNow;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
            = null;
    }
}

