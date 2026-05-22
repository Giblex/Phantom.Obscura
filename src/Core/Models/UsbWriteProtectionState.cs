using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Captures the intended write-protection posture for a Phantom Obscura bound
    /// USB drive. Persisted in <see cref="VaultManifest"/> so the desired state
    /// can be re-asserted across mounts / crashes.
    /// </summary>
    public sealed class UsbWriteProtectionState
    {
        /// <summary>
        /// Whether the GPT ReadOnly attribute (bit 60) should be set on the
        /// partition outside of an active Phantom Obscura unlock session.
        /// </summary>
        [JsonPropertyName("readOnly")]
        public bool ReadOnly { get; set; } = true;

        /// <summary>
        /// Whether the GPT Hidden attribute (bit 62) should be set. Hidden
        /// partitions do not get a default drive letter and are skipped by
        /// Windows Search. Off by default for backwards compatibility.
        /// </summary>
        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; } = false;

        /// <summary>
        /// Partition type GUID applied at bind. <c>null</c> or the standard
        /// Basic Data GUID means no re-tag was performed.
        /// </summary>
        [JsonPropertyName("partitionTypeGuid")]
        public string? PartitionTypeGuid { get; set; }

        /// <summary>
        /// File names of OS-indexer-suppression sentinels that Phantom Obscura
        /// expects to find at the drive root. Compared against actuals at
        /// unlock; missing/altered entries raise a tamper hint.
        /// </summary>
        [JsonPropertyName("expectedSentinelFiles")]
        public List<string> ExpectedSentinelFiles { get; set; } = new();

        /// <summary>
        /// Last time the desired state was successfully asserted against the
        /// drive (RO bit set + sentinels present). Used to detect drift.
        /// </summary>
        [JsonPropertyName("lastAsserted")]
        public DateTimeOffset? LastAsserted { get; set; }

        /// <summary>
        /// When true, Phantom Obscura skips the partition type GUID re-tag so
        /// the drive remains browsable on non-PO machines. The RO/Hidden bits
        /// and sentinel files are still applied.
        /// </summary>
        [JsonPropertyName("compatibilityMode")]
        public bool CompatibilityMode { get; set; } = false;
    }

    /// <summary>
    /// Audit entry written every time the USB junk scrubber removes or
    /// quarantines a file/folder injected by an OS indexer.
    /// </summary>
    public sealed class ScrubEvent
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Root-relative path that was removed.</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>Total bytes removed (sum across files if a folder).</summary>
        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        /// <summary>SHA-256 of file contents (single-file targets only).</summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        /// <summary>
        /// True if the entry was moved to .phantom_quarantine/ rather than
        /// hard-deleted. Quarantined items are purged after the configured
        /// retention window.
        /// </summary>
        [JsonPropertyName("quarantined")]
        public bool Quarantined { get; set; }

        /// <summary>Short tag indicating which class of OS junk triggered removal.</summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
