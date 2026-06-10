using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{

    public sealed class SecurityCapabilitiesManifest
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("manifestId")]
        public string ManifestId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("generatedUtc")]
        public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("applicationVersion")]
        public string ApplicationVersion { get; set; } = string.Empty;

        [JsonPropertyName("coreCapabilities")]
        public CoreSecurityCapabilities CoreCapabilities { get; set; } = new();

        [JsonPropertyName("encryption")]
        public EncryptionCapabilities Encryption { get; set; } = new();

        [JsonPropertyName("authentication")]
        public AuthenticationCapabilities Authentication { get; set; } = new();

        [JsonPropertyName("dataProtection")]
        public DataProtectionCapabilities DataProtection { get; set; } = new();

        [JsonPropertyName("compliance")]
        public ComplianceCapabilities Compliance { get; set; } = new();

        [JsonPropertyName("threatModel")]
        public ThreatModelReference ThreatModel { get; set; } = new();

        [JsonPropertyName("signature")]
        public string? SignatureBase64 { get; set; }

        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }
    }

    public sealed class CoreSecurityCapabilities
    {

        [JsonPropertyName("offlineEncryption")]
        public bool OfflineEncryption { get; set; } = true;

        [JsonPropertyName("userControlledKeyMaterial")]
        public bool UserControlledKeyMaterial { get; set; } = true;

        [JsonPropertyName("noCloudDependency")]
        public bool NoCloudDependency { get; set; } = true;

        [JsonPropertyName("explicitThreatModel")]
        public bool ExplicitThreatModel { get; set; } = true;

        [JsonPropertyName("zeroKnowledgeArchitecture")]
        public bool ZeroKnowledgeArchitecture { get; set; } = true;

        [JsonPropertyName("airGappedOperation")]
        public bool AirGappedOperation { get; set; } = true;

        [JsonPropertyName("memorySafeKeyHandling")]
        public bool MemorySafeKeyHandling { get; set; } = true;

        [JsonPropertyName("tamperEvidentManifest")]
        public bool TamperEvidentManifest { get; set; } = true;
    }

    public sealed class EncryptionCapabilities
    {

        [JsonPropertyName("symmetricAlgorithm")]
        public string SymmetricAlgorithm { get; set; } = "AES-256-GCM";

        [JsonPropertyName("keyDerivation")]
        public string KeyDerivation { get; set; } = "Argon2id";

        [JsonPropertyName("keyDerivationParams")]
        public KeyDerivationParams KeyDerivationParams { get; set; } = new();

        [JsonPropertyName("postQuantumSupport")]
        public bool PostQuantumSupport { get; set; } = true;

        [JsonPropertyName("postQuantumAlgorithm")]
        public string? PostQuantumAlgorithm { get; set; } = "ML-KEM-768";

        [JsonPropertyName("layeredEncryption")]
        public bool LayeredEncryption { get; set; } = true;

        [JsonPropertyName("authenticatedEncryption")]
        public bool AuthenticatedEncryption { get; set; } = true;
    }

    public sealed class KeyDerivationParams
    {
        [JsonPropertyName("memoryCostKb")]
        public int MemoryCostKb { get; set; } = 262144;

        [JsonPropertyName("timeCost")]
        public int TimeCost { get; set; } = 6;

        [JsonPropertyName("parallelism")]
        public int Parallelism { get; set; } = 4;

        [JsonPropertyName("saltLength")]
        public int SaltLength { get; set; } = 32;

        [JsonPropertyName("keyLength")]
        public int KeyLength { get; set; } = 32;
    }

    public sealed class AuthenticationCapabilities
    {

        [JsonPropertyName("passwordAuth")]
        public bool PasswordAuth { get; set; } = true;

        [JsonPropertyName("keyfileAuth")]
        public bool KeyfileAuth { get; set; } = true;

        [JsonPropertyName("totpSupport")]
        public bool TotpSupport { get; set; } = true;

        [JsonPropertyName("hardwareTokenSupport")]
        public bool HardwareTokenSupport { get; set; } = true;

        [JsonPropertyName("passkeySupport")]
        public bool PasskeySupport { get; set; } = true;

        [JsonPropertyName("mfaEnforced")]
        public bool MfaEnforced { get; set; } = false;

        [JsonPropertyName("deviceBinding")]
        public bool DeviceBinding { get; set; } = true;

        [JsonPropertyName("recoveryCodes")]
        public bool RecoveryCodes { get; set; } = true;
    }

    public sealed class DataProtectionCapabilities
    {

        [JsonPropertyName("secureMemoryZeroing")]
        public bool SecureMemoryZeroing { get; set; } = true;

        [JsonPropertyName("secureFileDeletion")]
        public bool SecureFileDeletion { get; set; } = true;

        [JsonPropertyName("selfDestructCapability")]
        public bool SelfDestructCapability { get; set; } = true;

        [JsonPropertyName("decoyVaultSupport")]
        public bool DecoyVaultSupport { get; set; } = true;

        [JsonPropertyName("clipboardAutoClear")]
        public bool ClipboardAutoClear { get; set; } = true;

        [JsonPropertyName("autoLockOnInactivity")]
        public bool AutoLockOnInactivity { get; set; } = true;

        [JsonPropertyName("aiLeakageProtection")]
        public bool AiLeakageProtection { get; set; } = true;

        [JsonPropertyName("encryptedBackups")]
        public bool EncryptedBackups { get; set; } = true;
    }

    public sealed class ComplianceCapabilities
    {

        [JsonPropertyName("regulatoryFrameworks")]
        public List<string> RegulatoryFrameworks { get; set; } = new()
        {
            "GDPR",
            "EU-CRA",
            "NIS2"
        };

        [JsonPropertyName("securityStandards")]
        public List<string> SecurityStandards { get; set; } = new()
        {
            "OWASP-ASVS-L3",
            "NIST-CSF",
            "Zero-Trust"
        };

        [JsonPropertyName("cryptographicStandards")]
        public List<string> CryptographicStandards { get; set; } = new()
        {
            "NIST-SP-800-132",
            "NIST-SP-800-38D",
            "RFC-9106"
        };

        [JsonPropertyName("auditTrailEnabled")]
        public bool AuditTrailEnabled { get; set; } = true;

        [JsonPropertyName("hashChainedAuditLogs")]
        public bool HashChainedAuditLogs { get; set; } = true;
    }

    public sealed class ThreatModelReference
    {

        [JsonPropertyName("defaultEnvironment")]
        public string DefaultEnvironment { get; set; } = "HostileOS";

        [JsonPropertyName("keyAssumptions")]
        public List<string> KeyAssumptions { get; set; } = new()
        {
            "ClipboardCompromise",
            "KeyloggerPresent",
            "MemoryScrapeRisk",
            "SupplyChainRisk",
            "AiLeakageRisk"
        };

        [JsonPropertyName("trustModel")]
        public string TrustModel { get; set; } = "Zero-Trust: No environment component trusted by default";
    }
}

