using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Security capabilities manifest for compliance documentation.
    /// Exposes provable design capabilities without dashboards or checklists.
    /// Reusable for: investor decks, enterprise pilots, audit responses.
    /// </summary>
    public sealed class SecurityCapabilitiesManifest
    {
        /// <summary>
        /// Version of the capabilities manifest schema.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Unique identifier for this capabilities declaration.
        /// </summary>
        [JsonPropertyName("manifestId")]
        public string ManifestId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Timestamp when this manifest was generated.
        /// </summary>
        [JsonPropertyName("generatedUtc")]
        public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Application version that generated this manifest.
        /// </summary>
        [JsonPropertyName("applicationVersion")]
        public string ApplicationVersion { get; set; } = string.Empty;

        /// <summary>
        /// Core security capabilities of the vault.
        /// </summary>
        [JsonPropertyName("coreCapabilities")]
        public CoreSecurityCapabilities CoreCapabilities { get; set; } = new();

        /// <summary>
        /// Encryption capabilities and algorithms.
        /// </summary>
        [JsonPropertyName("encryption")]
        public EncryptionCapabilities Encryption { get; set; } = new();

        /// <summary>
        /// Authentication capabilities.
        /// </summary>
        [JsonPropertyName("authentication")]
        public AuthenticationCapabilities Authentication { get; set; } = new();

        /// <summary>
        /// Data protection capabilities.
        /// </summary>
        [JsonPropertyName("dataProtection")]
        public DataProtectionCapabilities DataProtection { get; set; } = new();

        /// <summary>
        /// Compliance-relevant certifications and standards.
        /// </summary>
        [JsonPropertyName("compliance")]
        public ComplianceCapabilities Compliance { get; set; } = new();

        /// <summary>
        /// Explicit threat model reference.
        /// </summary>
        [JsonPropertyName("threatModel")]
        public ThreatModelReference ThreatModel { get; set; } = new();

        /// <summary>
        /// Optional digital signature of this manifest for verification.
        /// </summary>
        [JsonPropertyName("signature")]
        public string? SignatureBase64 { get; set; }

        /// <summary>
        /// Key ID used for signing (if signed).
        /// </summary>
        [JsonPropertyName("signingKeyId")]
        public string? SigningKeyId { get; set; }
    }

    /// <summary>
    /// Core security capabilities.
    /// </summary>
    public sealed class CoreSecurityCapabilities
    {
        /// <summary>
        /// Encryption is performed entirely offline - no network required.
        /// </summary>
        [JsonPropertyName("offlineEncryption")]
        public bool OfflineEncryption { get; set; } = true;

        /// <summary>
        /// User maintains complete control over key material.
        /// </summary>
        [JsonPropertyName("userControlledKeyMaterial")]
        public bool UserControlledKeyMaterial { get; set; } = true;

        /// <summary>
        /// No cloud dependency for core vault operations.
        /// </summary>
        [JsonPropertyName("noCloudDependency")]
        public bool NoCloudDependency { get; set; } = true;

        /// <summary>
        /// Explicit threat model documented and enforced.
        /// </summary>
        [JsonPropertyName("explicitThreatModel")]
        public bool ExplicitThreatModel { get; set; } = true;

        /// <summary>
        /// Zero-knowledge architecture - no plaintext leaves the vault.
        /// </summary>
        [JsonPropertyName("zeroKnowledgeArchitecture")]
        public bool ZeroKnowledgeArchitecture { get; set; } = true;

        /// <summary>
        /// Air-gapped operation supported via USB-bound vaults.
        /// </summary>
        [JsonPropertyName("airGappedOperation")]
        public bool AirGappedOperation { get; set; } = true;

        /// <summary>
        /// Memory-safe key handling with secure zeroing.
        /// </summary>
        [JsonPropertyName("memorySafeKeyHandling")]
        public bool MemorySafeKeyHandling { get; set; } = true;

        /// <summary>
        /// Tamper-evident manifest with integrity signatures.
        /// </summary>
        [JsonPropertyName("tamperEvidentManifest")]
        public bool TamperEvidentManifest { get; set; } = true;
    }

    /// <summary>
    /// Encryption capabilities and algorithms.
    /// </summary>
    public sealed class EncryptionCapabilities
    {
        /// <summary>
        /// Primary symmetric encryption algorithm.
        /// </summary>
        [JsonPropertyName("symmetricAlgorithm")]
        public string SymmetricAlgorithm { get; set; } = "AES-256-GCM";

        /// <summary>
        /// Key derivation function.
        /// </summary>
        [JsonPropertyName("keyDerivation")]
        public string KeyDerivation { get; set; } = "Argon2id";

        /// <summary>
        /// Key derivation parameters.
        /// </summary>
        [JsonPropertyName("keyDerivationParams")]
        public KeyDerivationParams KeyDerivationParams { get; set; } = new();

        /// <summary>
        /// Post-quantum encryption supported.
        /// </summary>
        [JsonPropertyName("postQuantumSupport")]
        public bool PostQuantumSupport { get; set; } = true;

        /// <summary>
        /// Post-quantum algorithm (if supported).
        /// </summary>
        [JsonPropertyName("postQuantumAlgorithm")]
        public string? PostQuantumAlgorithm { get; set; } = "ML-KEM-768";

        /// <summary>
        /// Layered encryption (defense in depth).
        /// </summary>
        [JsonPropertyName("layeredEncryption")]
        public bool LayeredEncryption { get; set; } = true;

        /// <summary>
        /// Authenticated encryption (AEAD).
        /// </summary>
        [JsonPropertyName("authenticatedEncryption")]
        public bool AuthenticatedEncryption { get; set; } = true;
    }

    /// <summary>
    /// Key derivation parameters.
    /// </summary>
    public sealed class KeyDerivationParams
    {
        [JsonPropertyName("memoryCostKb")]
        public int MemoryCostKb { get; set; } = 65536; // 64 MB

        [JsonPropertyName("timeCost")]
        public int TimeCost { get; set; } = 3;

        [JsonPropertyName("parallelism")]
        public int Parallelism { get; set; } = 4;

        [JsonPropertyName("saltLength")]
        public int SaltLength { get; set; } = 32;

        [JsonPropertyName("keyLength")]
        public int KeyLength { get; set; } = 32;
    }

    /// <summary>
    /// Authentication capabilities.
    /// </summary>
    public sealed class AuthenticationCapabilities
    {
        /// <summary>
        /// Password-based authentication.
        /// </summary>
        [JsonPropertyName("passwordAuth")]
        public bool PasswordAuth { get; set; } = true;

        /// <summary>
        /// Keyfile-based authentication.
        /// </summary>
        [JsonPropertyName("keyfileAuth")]
        public bool KeyfileAuth { get; set; } = true;

        /// <summary>
        /// TOTP (Time-based OTP) support.
        /// </summary>
        [JsonPropertyName("totpSupport")]
        public bool TotpSupport { get; set; } = true;

        /// <summary>
        /// Hardware token support (YubiKey, etc.).
        /// </summary>
        [JsonPropertyName("hardwareTokenSupport")]
        public bool HardwareTokenSupport { get; set; } = true;

        /// <summary>
        /// FIDO2/WebAuthn passkey support.
        /// </summary>
        [JsonPropertyName("passkeySupport")]
        public bool PasskeySupport { get; set; } = true;

        /// <summary>
        /// Multi-factor authentication enforced.
        /// </summary>
        [JsonPropertyName("mfaEnforced")]
        public bool MfaEnforced { get; set; } = false;

        /// <summary>
        /// Device binding (USB serial, volume ID).
        /// </summary>
        [JsonPropertyName("deviceBinding")]
        public bool DeviceBinding { get; set; } = true;

        /// <summary>
        /// Recovery codes for account recovery.
        /// </summary>
        [JsonPropertyName("recoveryCodes")]
        public bool RecoveryCodes { get; set; } = true;
    }

    /// <summary>
    /// Data protection capabilities.
    /// </summary>
    public sealed class DataProtectionCapabilities
    {
        /// <summary>
        /// Secure memory zeroing after use.
        /// </summary>
        [JsonPropertyName("secureMemoryZeroing")]
        public bool SecureMemoryZeroing { get; set; } = true;

        /// <summary>
        /// Secure file deletion (multi-pass overwrite).
        /// </summary>
        [JsonPropertyName("secureFileDeletion")]
        public bool SecureFileDeletion { get; set; } = true;

        /// <summary>
        /// Self-destruct capability on failed attempts.
        /// </summary>
        [JsonPropertyName("selfDestructCapability")]
        public bool SelfDestructCapability { get; set; } = true;

        /// <summary>
        /// Decoy vault support for plausible deniability.
        /// </summary>
        [JsonPropertyName("decoyVaultSupport")]
        public bool DecoyVaultSupport { get; set; } = true;

        /// <summary>
        /// Clipboard auto-clear after timeout.
        /// </summary>
        [JsonPropertyName("clipboardAutoClear")]
        public bool ClipboardAutoClear { get; set; } = true;

        /// <summary>
        /// Automatic vault lock after inactivity.
        /// </summary>
        [JsonPropertyName("autoLockOnInactivity")]
        public bool AutoLockOnInactivity { get; set; } = true;

        /// <summary>
        /// AI leakage protection.
        /// </summary>
        [JsonPropertyName("aiLeakageProtection")]
        public bool AiLeakageProtection { get; set; } = true;

        /// <summary>
        /// Encrypted backup support with signing.
        /// </summary>
        [JsonPropertyName("encryptedBackups")]
        public bool EncryptedBackups { get; set; } = true;
    }

    /// <summary>
    /// Compliance-relevant information.
    /// </summary>
    public sealed class ComplianceCapabilities
    {
        /// <summary>
        /// Relevant regulatory frameworks.
        /// </summary>
        [JsonPropertyName("regulatoryFrameworks")]
        public List<string> RegulatoryFrameworks { get; set; } = new()
        {
            "GDPR",
            "EU-CRA",
            "NIS2"
        };

        /// <summary>
        /// Security standards aligned with.
        /// </summary>
        [JsonPropertyName("securityStandards")]
        public List<string> SecurityStandards { get; set; } = new()
        {
            "OWASP-ASVS-L3",
            "NIST-CSF",
            "Zero-Trust"
        };

        /// <summary>
        /// Cryptographic standards compliance.
        /// </summary>
        [JsonPropertyName("cryptographicStandards")]
        public List<string> CryptographicStandards { get; set; } = new()
        {
            "NIST-SP-800-132",
            "NIST-SP-800-38D",
            "RFC-9106"
        };

        /// <summary>
        /// Audit trail capabilities.
        /// </summary>
        [JsonPropertyName("auditTrailEnabled")]
        public bool AuditTrailEnabled { get; set; } = true;

        /// <summary>
        /// Hash-chained audit logs for integrity.
        /// </summary>
        [JsonPropertyName("hashChainedAuditLogs")]
        public bool HashChainedAuditLogs { get; set; } = true;
    }

    /// <summary>
    /// Reference to the threat model.
    /// </summary>
    public sealed class ThreatModelReference
    {
        /// <summary>
        /// Default threat environment assumption.
        /// </summary>
        [JsonPropertyName("defaultEnvironment")]
        public string DefaultEnvironment { get; set; } = "HostileOS";

        /// <summary>
        /// Key threat assumptions.
        /// </summary>
        [JsonPropertyName("keyAssumptions")]
        public List<string> KeyAssumptions { get; set; } = new()
        {
            "ClipboardCompromise",
            "KeyloggerPresent",
            "MemoryScrapeRisk",
            "SupplyChainRisk",
            "AiLeakageRisk"
        };

        /// <summary>
        /// Trust model description.
        /// </summary>
        [JsonPropertyName("trustModel")]
        public string TrustModel { get; set; } = "Zero-Trust: No environment component trusted by default";
    }
}
