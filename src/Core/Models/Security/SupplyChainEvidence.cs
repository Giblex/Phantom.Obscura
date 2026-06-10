using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{

    public sealed class SupplyChainEvidence
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("applicationHash")]
        public string ApplicationHash { get; set; } = string.Empty;

        [JsonPropertyName("applicationVersion")]
        public string ApplicationVersion { get; set; } = string.Empty;

        [JsonPropertyName("buildConfiguration")]
        public string BuildConfiguration { get; set; } = string.Empty;

        [JsonPropertyName("commitHash")]
        public string? CommitHash { get; set; }

        [JsonPropertyName("buildTimestamp")]
        public DateTimeOffset? BuildTimestamp { get; set; }

        [JsonPropertyName("hadDebugSymbols")]
        public bool HadDebugSymbols { get; set; }

        [JsonPropertyName("codeSigningKeyId")]
        public string? CodeSigningKeyId { get; set; }

        [JsonPropertyName("dependencyHashes")]
        public Dictionary<string, string> DependencyHashes { get; set; } = new();

        [JsonPropertyName("knownGoodFingerprint")]
        public ToolFingerprint? KnownGoodFingerprint { get; set; }

        [JsonPropertyName("accessHistory")]
        public List<ToolAccessRecord> AccessHistory { get; set; } = new();

        [JsonPropertyName("maxAccessHistoryEntries")]
        public int MaxAccessHistoryEntries { get; set; } = 50;

        [JsonPropertyName("lastCapturedUtc")]
        public DateTimeOffset LastCapturedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("osvScanResults")]
        public OsvScanSnapshot? OsvScanResults { get; set; }
    }

    public sealed class ToolFingerprint
    {

        [JsonPropertyName("binaryHash")]
        public string BinaryHash { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }

        [JsonPropertyName("establishedUtc")]
        public DateTimeOffset EstablishedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("establishedBy")]
        public string? EstablishedBy { get; set; }
    }

    public sealed class ToolAccessRecord
    {

        [JsonPropertyName("toolHash")]
        public string ToolHash { get; set; } = string.Empty;

        [JsonPropertyName("toolVersion")]
        public string ToolVersion { get; set; } = string.Empty;

        [JsonPropertyName("accessType")]
        public VaultAccessType AccessType { get; set; }

        [JsonPropertyName("accessedUtc")]
        public DateTimeOffset AccessedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        [JsonPropertyName("matchedKnownGood")]
        public bool? MatchedKnownGood { get; set; }

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VaultAccessType
    {

        Unlock,

        Read,

        Write,

        Export,

        Provision,

        Rekey,

        Recovery
    }

    public sealed class OsvScanSnapshot
    {

        [JsonPropertyName("scannedUtc")]
        public DateTimeOffset ScannedUtc { get; set; }

        [JsonPropertyName("osvDatabaseVersion")]
        public string? OsvDatabaseVersion { get; set; }

        [JsonPropertyName("dependenciesScanned")]
        public int DependenciesScanned { get; set; }

        [JsonPropertyName("vulnerabilitiesFound")]
        public int VulnerabilitiesFound { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("vulnerabilityIds")]
        public List<string> VulnerabilityIds { get; set; } = new();
    }
}

