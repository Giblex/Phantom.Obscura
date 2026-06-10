using System;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Models
{

    public class VaultManifest
    {
        /// <summary>
        /// KDF parameters read from the OUTER manifest envelope at decrypt time. Runtime-only;
        /// not serialized into the manifest's encrypted payload. Downstream consumers (e.g.
        /// re-key flow) use this to know what parameters this manifest was last written under.
        /// </summary>
        [JsonIgnore]
        public ManifestKdfParams? RuntimeKdfParams { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; } = 3;

        [JsonPropertyName("vaultName")]
        public string VaultName { get; set; } = string.Empty;

        [JsonPropertyName("containerPath")]
        public string ContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("rootContainerPath")]
        public string? RootContainerPath { get; set; }
            = null;

        [JsonPropertyName("objectContainerPath")]
        public string? ObjectContainerPath { get; set; }
            = null;

        [JsonPropertyName("recoveryContainerPath")]
        public string? RecoveryContainerPath { get; set; }
            = null;

        [JsonPropertyName("bindingRecordPath")]
        public string? BindingRecordPath { get; set; }
            = null;

        [JsonPropertyName("recoveryRecordPath")]
        public string? RecoveryRecordPath { get; set; }
            = null;

        [JsonPropertyName("provisioningRecordPath")]
        public string? ProvisioningRecordPath { get; set; }
            = null;

        [JsonPropertyName("masterVolumePath")]
        public string? MasterVolumePath { get; set; }
            = null;

        [JsonPropertyName("decoyDatabasePath")]
        public string? DecoyDatabasePath { get; set; }
            = null;

        [JsonPropertyName("protectionTier")]
        public VaultProtectionTier ProtectionTier { get; set; }
            = VaultProtectionTier.StandardSecure;

        [JsonPropertyName("effectiveStorageTransport")]
        public VaultStorageTransport EffectiveStorageTransport { get; set; }
            = VaultStorageTransport.FileSystem;

        [JsonPropertyName("requestedStorageTransport")]
        public VaultStorageTransport? RequestedStorageTransport { get; set; }
            = null;

        [JsonPropertyName("supportsReversibleTierMigration")]
        public bool SupportsReversibleTierMigration { get; set; }
            = true;

        [JsonPropertyName("containerSizeBytes")]
        public long ContainerSizeBytes { get; set; }
            = 0;

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
            = null;

        [JsonPropertyName("saltBase64")]
        public string SaltBase64 { get; set; } = string.Empty;

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "AES-256-GCM";

        [JsonPropertyName("keyfilePath")]
        public string? KeyfilePath { get; set; }
            = null;

        [JsonPropertyName("requiresHardwareToken")]
        public bool RequiresHardwareToken { get; set; }
            = false;

        [JsonPropertyName("phantomKeyBridgeEnabled")]
        public bool PhantomKeyBridgeEnabled { get; set; }
            = false;

        [JsonPropertyName("phantomKeyBridgeWorkspacePath")]
        public string? PhantomKeyBridgeWorkspacePath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgeManifestPath")]
        public string? PhantomKeyBridgeManifestPath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgeContinuityPath")]
        public string? PhantomKeyBridgeContinuityPath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgePolicyPath")]
        public string? PhantomKeyBridgePolicyPath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgeConsumerMapPath")]
        public string? PhantomKeyBridgeConsumerMapPath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgeAuditLogPath")]
        public string? PhantomKeyBridgeAuditLogPath { get; set; }
            = null;

        [JsonPropertyName("phantomKeyBridgeReceiptPath")]
        public string? PhantomKeyBridgeReceiptPath { get; set; }
            = null;

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }
            = null;

        [JsonPropertyName("usbBindingId")]
        public string? UsbBindingId { get; set; }
            = null;

        [JsonPropertyName("usbBindingGuid")]
        public string? UsbBindingGuid { get; set; }
            = null;

        [JsonPropertyName("guuid")]
        public string? Guuid { get; set; }
            = null;

        [JsonPropertyName("totpSecret")]
        public string? TotpSecret { get; set; }
            = null;

        [JsonPropertyName("requiresTotp")]
        public bool RequiresTotp { get; set; }
            = false;

        [JsonPropertyName("passkeyId")]
        public string? PasskeyId { get; set; }
            = null;

        [JsonPropertyName("kemPublicKey")]
        public string? KemPublicKeyBase64 { get; set; }
            = null;

        [JsonPropertyName("kemCiphertext")]
        public string? KemCiphertextBase64 { get; set; }
            = null;

        [JsonPropertyName("kemPrivateKeyEncrypted")]
        public string? KemPrivateKeyEncryptedBase64 { get; set; }
            = null;

        [JsonPropertyName("yubiKeySerial")]
        public int? YubiKeySerial { get; set; }
            = null;

        [JsonPropertyName("yubiKeyCredentialId")]
        public string? YubiKeyCredentialIdBase64 { get; set; }
            = null;

        [JsonPropertyName("yubiKeyPublicKey")]
        public string? YubiKeyPublicKeyBase64 { get; set; }
            = null;

        [JsonPropertyName("autoFillEnabled")]
        public bool AutoFillEnabled { get; set; } = false;

        [JsonPropertyName("autoFillUsername")]
        public bool AutoFillUsername { get; set; } = true;

        [JsonPropertyName("autoFillPassword")]
        public bool AutoFillPassword { get; set; } = true;

        [JsonPropertyName("autoBackupEnabled")]
        public bool AutoBackupEnabled { get; set; } = false;

        [JsonPropertyName("backupRetentionDays")]
        public int BackupRetentionDays { get; set; } = 3;

        [JsonPropertyName("domainWhitelist")]
        public string? DomainWhitelist { get; set; } = string.Empty;

        [JsonPropertyName("failedAttempts")]
        public int FailedAttempts { get; set; } = 0;

        [JsonPropertyName("maxFailedAttempts")]
        public int MaxFailedAttempts { get; set; } = 5;

        [JsonPropertyName("selfDestructEnabled")]
        public bool SelfDestructEnabled { get; set; } = false;

        [JsonPropertyName("lockedUntilUtc")]
        public DateTimeOffset? LockedUntilUtc { get; set; }
            = null;

        [JsonPropertyName("categories")]
        public System.Collections.Generic.List<CategoryModel>? Categories { get; set; } = null;

        [JsonPropertyName("securityState")]
        public VaultSecurityState SecurityState { get; set; } = VaultSecurityState.Normal;

        [JsonPropertyName("rekeyRequired")]
        public bool RekeyRequired { get; set; } = false;

        [JsonPropertyName("trustedDevices")]
        public System.Collections.Generic.List<DeviceFingerprint> TrustedDevices { get; set; }
            = new System.Collections.Generic.List<DeviceFingerprint>();

        [JsonPropertyName("lastKeyRotation")]
        public DateTimeOffset LastKeyRotation { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("keyRotationCount")]
        public int KeyRotationCount { get; set; } = 0;

        [JsonPropertyName("daysUntilRotationRequired")]
        public int DaysUntilRotationRequired { get; set; } = 90;

        [JsonPropertyName("keyRotationPending")]
        public bool KeyRotationPending { get; set; } = false;

        [JsonPropertyName("manifestSequence")]
        public long ManifestSequence { get; set; } = 0;

        [JsonPropertyName("policyHash")]
        public string? PolicyHashBase64 { get; set; } = null;

        [JsonPropertyName("integritySignature")]
        public string? IntegritySignatureBase64 { get; set; } = null;

        [JsonPropertyName("signatureTimestamp")]
        public DateTimeOffset? SignatureTimestamp { get; set; } = null;

        [JsonPropertyName("pinSaltBase64")]
        public string? PinSaltBase64 { get; set; } = null;

        [JsonPropertyName("pinHashBase64")]
        public string? PinHashBase64 { get; set; } = null;

        [JsonPropertyName("pinPbkdf2Iterations")]
        public int PinPbkdf2Iterations { get; set; } = 150_000;

        [JsonPropertyName("recoveryCodes")]
        public RecoveryCode[]? RecoveryCodes { get; set; } = null;

        [JsonPropertyName("backupSigningPublicKey")]
        public string? BackupSigningPublicKeyBase64 { get; set; } = null;

        [JsonPropertyName("encryptedBackupSigningKey")]
        public string? EncryptedBackupSigningKeyBase64 { get; set; } = null;

        [JsonPropertyName("threatModel")]
        public ThreatModelMetadata? ThreatModel { get; set; } = null;

        [JsonPropertyName("aiAccessPolicy")]
        public AIAccessPolicy? AiAccessPolicy { get; set; } = null;

        [JsonPropertyName("supplyChainEvidence")]
        public SupplyChainEvidence? SupplyChainEvidence { get; set; } = null;

        [JsonPropertyName("securityCapabilities")]
        public SecurityCapabilitiesManifest? SecurityCapabilities { get; set; } = null;

        [JsonPropertyName("usbWriteProtection")]
        public UsbWriteProtectionState? UsbWriteProtection { get; set; } = null;

        [JsonPropertyName("scrubHistory")]
        public System.Collections.Generic.List<ScrubEvent> ScrubHistory { get; set; }
            = new System.Collections.Generic.List<ScrubEvent>();
    }

    public sealed class RecoveryCode
    {

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("used")]
        public bool Used { get; set; }

        [JsonPropertyName("usedAtUtc")]
        public DateTimeOffset? UsedAtUtc { get; set; }

        [JsonPropertyName("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}

