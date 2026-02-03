using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Supply chain evidence capture for forensic analysis.
    /// No scanning or monitoring - just evidence capture that pairs with offline manifest philosophy.
    /// If something goes wrong later, you can answer HOW it happened.
    /// </summary>
    public sealed class SupplyChainEvidence
    {
        /// <summary>
        /// Version of the evidence schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// SHA-256 hash of the application binary that created/last modified this vault.
        /// </summary>
        [JsonPropertyName("applicationHash")]
        public string ApplicationHash { get; set; } = string.Empty;

        /// <summary>
        /// Application version string at time of vault creation/modification.
        /// </summary>
        [JsonPropertyName("applicationVersion")]
        public string ApplicationVersion { get; set; } = string.Empty;

        /// <summary>
        /// Build configuration (Debug/Release) of the application.
        /// </summary>
        [JsonPropertyName("buildConfiguration")]
        public string BuildConfiguration { get; set; } = string.Empty;

        /// <summary>
        /// Git commit hash of the application build (if available).
        /// </summary>
        [JsonPropertyName("commitHash")]
        public string? CommitHash { get; set; }

        /// <summary>
        /// Timestamp when the application was built.
        /// </summary>
        [JsonPropertyName("buildTimestamp")]
        public DateTimeOffset? BuildTimestamp { get; set; }

        /// <summary>
        /// Whether the application had debug symbols attached (suspicious in production).
        /// </summary>
        [JsonPropertyName("hadDebugSymbols")]
        public bool HadDebugSymbols { get; set; }

        /// <summary>
        /// Key ID of the code signing certificate (if signed).
        /// </summary>
        [JsonPropertyName("codeSigningKeyId")]
        public string? CodeSigningKeyId { get; set; }

        /// <summary>
        /// Hashes of critical dependencies at time of vault operation.
        /// Key: assembly name, Value: SHA-256 hash.
        /// </summary>
        [JsonPropertyName("dependencyHashes")]
        public Dictionary<string, string> DependencyHashes { get; set; } = new();

        /// <summary>
        /// Known-good tool fingerprint for this vault (optional).
        /// If set, can warn when a different tool version accesses the vault.
        /// </summary>
        [JsonPropertyName("knownGoodFingerprint")]
        public ToolFingerprint? KnownGoodFingerprint { get; set; }

        /// <summary>
        /// History of tools that have accessed this vault.
        /// Limited to last N entries to avoid unbounded growth.
        /// </summary>
        [JsonPropertyName("accessHistory")]
        public List<ToolAccessRecord> AccessHistory { get; set; } = new();

        /// <summary>
        /// Maximum number of access history entries to retain.
        /// </summary>
        [JsonPropertyName("maxAccessHistoryEntries")]
        public int MaxAccessHistoryEntries { get; set; } = 50;

        /// <summary>
        /// Timestamp when evidence was last captured.
        /// </summary>
        [JsonPropertyName("lastCapturedUtc")]
        public DateTimeOffset LastCapturedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Optional OSV (Open Source Vulnerabilities) scan results.
        /// Stored as reference, not live scanning.
        /// </summary>
        [JsonPropertyName("osvScanResults")]
        public OsvScanSnapshot? OsvScanResults { get; set; }
    }

    /// <summary>
    /// Fingerprint of a tool/application for verification.
    /// </summary>
    public sealed class ToolFingerprint
    {
        /// <summary>
        /// SHA-256 hash of the application binary.
        /// </summary>
        [JsonPropertyName("binaryHash")]
        public string BinaryHash { get; set; } = string.Empty;

        /// <summary>
        /// Application version string.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Code signing key ID (if signed).
        /// </summary>
        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }

        /// <summary>
        /// When this fingerprint was established.
        /// </summary>
        [JsonPropertyName("establishedUtc")]
        public DateTimeOffset EstablishedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// User who established this fingerprint (if known).
        /// </summary>
        [JsonPropertyName("establishedBy")]
        public string? EstablishedBy { get; set; }
    }

    /// <summary>
    /// Record of a tool accessing the vault.
    /// </summary>
    public sealed class ToolAccessRecord
    {
        /// <summary>
        /// SHA-256 hash of the accessing tool.
        /// </summary>
        [JsonPropertyName("toolHash")]
        public string ToolHash { get; set; } = string.Empty;

        /// <summary>
        /// Version of the accessing tool.
        /// </summary>
        [JsonPropertyName("toolVersion")]
        public string ToolVersion { get; set; } = string.Empty;

        /// <summary>
        /// Type of access performed.
        /// </summary>
        [JsonPropertyName("accessType")]
        public VaultAccessType AccessType { get; set; }

        /// <summary>
        /// Timestamp of access.
        /// </summary>
        [JsonPropertyName("accessedUtc")]
        public DateTimeOffset AccessedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Device fingerprint where access occurred.
        /// </summary>
        [JsonPropertyName("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        /// <summary>
        /// Whether this tool matched the known-good fingerprint.
        /// </summary>
        [JsonPropertyName("matchedKnownGood")]
        public bool? MatchedKnownGood { get; set; }

        /// <summary>
        /// Any warnings generated during this access.
        /// </summary>
        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Types of vault access for audit purposes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VaultAccessType
    {
        /// <summary>
        /// Vault was opened/unlocked.
        /// </summary>
        Unlock,

        /// <summary>
        /// Vault contents were read.
        /// </summary>
        Read,

        /// <summary>
        /// Vault contents were modified.
        /// </summary>
        Write,

        /// <summary>
        /// Vault was exported/backed up.
        /// </summary>
        Export,

        /// <summary>
        /// Vault was created/provisioned.
        /// </summary>
        Provision,

        /// <summary>
        /// Vault was rekeyed.
        /// </summary>
        Rekey,

        /// <summary>
        /// Recovery operation was performed.
        /// </summary>
        Recovery
    }

    /// <summary>
    /// Snapshot of OSV (Open Source Vulnerabilities) scan results.
    /// Reference data only - not live scanning.
    /// </summary>
    public sealed class OsvScanSnapshot
    {
        /// <summary>
        /// Timestamp when scan was performed.
        /// </summary>
        [JsonPropertyName("scannedUtc")]
        public DateTimeOffset ScannedUtc { get; set; }

        /// <summary>
        /// Version of OSV database used.
        /// </summary>
        [JsonPropertyName("osvDatabaseVersion")]
        public string? OsvDatabaseVersion { get; set; }

        /// <summary>
        /// Number of dependencies scanned.
        /// </summary>
        [JsonPropertyName("dependenciesScanned")]
        public int DependenciesScanned { get; set; }

        /// <summary>
        /// Number of vulnerabilities found (if any).
        /// </summary>
        [JsonPropertyName("vulnerabilitiesFound")]
        public int VulnerabilitiesFound { get; set; }

        /// <summary>
        /// High-level summary of findings.
        /// </summary>
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        /// <summary>
        /// List of vulnerability IDs found (e.g., CVE-2024-XXXX).
        /// </summary>
        [JsonPropertyName("vulnerabilityIds")]
        public List<string> VulnerabilityIds { get; set; } = new();
    }
}
