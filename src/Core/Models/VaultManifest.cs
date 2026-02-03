using System;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Represents metadata about an encrypted vault. This information is stored
    /// in the encrypted manifest file on the USB drive. None of the fields
    /// contain plaintext secrets; all sensitive values (such as the derived
    /// encryption key or password) are handled outside of this class.
    /// </summary>
    public class VaultManifest
    {
        /// <summary>
        /// Version of the manifest schema. Increment this when breaking
        /// changes are made to the structure.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 3;

        /// <summary>
        /// Name of the vault (for display purposes only).
        /// </summary>
        [JsonPropertyName("vaultName")]
        public string VaultName { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the encrypted container file on disk. The path is
        /// relative to the USB root to avoid leaking host information.
        /// </summary>
        [JsonPropertyName("containerPath")]
        public string ContainerPath { get; set; } = string.Empty;

        /// <summary>
        /// When true, the vault uses a VeraCrypt outer container and
        /// <see cref="ContainerPath"/> points to that container file. When
        /// false, the vault stores the encrypted database directly on disk
        /// and <see cref="ContainerPath"/> points to the inner
        /// zero-knowledge encrypted file (e.g., vault.pvault).
        /// </summary>
        [JsonPropertyName("useVeraCrypt")]
        public bool UseVeraCrypt { get; set; } = true;

        /// <summary>
        /// Auto-generated VeraCrypt password stored encrypted in the manifest.
        /// When the user doesn't provide a password during provisioning, the app
        /// generates a cryptographically secure random password for the VeraCrypt
        /// container, encrypts it with the manifest encryption key, and stores it here.
        /// This allows transparent VeraCrypt usage without requiring user password entry.
        /// The value is Base64-encoded encrypted data containing nonce, tag, and ciphertext.
        /// </summary>
        [JsonPropertyName("veraCryptPasswordEncrypted")]
        public string? VeraCryptPasswordEncryptedBase64 { get; set; }
            = null;

        /// <summary>
        /// Size of the container in bytes. This field is recorded only to
        /// verify that the expected container exists; actual storage
        /// allocations are handled by the underlying encryption tool.
        /// </summary>
        [JsonPropertyName("containerSizeBytes")]
        public long ContainerSizeBytes { get; set; }
            = 0;

        /// <summary>
        /// UTC timestamp when the vault was created. Using UTC avoids
        /// ambiguities around daylight savings or time zones.
        /// </summary>
        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Optional description provided by the user. This may include a
        /// friendly name or purpose for the vault. Do not store secrets here.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
            = null;

        /// <summary>
        /// Salt used during key derivation. The salt must be stored in
        /// plaintext because it is needed to derive the same encryption key
        /// when unlocking the vault. It is not a secret but must be unique
        /// per vault.
        /// </summary>
        [JsonPropertyName("saltBase64")]
        public string SaltBase64 { get; set; } = string.Empty;

        /// <summary>
        /// Identifier of the encryption algorithm used (e.g. "AES-256-GCM").
        /// This allows forward compatibility as algorithms evolve. When
        /// reading a manifest the application can select the appropriate
        /// decryption algorithm.
        /// </summary>
        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "AES-256-GCM";

        /// <summary>
        /// Optional path to a keyfile on disk. Keyfiles provide an additional
        /// entropy source and are required for multi-factor setups. If null,
        /// the vault relies solely on the passphrase.
        /// </summary>
        [JsonPropertyName("keyfilePath")]
        public string? KeyfilePath { get; set; }
            = null;

        /// <summary>
        /// Flag indicating whether a hardware security device (e.g. YubiKey)
        /// must be present to unlock the vault. Applications should respect
        /// this and prompt the user accordingly.
        /// </summary>
        [JsonPropertyName("requiresHardwareToken")]
        public bool RequiresHardwareToken { get; set; }
            = false;

        /// <summary>
        /// Unique identifier of the removable drive that the vault is bound to.
        /// When provisioning a new vault this value is computed from
        /// properties of the selected USB device (for example, the volume
        /// serial number on Windows or a UUID on Linux). During unlock, the
        /// application verifies that the current drive matches this value to
        /// prevent copying the vault to another drive. Clearing or altering
        /// this field will break the binding and is not recommended.
        /// </summary>
        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }
            = null;

        /// <summary>
        /// Base32‑encoded secret used for generating time‑based one‑time
        /// passwords (TOTP) as an additional factor during unlock. This
        /// property is optional and is only set when the user enables
        /// TOTP-based multi‑factor authentication. The secret itself is
        /// derived from a cryptographically secure random source and
        /// encoded using RFC 4648 Base32. Note that storing a TOTP secret
        /// alongside the manifest requires careful protection of the
        /// manifest; ensure that encryption and key derivation are
        /// sufficiently strong.
        /// </summary>
        [JsonPropertyName("totpSecret")]
        public string? TotpSecret { get; set; }
            = null;

        /// <summary>
        /// Indicates whether TOTP (Time-based One-Time Password) verification
        /// is required as an additional authentication factor when unlocking
        /// this vault. When true, users must provide a valid TOTP code from
        /// their authenticator app in addition to the password/keyfile.
        /// </summary>
        [JsonPropertyName("requiresTotp")]
        public bool RequiresTotp { get; set; }
            = false;

        /// <summary>
        /// Public credential identifier for a registered FIDO2 passkey or
        /// WebAuthn credential. When using passkeys as a second factor, the
        /// authenticator returns an opaque credential ID which is stored
        /// here. During authentication, the application sends this ID to
        /// the authenticator to verify possession of the private key. This
        /// property is optional and may be null when passkeys are not in
        /// use.
        /// </summary>
        [JsonPropertyName("passkeyId")]
        public string? PasskeyId { get; set; }
            = null;

        /// <summary>
        /// Base64-encoded ML-KEM-768 (Kyber) public key for post-quantum hybrid encryption.
        /// This 1184-byte public key is used to encapsulate symmetric keys, providing
        /// protection against quantum computer attacks. The corresponding private key
        /// (2400 bytes) must be stored inside the encrypted container. When null,
        /// post-quantum encryption is not enabled for this vault.
        /// </summary>
        [JsonPropertyName("kemPublicKey")]
        public string? KemPublicKeyBase64 { get; set; }
            = null;

        /// <summary>
        /// Base64-encoded ciphertext from KEM encapsulation of the vault's main symmetric key.
        /// This 1088-byte value is produced during vault provisioning when post-quantum
        /// encryption is enabled. To unlock the vault, the KEM private key is used to
        /// decapsulate this ciphertext and recover the symmetric key. When null,
        /// traditional key derivation (Argon2id) is used instead.
        /// </summary>
        [JsonPropertyName("kemCiphertext")]
        public string? KemCiphertextBase64 { get; set; }
            = null;

        /// <summary>
        /// Base64-encoded encrypted ML-KEM-768 private key.
        /// The private key is encrypted with the manifest KEK (Argon2id-derived key)
        /// to protect it from unauthorized access. This allows storage in the manifest
        /// without creating circular dependencies. Stores a serialized EncryptionResult
        /// containing nonce, tag, and ciphertext.
        /// Size: 2400 bytes raw + ~100 bytes encryption overhead
        /// </summary>
        [JsonPropertyName("kemPrivateKeyEncrypted")]
        public string? KemPrivateKeyEncryptedBase64 { get; set; }
            = null;

        /// <summary>
        /// Serial number of the YubiKey device bound to this vault. When set, the
        /// application will verify that the connected YubiKey matches this serial
        /// before allowing unlock operations. This provides device-level binding
        /// in addition to credential verification. Set to null to disable serial checking.
        /// </summary>
        [JsonPropertyName("yubiKeySerial")]
        public int? YubiKeySerial { get; set; }
            = null;

        /// <summary>
        /// Base64-encoded FIDO2 credential ID for YubiKey authentication. This is returned
        /// during credential registration and must be presented during authentication
        /// challenges. Different from PasskeyId which is for platform authenticators.
        /// </summary>
        [JsonPropertyName("yubiKeyCredentialId")]
        public string? YubiKeyCredentialIdBase64 { get; set; }
            = null;

        /// <summary>
        /// Base64-encoded COSE public key for YubiKey FIDO2 signature verification.
        /// This public key is extracted during credential registration and used to
        /// verify authentication assertions. The key is encoded in COSE format per
        /// the WebAuthn specification.
        /// </summary>
        [JsonPropertyName("yubiKeyPublicKey")]
        public string? YubiKeyPublicKeyBase64 { get; set; }
            = null;

        /// <summary>
        /// Indicates whether automatic injection of credentials into
        /// websites or applications is enabled for this vault. When
        /// disabled, the application will not attempt to auto‑fill any
        /// fields regardless of other settings.
        /// </summary>
        [JsonPropertyName("autoFillEnabled")]
        public bool AutoFillEnabled { get; set; } = false;

        /// <summary>
        /// Determines whether the username portion of a credential should
        /// be inserted automatically. This allows users to auto‑fill
        /// passwords while still typing usernames manually.
        /// </summary>
        [JsonPropertyName("autoFillUsername")]
        public bool AutoFillUsername { get; set; } = true;

        /// <summary>
        /// Determines whether the password portion of a credential should
        /// be inserted automatically. Disabling this will require the
        /// user to type their password even when the vault auto‑fill is
        /// enabled.
        /// </summary>
        [JsonPropertyName("autoFillPassword")]
        public bool AutoFillPassword { get; set; } = true;

        /// <summary>
        /// Enables or disables automatic creation of encrypted backup
        /// files each time the manifest or vault database is updated. When
        /// enabled, the application will write a backup copy of the
        /// manifest to a <c>backups</c> directory on the removable
        /// drive and prune older backups according to
        /// <see cref="BackupRetentionDays"/>. Backups are encrypted
        /// using the same passphrase‑derived key as the primary
        /// manifest to ensure they remain confidential if copied or
        /// exfiltrated. This flag is false by default because
        /// automatic backups consume additional storage and may not
        /// be required in all scenarios.
        /// </summary>
        [JsonPropertyName("autoBackupEnabled")]
        public bool AutoBackupEnabled { get; set; } = false;

        /// <summary>
        /// Number of days to retain automatic backups before they are
        /// deleted. The backup service will remove any backup files
        /// older than this value. A default of 3 days provides a
        /// short window to recover from accidental deletion or
        /// corruption without accumulating excessive history. Users
        /// should adjust this according to their own recovery
        /// requirements. Values less than 1 disable pruning.
        /// </summary>
        [JsonPropertyName("backupRetentionDays")]
        public int BackupRetentionDays { get; set; } = 3;

        /// <summary>
        /// Comma separated list of domain names for which auto‑fill is
        /// permitted. Domains should be specified without scheme.
        /// When empty, auto‑fill is allowed on all domains. This
        /// whitelist is intended to mitigate the risk of credential
        /// injection on malicious sites.
        /// </summary>
        [JsonPropertyName("domainWhitelist")]
        public string? DomainWhitelist { get; set; } = string.Empty;

        /// <summary>
        /// Tracks the number of consecutive failed unlock attempts. This
        /// counter is incremented each time an incorrect passphrase or
        /// multi‑factor token is provided. When the count reaches
        /// <see cref="MaxFailedAttempts"/>, the vault will either
        /// enforce a cooldown period (see <see cref="LockedUntilUtc"/>) or
        /// destroy itself if <see cref="SelfDestructEnabled"/> is true.
        /// Reset this value to zero after a successful unlock.
        /// </summary>
        [JsonPropertyName("failedAttempts")]
        public int FailedAttempts { get; set; } = 0;

        /// <summary>
        /// Maximum number of consecutive failed attempts allowed before
        /// triggering a lockout or self‑destruct. A sensible default of 5
        /// provides some tolerance for mistyped passwords while still
        /// protecting against brute‑force attacks. Users in high‑security
        /// environments can lower this value.
        /// </summary>
        [JsonPropertyName("maxFailedAttempts")]
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>
        /// When true the vault will permanently wipe its manifest and
        /// container when the failed attempt counter reaches
        /// <see cref="MaxFailedAttempts"/>. This mode should only be
        /// enabled by advanced users who understand the risks: any
        /// accidental mistype could irrevocably destroy the data. When
        /// false the vault will simply lock for a cooldown period.
        /// </summary>
        [JsonPropertyName("selfDestructEnabled")]
        public bool SelfDestructEnabled { get; set; } = false;

        /// <summary>
        /// If set, indicates that the vault is currently locked out due to
        /// repeated failed attempts. The value stores the UTC time until
        /// which unlock attempts should be refused. Applications must
        /// compare the current UTC time to this property and disallow
        /// unlocking until the cooldown has expired. Once the vault is
        /// successfully unlocked the property should be cleared.
        /// </summary>
        [JsonPropertyName("lockedUntilUtc")]
        public DateTimeOffset? LockedUntilUtc { get; set; }
            = null;

        /// <summary>
        /// Ordered list of categories for organizing credentials. This property
        /// was added to allow the UI to persist category metadata alongside
        /// the rest of the manifest. It may be null for older manifests and
        /// should be treated as optional by callers.
        /// </summary>
        [JsonPropertyName("categories")]
        public System.Collections.Generic.List<CategoryModel>? Categories { get; set; } = null;

        /// <summary>
        /// Overall security posture of the vault based on Defence Engine threat detection.
        /// Tracked states: Normal (no threats), Suspicious (warnings detected), Compromised (critical breach).
        /// Defaults to Normal for backwards compatibility.
        /// </summary>
        [JsonPropertyName("securityState")]
        public VaultSecurityState SecurityState { get; set; } = VaultSecurityState.Normal;

        /// <summary>
        /// When true, the vault requires a full rekey operation before normal use can resume.
        /// Set by the Defence Engine when a compromised state is detected or when security
        /// policy mandates regeneration of all encryption keys. The user must provide current
        /// credentials, generate new keys, and re-encrypt the vault database.
        /// </summary>
        [JsonPropertyName("rekeyRequired")]
        public bool RekeyRequired { get; set; } = false;

        /// <summary>
        /// List of device fingerprints that have been explicitly trusted for this vault.
        /// When a vault is opened from a new device, the Defence Engine raises a
        /// NewDeviceFingerprint threat. Users can choose to trust the device, adding
        /// its fingerprint to this list. Subsequent access from trusted devices will
        /// not trigger warnings. Defaults to empty list for backwards compatibility.
        /// </summary>
        [JsonPropertyName("trustedDevices")]
        public System.Collections.Generic.List<DeviceFingerprint> TrustedDevices { get; set; }
            = new System.Collections.Generic.List<DeviceFingerprint>();

        /// <summary>
        /// Rotation metadata to help the client prompt for periodic rekey.
        /// </summary>
        [JsonPropertyName("lastKeyRotation")]
        public DateTimeOffset LastKeyRotation { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("keyRotationCount")]
        public int KeyRotationCount { get; set; } = 0;

        [JsonPropertyName("daysUntilRotationRequired")]
        public int DaysUntilRotationRequired { get; set; } = 90;

        [JsonPropertyName("keyRotationPending")]
        public bool KeyRotationPending { get; set; } = false;

        /// <summary>
        /// Optional HMAC-SHA256 signature of critical manifest fields for additional integrity
        /// verification beyond GCM authentication. When set, the signature is computed over
        /// a canonical representation of: ContainerPath, SaltBase64, DeviceId, KemPublicKeyBase64,
        /// and YubiKeySerial. The signature key is derived from the manifest KEK. This provides
        /// defense-in-depth against manifest tampering even if an attacker obtains the KEK.
        /// </summary>
        [JsonPropertyName("integritySignature")]
        public string? IntegritySignatureBase64 { get; set; } = null;

        /// <summary>
        /// Timestamp when the integrity signature was last updated. Used to detect stale
        /// signatures and determine if re-signing is needed after manifest changes.
        /// </summary>
        [JsonPropertyName("signatureTimestamp")]
        public DateTimeOffset? SignatureTimestamp { get; set; } = null;

        /// <summary>
        /// Base64-encoded salt for PIN lock key derivation. When set, this vault
        /// has a PIN lock configured. The PIN is hashed with PBKDF2 using this salt.
        /// This allows per-vault PIN configuration independent of global settings.
        /// </summary>
        [JsonPropertyName("pinSaltBase64")]
        public string? PinSaltBase64 { get; set; } = null;

        /// <summary>
        /// Base64-encoded PBKDF2-SHA256 hash of the vault's PIN. Used to verify
        /// PIN entry during soft lock unlock. The hash is computed with 150,000
        /// iterations using the salt stored in PinSaltBase64.
        /// </summary>
        [JsonPropertyName("pinHashBase64")]
        public string? PinHashBase64 { get; set; } = null;

        /// <summary>
        /// Number of PBKDF2 iterations used for PIN hashing. Defaults to 150,000
        /// but can be increased for stronger protection against brute force attacks.
        /// </summary>
        [JsonPropertyName("pinPbkdf2Iterations")]
        public int PinPbkdf2Iterations { get; set; } = 150_000;

        /// <summary>
        /// Array of recovery code metadata for account recovery.
        /// Recovery codes provide a fallback authentication method when all other factors are lost.
        /// Each code is single-use and cryptographically hashed for secure storage.
        /// Users should print these codes and store them securely offline.
        /// </summary>
        [JsonPropertyName("recoveryCodes")]
        public RecoveryCode[]? RecoveryCodes { get; set; } = null;

        /// <summary>
        /// Base64-encoded ECDSA P-256 public key for backup signature verification.
        /// Used to verify the integrity and authenticity of backup files.
        /// Generated once during vault creation and stored in the manifest.
        /// </summary>
        [JsonPropertyName("backupSigningPublicKey")]
        public string? BackupSigningPublicKeyBase64 { get; set; } = null;

        /// <summary>
        /// Base64-encoded encrypted ECDSA P-256 private key for backup signing.
        /// Encrypted with the vault passphrase using AES-256-GCM.
        /// The private key is needed to create signed backups.
        /// </summary>
        [JsonPropertyName("encryptedBackupSigningKey")]
        public string? EncryptedBackupSigningKeyBase64 { get; set; } = null;

        // ============================================================================
        // SECURITY METADATA (v4.0+)
        // Explicit threat model, AI protection, supply-chain evidence, and compliance
        // ============================================================================

        /// <summary>
        /// Explicit threat model metadata for hostile-environment vault operation.
        /// Makes security assumptions first-class for UI warnings, AI auditing, and compliance.
        /// Designed for zero-trust environments where no component is trusted by default.
        /// </summary>
        [JsonPropertyName("threatModel")]
        public ThreatModelMetadata? ThreatModel { get; set; } = null;

        /// <summary>
        /// AI-aware access policy that actively defends against AI leakage.
        /// Blocks clipboard sharing to AI contexts, prevents autofill to AI chat interfaces,
        /// and supports AI-safe view mode with masked secrets.
        /// </summary>
        [JsonPropertyName("aiAccessPolicy")]
        public AIAccessPolicy? AiAccessPolicy { get; set; } = null;

        /// <summary>
        /// Supply chain evidence capture for forensic analysis.
        /// Stores hashes of tools that accessed the vault, optional known-good fingerprint,
        /// and access history. No scanning or monitoring - just evidence capture.
        /// </summary>
        [JsonPropertyName("supplyChainEvidence")]
        public SupplyChainEvidence? SupplyChainEvidence { get; set; } = null;

        /// <summary>
        /// Security capabilities manifest for compliance documentation.
        /// Exposes provable design capabilities for audit responses, investor decks,
        /// and enterprise pilots. No dashboards - just provable design.
        /// </summary>
        [JsonPropertyName("securityCapabilities")]
        public SecurityCapabilitiesManifest? SecurityCapabilities { get; set; } = null;
    }

    /// <summary>
    /// Metadata for a single recovery code.
    /// </summary>
    public sealed class RecoveryCode
    {
        /// <summary>
        /// Argon2 hash of the recovery code (includes salt).
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// Whether this recovery code has been used.
        /// </summary>
        [JsonPropertyName("used")]
        public bool Used { get; set; }

        /// <summary>
        /// Timestamp when this recovery code was used (if Used is true).
        /// </summary>
        [JsonPropertyName("usedAtUtc")]
        public DateTimeOffset? UsedAtUtc { get; set; }

        /// <summary>
        /// Timestamp when this recovery code was created.
        /// </summary>
        [JsonPropertyName("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
