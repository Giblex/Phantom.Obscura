using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{

    public class SyncManifest
    {

        public string Version { get; set; } = "1.0.0";

        public DateTime LastSyncUtc { get; set; } = DateTime.UtcNow;

        public string DeviceId { get; set; } = string.Empty;

        public Dictionary<string, VaultChecksum> Vaults { get; set; } = new();

        public string PoliciesChecksum { get; set; } = string.Empty;

        public Dictionary<string, int> RecordCounts { get; set; } = new();

        public List<string> AuthorizedMachines { get; set; } = new();

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

        public int AutoLockTimeoutSeconds { get; set; } = 300;

        public bool RequirePinForSensitive { get; set; } = true;

        public bool EnableTamperDetection { get; set; } = true;

        public SyncStrategy PreferredSyncStrategy { get; set; } = SyncStrategy.Delta;
    }

    public enum SyncStrategy
    {

        Delta,

        Full
    }
}

