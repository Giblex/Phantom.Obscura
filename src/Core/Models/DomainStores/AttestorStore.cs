using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Attestor;

namespace PhantomVault.Core.Models.DomainStores
{
    /// <summary>
    /// Attestor domain store - identity and authentication authority.
    ///
    /// This store contains:
    /// - TOTP/HOTP seeds and configuration
    /// - Passkey credential metadata (private keys never leave this store)
    /// - Certificate metadata and encrypted private keys
    /// - SSH key metadata and encrypted private keys
    /// - Identity policies and bindings
    /// - Approval audit trail
    ///
    /// This store does NOT contain:
    /// - Passwords or login credentials (Obscura domain)
    /// - Recovery codes (Recovery domain)
    ///
    /// Encrypted with: K_attestor (domain key)
    /// File: attestor.store.encrypted
    ///
    /// CRITICAL: Attestor performs cryptographic operations.
    /// Keys in this store are USED, never EXPORTED.
    /// </summary>
    public sealed class AttestorStore
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
        /// Attestor profile with threat assumptions and defaults.
        /// </summary>
        [JsonPropertyName("profile")]
        public AttestorProfile Profile { get; set; } = new();

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
        /// TOTP/HOTP identities.
        /// </summary>
        [JsonPropertyName("totp_identities")]
        public List<TotpIdentity> TotpIdentities { get; set; } = new();

        /// <summary>
        /// Passkey credentials (non-exportable).
        /// Private keys encrypted with K_attestor.
        /// </summary>
        [JsonPropertyName("passkeys")]
        public List<PasskeyIdentity> Passkeys { get; set; } = new();

        /// <summary>
        /// Certificate identities.
        /// Private keys encrypted with K_attestor.
        /// </summary>
        [JsonPropertyName("certificates")]
        public List<CertificateIdentity> Certificates { get; set; } = new();

        /// <summary>
        /// SSH key identities.
        /// Private keys encrypted with K_attestor.
        /// </summary>
        [JsonPropertyName("ssh_keys")]
        public List<SshKeyIdentity> SshKeys { get; set; } = new();

        /// <summary>
        /// Identity groups for shared policy application.
        /// </summary>
        [JsonPropertyName("groups")]
        public List<IdentityGroup> Groups { get; set; } = new();

        /// <summary>
        /// Policy sets attached to identities/groups.
        /// </summary>
        [JsonPropertyName("policy_sets")]
        public List<PolicySet> PolicySets { get; set; } = new();

        /// <summary>
        /// Recent approval artifacts for audit.
        /// </summary>
        [JsonPropertyName("recent_approvals")]
        public List<ApprovalArtifact> RecentApprovals { get; set; } = new();

        /// <summary>
        /// Maximum approvals to retain.
        /// </summary>
        [JsonPropertyName("max_approvals_retained")]
        public int MaxApprovalsRetained { get; set; } = 100;

        /// <summary>
        /// Hardware token bindings (YubiKey, etc.).
        /// </summary>
        [JsonPropertyName("hardware_tokens")]
        public List<HardwareTokenBinding> HardwareTokens { get; set; } = new();

        /// <summary>
        /// Relying party allowlist for passkeys.
        /// Empty = deny all (safe default).
        /// </summary>
        [JsonPropertyName("rp_allowlist")]
        public List<RelyingPartyEntry> RpAllowlist { get; set; } = new();
    }

    /// <summary>
    /// TOTP/HOTP identity with seed stored encrypted.
    /// </summary>
    public sealed class TotpIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("issuer")]
        public string Issuer { get; set; } = string.Empty;

        [JsonPropertyName("account_hint")]
        public string? AccountHint { get; set; }

        /// <summary>
        /// TOTP seed encrypted with K_attestor.
        /// Base64(nonce || ciphertext || tag)
        /// </summary>
        [JsonPropertyName("seed_encrypted")]
        public string SeedEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("algorithm")]
        public TotpAlgorithm Algorithm { get; set; } = TotpAlgorithm.SHA1;

        [JsonPropertyName("digits")]
        public int Digits { get; set; } = 6;

        [JsonPropertyName("period_seconds")]
        public int PeriodSeconds { get; set; } = 30;

        [JsonPropertyName("is_hotp")]
        public bool IsHotp { get; set; } = false;

        [JsonPropertyName("hotp_counter")]
        public long HotpCounter { get; set; } = 0;

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }

        [JsonPropertyName("use_count")]
        public long UseCount { get; set; } = 0;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        /// <summary>
        /// Whether export is allowed (should be false by default).
        /// </summary>
        [JsonPropertyName("export_allowed")]
        public bool ExportAllowed { get; set; } = false;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TotpAlgorithm
    {
        SHA1,
        SHA256,
        SHA512
    }

    /// <summary>
    /// Passkey identity with non-exportable private key.
    /// </summary>
    public sealed class PasskeyIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Relying party ID (e.g., "example.com").
        /// MUST be validated on every authentication.
        /// </summary>
        [JsonPropertyName("rp_id")]
        public string RpId { get; set; } = string.Empty;

        /// <summary>
        /// Relying party name for display.
        /// </summary>
        [JsonPropertyName("rp_name")]
        public string? RpName { get; set; }

        /// <summary>
        /// WebAuthn credential ID (base64).
        /// </summary>
        [JsonPropertyName("credential_id")]
        public string CredentialId { get; set; } = string.Empty;

        /// <summary>
        /// User handle from registration (base64).
        /// </summary>
        [JsonPropertyName("user_handle")]
        public string? UserHandle { get; set; }

        /// <summary>
        /// Public key in COSE format (base64).
        /// </summary>
        [JsonPropertyName("public_key")]
        public string PublicKey { get; set; } = string.Empty;

        /// <summary>
        /// Private key encrypted with K_attestor.
        /// NON-EXPORTABLE: Used only for signing, never returned to caller.
        /// </summary>
        [JsonPropertyName("private_key_encrypted")]
        public string PrivateKeyEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Signature algorithm (e.g., "ES256").
        /// </summary>
        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "ES256";

        /// <summary>
        /// Signature counter for replay detection.
        /// </summary>
        [JsonPropertyName("sign_count")]
        public uint SignCount { get; set; } = 0;

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        /// <summary>
        /// Whether this passkey requires user verification (PIN/biometric).
        /// </summary>
        [JsonPropertyName("user_verification_required")]
        public bool UserVerificationRequired { get; set; } = true;

        /// <summary>
        /// Device binding - passkey only works on specific devices.
        /// </summary>
        [JsonPropertyName("device_bound")]
        public bool DeviceBound { get; set; } = true;

        /// <summary>
        /// USB presence required for signing.
        /// </summary>
        [JsonPropertyName("usb_presence_required")]
        public bool UsbPresenceRequired { get; set; } = true;
    }

    /// <summary>
    /// Certificate identity with encrypted private key.
    /// </summary>
    public sealed class CertificateIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("issuer")]
        public string Issuer { get; set; } = string.Empty;

        [JsonPropertyName("serial_number")]
        public string SerialNumber { get; set; } = string.Empty;

        [JsonPropertyName("thumbprint")]
        public string Thumbprint { get; set; } = string.Empty;

        /// <summary>
        /// Certificate in PEM or DER format (base64).
        /// </summary>
        [JsonPropertyName("certificate")]
        public string Certificate { get; set; } = string.Empty;

        /// <summary>
        /// Private key encrypted with K_attestor.
        /// </summary>
        [JsonPropertyName("private_key_encrypted")]
        public string PrivateKeyEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("not_before")]
        public DateTimeOffset NotBefore { get; set; }

        [JsonPropertyName("not_after")]
        public DateTimeOffset NotAfter { get; set; }

        [JsonPropertyName("key_usage")]
        public List<string> KeyUsage { get; set; } = new();

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("export_allowed")]
        public bool ExportAllowed { get; set; } = false;
    }

    /// <summary>
    /// SSH key identity with encrypted private key.
    /// </summary>
    public sealed class SshKeyIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        /// <summary>
        /// Public key in OpenSSH format.
        /// </summary>
        [JsonPropertyName("public_key")]
        public string PublicKey { get; set; } = string.Empty;

        /// <summary>
        /// Private key encrypted with K_attestor.
        /// </summary>
        [JsonPropertyName("private_key_encrypted")]
        public string PrivateKeyEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("key_type")]
        public string KeyType { get; set; } = "ed25519";

        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; set; } = string.Empty;

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("export_allowed")]
        public bool ExportAllowed { get; set; } = false;

        /// <summary>
        /// Hosts this key is authorized for.
        /// </summary>
        [JsonPropertyName("authorized_hosts")]
        public List<string> AuthorizedHosts { get; set; } = new();
    }

    /// <summary>
    /// Hardware token binding (YubiKey, etc.).
    /// </summary>
    public sealed class HardwareTokenBinding
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "YubiKey";

        [JsonPropertyName("serial_number")]
        public string? SerialNumber { get; set; }

        [JsonPropertyName("firmware_version")]
        public string? FirmwareVersion { get; set; }

        [JsonPropertyName("credential_id")]
        public string? CredentialId { get; set; }

        [JsonPropertyName("public_key")]
        public string? PublicKey { get; set; }

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }
    }

    /// <summary>
    /// Relying party entry for passkey allowlist.
    /// </summary>
    public sealed class RelyingPartyEntry
    {
        [JsonPropertyName("rp_id")]
        public string RpId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("added_utc")]
        public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("auto_approve")]
        public bool AutoApprove { get; set; } = false;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }
}
