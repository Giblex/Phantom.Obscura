using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Manifest for tracking USB-Desktop synchronization
    /// </summary>
    public class SyncManifest
    {
        /// <summary>
        /// Schema version for the manifest
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Last successful sync timestamp
        /// </summary>
        public DateTime LastSyncUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Device identifier that performed the last sync
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Vault checksums for integrity verification
        /// </summary>
        public Dictionary<string, VaultChecksum> Vaults { get; set; } = new();

        /// <summary>
        /// Policy checksums
        /// </summary>
        public string PoliciesChecksum { get; set; } = string.Empty;

        /// <summary>
        /// Number of credentials in each vault
        /// </summary>
        public Dictionary<string, int> RecordCounts { get; set; } = new();

        /// <summary>
        /// List of authorized machines
        /// </summary>
        public List<string> AuthorizedMachines { get; set; } = new();

        /// <summary>
        /// Global settings
        /// </summary>
        public GlobalSyncSettings Settings { get; set; } = new();
    }

    public class VaultChecksum
    {
        public string Id { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public DateTime LastModifiedUtc { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }

    public class GlobalSyncSettings
    {
        /// <summary>
        /// Auto-lock timeout in seconds
        /// </summary>
        public int AutoLockTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Require PIN for sensitive operations
        /// </summary>
        public bool RequirePinForSensitive { get; set; } = true;

        /// <summary>
        /// Enable tamper detection
        /// </summary>
        public bool EnableTamperDetection { get; set; } = true;

        /// <summary>
        /// Sync strategy preference
        /// </summary>
        public SyncStrategy PreferredSyncStrategy { get; set; } = SyncStrategy.Delta;
    }

    public enum SyncStrategy
    {
        /// <summary>
        /// Only sync changed records (fast)
        /// </summary>
        Delta,

        /// <summary>
        /// Sync everything (slow but thorough)
        /// </summary>
        Full
    }
}
