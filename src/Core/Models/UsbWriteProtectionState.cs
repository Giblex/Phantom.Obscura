using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{

    public sealed class UsbWriteProtectionState
    {

        [JsonPropertyName("readOnly")]
        public bool ReadOnly { get; set; } = true;

        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; } = false;

        [JsonPropertyName("partitionTypeGuid")]
        public string? PartitionTypeGuid { get; set; }

        [JsonPropertyName("expectedSentinelFiles")]
        public List<string> ExpectedSentinelFiles { get; set; } = new();

        [JsonPropertyName("lastAsserted")]
        public DateTimeOffset? LastAsserted { get; set; }

        [JsonPropertyName("compatibilityMode")]
        public bool CompatibilityMode { get; set; } = false;
    }

    public sealed class ScrubEvent
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("quarantined")]
        public bool Quarantined { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}

