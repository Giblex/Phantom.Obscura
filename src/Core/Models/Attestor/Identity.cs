using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{
    /// <summary>
    /// A logical identity "thing" you can authenticate as.
    /// Represents services, accounts, keypairs, certificates, passkeys, etc.
    /// Secrets are never stored here - only referenced via vault_ref.
    /// </summary>
    public sealed class Identity
    {
        /// <summary>
        /// Unique identifier for this identity (UUIDv7 recommended).
        /// </summary>
        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Kind of identity this represents.
        /// </summary>
        [JsonPropertyName("kind")]
        public IdentityKind Kind { get; set; } = IdentityKind.Totp;

        /// <summary>
        /// Human-readable label for this identity.
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Service/issuer that this identity authenticates to.
        /// </summary>
        [JsonPropertyName("issuer")]
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// Account hint (username, email) - not the secret itself.
        /// </summary>
        [JsonPropertyName("account_hint")]
        public string? AccountHint { get; set; }

        /// <summary>
        /// User-defined tags for organization and filtering.
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Risk tier classification for policy decisions.
        /// </summary>
        [JsonPropertyName("risk_tier")]
        public RiskTier RiskTier { get; set; } = RiskTier.Medium;

        /// <summary>
        /// Reference to the vault where secrets are stored.
        /// </summary>
        [JsonPropertyName("vault_ref_id")]
        public string VaultRefId { get; set; } = string.Empty;

        /// <summary>
        /// Reference to the specific item within the vault (path or ID).
        /// </summary>
        [JsonPropertyName("secret_item_ref")]
        public string SecretItemRef { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when this identity was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp when this identity was last updated.
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Current status of this identity.
        /// </summary>
        [JsonPropertyName("status")]
        public IdentityStatus Status { get; set; } = IdentityStatus.Active;

        /// <summary>
        /// Bindings that control when/where this identity is valid.
        /// </summary>
        [JsonPropertyName("bindings")]
        public IdentityBindings Bindings { get; set; } = new();

        /// <summary>
        /// Reference to the policy set that governs this identity.
        /// </summary>
        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        /// <summary>
        /// Optional expiration date for this identity.
        /// </summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>
        /// Optional notes about this identity.
        /// </summary>
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Last time this identity was used for authentication.
        /// </summary>
        [JsonPropertyName("last_used_at")]
        public DateTimeOffset? LastUsedAt { get; set; }

        /// <summary>
        /// Total number of times this identity has been used.
        /// </summary>
        [JsonPropertyName("use_count")]
        public long UseCount { get; set; } = 0;
    }

    /// <summary>
    /// Types of identity supported by Attestor.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IdentityKind
    {
        /// <summary>
        /// Time-based One-Time Password (RFC 6238).
        /// </summary>
        Totp,

        /// <summary>
        /// HMAC-based One-Time Password (RFC 4226).
        /// </summary>
        Hotp,

        /// <summary>
        /// FIDO2/WebAuthn passkey.
        /// </summary>
        Passkey,

        /// <summary>
        /// X.509 certificate (client auth, code signing, etc.).
        /// </summary>
        Certificate,

        /// <summary>
        /// SSH key pair.
        /// </summary>
        SshKey,

        /// <summary>
        /// API key or token.
        /// </summary>
        ApiKey,

        /// <summary>
        /// Recovery codes (single-use backup codes).
        /// </summary>
        RecoveryCodes,

        /// <summary>
        /// GPG/PGP key pair.
        /// </summary>
        PgpKey,

        /// <summary>
        /// Generic signing key (Ed25519, ECDSA, etc.).
        /// </summary>
        SigningKey,

        /// <summary>
        /// OAuth/OIDC refresh token.
        /// </summary>
        OAuthToken,

        /// <summary>
        /// Hardware token (YubiKey, etc.).
        /// </summary>
        HardwareToken
    }

    /// <summary>
    /// Risk tier classification for policy decisions.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RiskTier
    {
        /// <summary>
        /// Low-risk identity (forums, newsletters, etc.).
        /// </summary>
        Low,

        /// <summary>
        /// Medium-risk identity (social media, general services).
        /// </summary>
        Medium,

        /// <summary>
        /// High-risk identity (email, cloud storage, dev tools).
        /// </summary>
        High,

        /// <summary>
        /// Critical identity (banking, admin accounts, signing keys).
        /// </summary>
        Critical
    }

    /// <summary>
    /// Status of an identity.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IdentityStatus
    {
        /// <summary>
        /// Identity is active and can be used.
        /// </summary>
        Active,

        /// <summary>
        /// Identity is temporarily paused (user choice).
        /// </summary>
        Paused,

        /// <summary>
        /// Identity has been revoked (security incident).
        /// </summary>
        Revoked,

        /// <summary>
        /// Identity has expired.
        /// </summary>
        Expired,

        /// <summary>
        /// Identity is pending setup/verification.
        /// </summary>
        Pending
    }

    /// <summary>
    /// Bindings that control when/where an identity is valid.
    /// Encodes conditions like "only valid when USB present" or "only for these origins".
    /// </summary>
    public sealed class IdentityBindings
    {
        /// <summary>
        /// Device binding mode: none (portable), soft (warn on new device), hard (block).
        /// </summary>
        [JsonPropertyName("device_binding")]
        public DeviceBindingMode DeviceBinding { get; set; } = DeviceBindingMode.None;

        /// <summary>
        /// Require USB token to be present.
        /// </summary>
        [JsonPropertyName("usb_binding")]
        public bool UsbBinding { get; set; } = true;

        /// <summary>
        /// Origins where this identity may be used (for autofill/passkey).
        /// </summary>
        [JsonPropertyName("origin_binding")]
        public List<string> OriginBinding { get; set; } = new();

        /// <summary>
        /// Specific device fingerprints allowed to use this identity.
        /// </summary>
        [JsonPropertyName("allowed_device_ids")]
        public List<string> AllowedDeviceIds { get; set; } = new();

        /// <summary>
        /// Time-of-day restrictions (e.g., "09:00-17:00").
        /// </summary>
        [JsonPropertyName("time_restrictions")]
        public List<string> TimeRestrictions { get; set; } = new();

        /// <summary>
        /// Geographic restrictions (country codes).
        /// </summary>
        [JsonPropertyName("geo_restrictions")]
        public List<string> GeoRestrictions { get; set; } = new();

        /// <summary>
        /// Network restrictions (allowed IP ranges).
        /// </summary>
        [JsonPropertyName("network_restrictions")]
        public List<string> NetworkRestrictions { get; set; } = new();

        /// <summary>
        /// Require hardware token for this identity.
        /// </summary>
        [JsonPropertyName("require_hardware_token")]
        public bool RequireHardwareToken { get; set; } = false;

        /// <summary>
        /// Require offline-only usage (no network during use).
        /// </summary>
        [JsonPropertyName("offline_only")]
        public bool OfflineOnly { get; set; } = false;
    }

    /// <summary>
    /// Device binding modes for identity portability control.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeviceBindingMode
    {
        /// <summary>
        /// Identity is portable - can be used anywhere.
        /// </summary>
        None,

        /// <summary>
        /// Soft binding - warn when used on new device.
        /// </summary>
        Soft,

        /// <summary>
        /// Hard binding - block usage on non-registered devices.
        /// </summary>
        Hard
    }

    /// <summary>
    /// Group of identities for shared policy application.
    /// </summary>
    public sealed class IdentityGroup
    {
        /// <summary>
        /// Unique identifier for this group.
        /// </summary>
        [JsonPropertyName("group_id")]
        public string GroupId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Human-readable name for this group.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this group represents.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Identity IDs that belong to this group.
        /// </summary>
        [JsonPropertyName("identity_ids")]
        public List<string> IdentityIds { get; set; } = new();

        /// <summary>
        /// Policy set applied to all identities in this group.
        /// </summary>
        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        /// <summary>
        /// Timestamp when this group was created.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp when this group was last updated.
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
