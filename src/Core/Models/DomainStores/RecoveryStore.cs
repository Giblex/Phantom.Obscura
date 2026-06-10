using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.DomainStores
{

    public sealed class RecoveryStore
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("store_id")]
        public string StoreId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("modified_utc")]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("policy")]
        public RecoveryPolicy Policy { get; set; } = new();

        [JsonPropertyName("obscura_sealed")]
        public SealedRecoveryKey? ObscuraSealed { get; set; }

        [JsonPropertyName("attestor_sealed")]
        public SealedRecoveryKey? AttestorSealed { get; set; }

        [JsonPropertyName("recovery_codes")]
        public List<RecoveryCodeEntry> RecoveryCodes { get; set; } = new();

        [JsonPropertyName("max_recovery_codes")]
        public int MaxRecoveryCodes { get; set; } = 10;

        [JsonPropertyName("audit_log")]
        public List<RecoveryAuditEntry> AuditLog { get; set; } = new();

        [JsonPropertyName("max_audit_entries")]
        public int MaxAuditEntries { get; set; } = 100;

        [JsonPropertyName("audit_chain_head")]
        public string? AuditChainHead { get; set; }

        [JsonPropertyName("salt")]
        public byte[] Salt { get; set; } = new byte[32];

        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("setup_app_version")]
        public string? SetupAppVersion { get; set; }

        /// <summary>
        /// The mandatory secret backed up for recovery — the composite keyfile material that, on the
        /// USB-bound vault, is the only required credential. Carried inside the RSK-encrypted envelope
        /// body, so any recovery code restores it. Null when only domain seals are used.
        /// </summary>
        [JsonPropertyName("protected_keyfile")]
        public byte[]? ProtectedKeyfile { get; set; }
    }

    public sealed class SealedRecoveryKey
    {

        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("sealed_blob")]
        public string SealedBlob { get; set; } = string.Empty;

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("seal_version")]
        public int SealVersion { get; set; } = 1;

        [JsonPropertyName("blob_hash")]
        public string BlobHash { get; set; } = string.Empty;

        [JsonPropertyName("has_been_used")]
        public bool HasBeenUsed { get; set; } = false;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }
    }

    public sealed class RecoveryCodeEntry
    {

        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("used")]
        public bool Used { get; set; } = false;

        [JsonPropertyName("used_utc")]
        public DateTimeOffset? UsedUtc { get; set; }

        [JsonPropertyName("used_for_domain")]
        public string? UsedForDomain { get; set; }

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("code_number")]
        public int CodeNumber { get; set; }
    }

    public sealed class RecoveryPolicy
    {

        [JsonPropertyName("recovery_delay_seconds")]
        public int RecoveryDelaySeconds { get; set; } = 60;

        [JsonPropertyName("require_usb_presence")]
        public bool RequireUsbPresence { get; set; } = true;

        [JsonPropertyName("require_explicit_domain_selection")]
        public bool RequireExplicitDomainSelection { get; set; } = true;

        [JsonPropertyName("force_rotation_after_recovery")]
        public bool ForceRotationAfterRecovery { get; set; } = true;

        [JsonPropertyName("invalidate_codes_after_use")]
        public bool InvalidateCodesAfterUse { get; set; } = false;

        [JsonPropertyName("require_second_factor")]
        public bool RequireSecondFactor { get; set; } = false;

        [JsonPropertyName("second_factor_type")]
        public RecoverySecondFactorType SecondFactorType { get; set; } = RecoverySecondFactorType.None;

        [JsonPropertyName("max_session_seconds")]
        public int MaxSessionSeconds { get; set; } = 300;

        [JsonPropertyName("min_app_version")]
        public string? MinAppVersion { get; set; }

        [JsonPropertyName("device_binding_mode")]
        public RecoveryDeviceBindingMode DeviceBindingMode { get; set; } = RecoveryDeviceBindingMode.Warn;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoverySecondFactorType
    {

        None,

        Pin,

        HardwareToken,

        PlatformBiometric,

        CrossDeviceApproval
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoveryDeviceBindingMode
    {

        None,

        Warn,

        Block
    }

    public sealed class RecoveryAuditEntry
    {

        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("event_type")]
        public RecoveryEventType EventType { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("target_domain")]
        public string? TargetDomain { get; set; }

        [JsonPropertyName("code_number")]
        public int? CodeNumber { get; set; }

        [JsonPropertyName("device_fingerprint")]
        public string? DeviceFingerprint { get; set; }

        [JsonPropertyName("app_version")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("failure_reason")]
        public string? FailureReason { get; set; }

        [JsonPropertyName("prev_hash")]
        public string PrevHash { get; set; } = string.Empty;

        [JsonPropertyName("this_hash")]
        public string ThisHash { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoveryEventType
    {

        RecoveryModeEntered,

        RecoveryModeExited,

        CodeValidationAttempt,

        DomainRecovered,

        KeyRotationTriggered,

        CodesRegenerated,

        RecoveryAborted,

        PolicyViolation,

        UsbRemovedDuringRecovery,

        DeviceMismatch
    }
}

