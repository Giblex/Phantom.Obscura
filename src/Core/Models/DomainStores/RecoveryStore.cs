using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.DomainStores
{
    /// <summary>
    /// Recovery domain store - break-glass recovery operations.
    ///
    /// This store contains:
    /// - Sealed recovery key blobs (one per domain)
    /// - Recovery policy (factors, delays, version pins)
    /// - Recovery audit log
    /// - Recovery code hashes (single-use)
    ///
    /// This store does NOT contain:
    /// - Plaintext master keys
    /// - Plaintext domain keys
    /// - Exportable passkey private keys
    /// - Anything auto-readable by Obscura or Attestor
    ///
    /// Encrypted with: K_recovery (domain key)
    /// File: recovery.store.encrypted
    ///
    /// CRITICAL: Recovery is HARDER to unlock than anything else.
    /// Recovery is NOT a convenience layer.
    /// </summary>
    public sealed class RecoveryStore
    {
        /// <summary>
        /// Schema version for migration support.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Store identifier (UUIDv7 recommended).
        /// </summary>
        [JsonPropertyName("store_id")]
        public string StoreId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// UTC timestamp when store was created.
        /// </summary>
        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// UTC timestamp when store was last modified.
        /// </summary>
        [JsonPropertyName("modified_utc")]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Recovery policy governing break-glass operations.
        /// </summary>
        [JsonPropertyName("policy")]
        public RecoveryPolicy Policy { get; set; } = new();

        /// <summary>
        /// Sealed recovery key for Obscura domain.
        /// Seal_obscura = Encrypt(K_recovery, K_obscura_recover || metadata)
        /// </summary>
        [JsonPropertyName("obscura_sealed")]
        public SealedRecoveryKey? ObscuraSealed { get; set; }

        /// <summary>
        /// Sealed recovery key for Attestor domain.
        /// Seal_attestor = Encrypt(K_recovery, K_attestor_recover || metadata)
        /// </summary>
        [JsonPropertyName("attestor_sealed")]
        public SealedRecoveryKey? AttestorSealed { get; set; }

        /// <summary>
        /// Recovery code hashes (single-use backup codes).
        /// </summary>
        [JsonPropertyName("recovery_codes")]
        public List<RecoveryCodeEntry> RecoveryCodes { get; set; } = new();

        /// <summary>
        /// Maximum recovery codes to generate.
        /// </summary>
        [JsonPropertyName("max_recovery_codes")]
        public int MaxRecoveryCodes { get; set; } = 10;

        /// <summary>
        /// Recovery audit log.
        /// </summary>
        [JsonPropertyName("audit_log")]
        public List<RecoveryAuditEntry> AuditLog { get; set; } = new();

        /// <summary>
        /// Maximum audit entries to retain.
        /// </summary>
        [JsonPropertyName("max_audit_entries")]
        public int MaxAuditEntries { get; set; } = 100;

        /// <summary>
        /// Hash of the previous audit entry (chain integrity).
        /// </summary>
        [JsonPropertyName("audit_chain_head")]
        public string? AuditChainHead { get; set; }

        /// <summary>
        /// Salt for recovery key derivation (16+ bytes, random).
        /// </summary>
        [JsonPropertyName("salt")]
        public byte[] Salt { get; set; } = new byte[32];

        /// <summary>
        /// Device ID binding for recovery operations.
        /// </summary>
        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        /// <summary>
        /// Application version at time of last recovery setup.
        /// Used for version pinning security.
        /// </summary>
        [JsonPropertyName("setup_app_version")]
        public string? SetupAppVersion { get; set; }
    }

    /// <summary>
    /// Sealed recovery key blob for a specific domain.
    /// </summary>
    public sealed class SealedRecoveryKey
    {
        /// <summary>
        /// Target domain this key recovers.
        /// </summary>
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// Encrypted recovery key blob.
        /// Format: Base64(nonce || ciphertext || tag)
        /// Plaintext: K_domain_recover || metadata_json
        /// </summary>
        [JsonPropertyName("sealed_blob")]
        public string SealedBlob { get; set; } = string.Empty;

        /// <summary>
        /// When this seal was created.
        /// </summary>
        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Version of the seal format.
        /// </summary>
        [JsonPropertyName("seal_version")]
        public int SealVersion { get; set; } = 1;

        /// <summary>
        /// SHA-256 hash of the sealed blob for quick verification.
        /// </summary>
        [JsonPropertyName("blob_hash")]
        public string BlobHash { get; set; } = string.Empty;

        /// <summary>
        /// Whether this seal has been used (and thus should be rotated).
        /// </summary>
        [JsonPropertyName("has_been_used")]
        public bool HasBeenUsed { get; set; } = false;

        /// <summary>
        /// When this seal was last used (if ever).
        /// </summary>
        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }
    }

    /// <summary>
    /// Recovery code entry with Argon2id hash.
    /// </summary>
    public sealed class RecoveryCodeEntry
    {
        /// <summary>
        /// Entry identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Argon2id hash of the recovery code (includes salt).
        /// Format: $argon2id$v=19$m=65536,t=3,p=4$salt$hash
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// Whether this code has been used.
        /// </summary>
        [JsonPropertyName("used")]
        public bool Used { get; set; } = false;

        /// <summary>
        /// When this code was used (if used).
        /// </summary>
        [JsonPropertyName("used_utc")]
        public DateTimeOffset? UsedUtc { get; set; }

        /// <summary>
        /// Which domain was recovered using this code.
        /// </summary>
        [JsonPropertyName("used_for_domain")]
        public string? UsedForDomain { get; set; }

        /// <summary>
        /// When this code was created.
        /// </summary>
        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Code number (1-10) for user reference.
        /// </summary>
        [JsonPropertyName("code_number")]
        public int CodeNumber { get; set; }
    }

    /// <summary>
    /// Recovery policy governing break-glass operations.
    /// </summary>
    public sealed class RecoveryPolicy
    {
        /// <summary>
        /// Minimum delay in seconds before recovery proceeds after entering recovery mode.
        /// Default: 60 seconds (gives time to abort if coerced).
        /// </summary>
        [JsonPropertyName("recovery_delay_seconds")]
        public int RecoveryDelaySeconds { get; set; } = 60;

        /// <summary>
        /// Require USB presence during recovery.
        /// </summary>
        [JsonPropertyName("require_usb_presence")]
        public bool RequireUsbPresence { get; set; } = true;

        /// <summary>
        /// Require explicit domain selection (no implicit "recover all").
        /// </summary>
        [JsonPropertyName("require_explicit_domain_selection")]
        public bool RequireExplicitDomainSelection { get; set; } = true;

        /// <summary>
        /// Force key rotation after any recovery operation.
        /// </summary>
        [JsonPropertyName("force_rotation_after_recovery")]
        public bool ForceRotationAfterRecovery { get; set; } = true;

        /// <summary>
        /// Invalidate remaining recovery codes after one is used.
        /// More secure but less convenient.
        /// </summary>
        [JsonPropertyName("invalidate_codes_after_use")]
        public bool InvalidateCodesAfterUse { get; set; } = false;

        /// <summary>
        /// Require second factor for recovery (e.g., PIN, hardware token).
        /// </summary>
        [JsonPropertyName("require_second_factor")]
        public bool RequireSecondFactor { get; set; } = false;

        /// <summary>
        /// Type of second factor required.
        /// </summary>
        [JsonPropertyName("second_factor_type")]
        public RecoverySecondFactorType SecondFactorType { get; set; } = RecoverySecondFactorType.None;

        /// <summary>
        /// Maximum recovery session duration in seconds.
        /// After this, recovery session is automatically invalidated.
        /// </summary>
        [JsonPropertyName("max_session_seconds")]
        public int MaxSessionSeconds { get; set; } = 300;

        /// <summary>
        /// Require specific app version for recovery (version pinning).
        /// Prevents using old vulnerable versions.
        /// </summary>
        [JsonPropertyName("min_app_version")]
        public string? MinAppVersion { get; set; }

        /// <summary>
        /// Device binding mode for recovery.
        /// </summary>
        [JsonPropertyName("device_binding_mode")]
        public RecoveryDeviceBindingMode DeviceBindingMode { get; set; } = RecoveryDeviceBindingMode.Warn;
    }

    /// <summary>
    /// Second factor types for recovery.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoverySecondFactorType
    {
        /// <summary>
        /// No second factor required.
        /// </summary>
        None,

        /// <summary>
        /// PIN required as second factor.
        /// </summary>
        Pin,

        /// <summary>
        /// Hardware token required (YubiKey, etc.).
        /// </summary>
        HardwareToken,

        /// <summary>
        /// Windows Hello or platform biometric.
        /// </summary>
        PlatformBiometric,

        /// <summary>
        /// Another device must approve (future).
        /// </summary>
        CrossDeviceApproval
    }

    /// <summary>
    /// Device binding mode for recovery operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoveryDeviceBindingMode
    {
        /// <summary>
        /// No device check.
        /// </summary>
        None,

        /// <summary>
        /// Warn if device doesn't match but allow.
        /// </summary>
        Warn,

        /// <summary>
        /// Block recovery on non-registered devices.
        /// </summary>
        Block
    }

    /// <summary>
    /// Recovery audit log entry.
    /// </summary>
    public sealed class RecoveryAuditEntry
    {
        /// <summary>
        /// Entry identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp of the event.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Type of recovery event.
        /// </summary>
        [JsonPropertyName("event_type")]
        public RecoveryEventType EventType { get; set; }

        /// <summary>
        /// Whether the operation succeeded.
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>
        /// Target domain (if applicable).
        /// </summary>
        [JsonPropertyName("target_domain")]
        public string? TargetDomain { get; set; }

        /// <summary>
        /// Recovery code number used (if applicable).
        /// </summary>
        [JsonPropertyName("code_number")]
        public int? CodeNumber { get; set; }

        /// <summary>
        /// Device fingerprint where event occurred.
        /// </summary>
        [JsonPropertyName("device_fingerprint")]
        public string? DeviceFingerprint { get; set; }

        /// <summary>
        /// App version at time of event.
        /// </summary>
        [JsonPropertyName("app_version")]
        public string? AppVersion { get; set; }

        /// <summary>
        /// Failure reason (if failed).
        /// </summary>
        [JsonPropertyName("failure_reason")]
        public string? FailureReason { get; set; }

        /// <summary>
        /// Hash of previous audit entry (chain integrity).
        /// </summary>
        [JsonPropertyName("prev_hash")]
        public string PrevHash { get; set; } = string.Empty;

        /// <summary>
        /// Hash of this entry.
        /// </summary>
        [JsonPropertyName("this_hash")]
        public string ThisHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Types of recovery events.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoveryEventType
    {
        /// <summary>
        /// Recovery mode was entered.
        /// </summary>
        RecoveryModeEntered,

        /// <summary>
        /// Recovery mode was exited (timeout or user).
        /// </summary>
        RecoveryModeExited,

        /// <summary>
        /// Recovery code validation attempted.
        /// </summary>
        CodeValidationAttempt,

        /// <summary>
        /// Domain recovery was performed.
        /// </summary>
        DomainRecovered,

        /// <summary>
        /// Key rotation was triggered post-recovery.
        /// </summary>
        KeyRotationTriggered,

        /// <summary>
        /// Recovery codes were regenerated.
        /// </summary>
        CodesRegenerated,

        /// <summary>
        /// Recovery was aborted (user cancelled).
        /// </summary>
        RecoveryAborted,

        /// <summary>
        /// Policy violation during recovery.
        /// </summary>
        PolicyViolation,

        /// <summary>
        /// USB was removed during recovery (session killed).
        /// </summary>
        UsbRemovedDuringRecovery,

        /// <summary>
        /// Device mismatch detected.
        /// </summary>
        DeviceMismatch
    }
}
