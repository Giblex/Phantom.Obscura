using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.Attestor
{

    public sealed class Identity
    {

        [JsonPropertyName("identity_id")]
        public string IdentityId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("kind")]
        public IdentityKind Kind { get; set; } = IdentityKind.Totp;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("issuer")]
        public string Issuer { get; set; } = string.Empty;

        [JsonPropertyName("account_hint")]
        public string? AccountHint { get; set; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("risk_tier")]
        public RiskTier RiskTier { get; set; } = RiskTier.Medium;

        [JsonPropertyName("vault_ref_id")]
        public string VaultRefId { get; set; } = string.Empty;

        [JsonPropertyName("secret_item_ref")]
        public string SecretItemRef { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("status")]
        public IdentityStatus Status { get; set; } = IdentityStatus.Active;

        [JsonPropertyName("bindings")]
        public IdentityBindings Bindings { get; set; } = new();

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("last_used_at")]
        public DateTimeOffset? LastUsedAt { get; set; }

        [JsonPropertyName("use_count")]
        public long UseCount { get; set; } = 0;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IdentityKind
    {

        Totp,

        Hotp,

        Passkey,

        Certificate,

        SshKey,

        ApiKey,

        RecoveryCodes,

        PgpKey,

        SigningKey,

        OAuthToken,

        HardwareToken
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RiskTier
    {

        Low,

        Medium,

        High,

        Critical
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IdentityStatus
    {

        Active,

        Paused,

        Revoked,

        Expired,

        Pending
    }

    public sealed class IdentityBindings
    {

        [JsonPropertyName("device_binding")]
        public DeviceBindingMode DeviceBinding { get; set; } = DeviceBindingMode.None;

        [JsonPropertyName("usb_binding")]
        public bool UsbBinding { get; set; } = true;

        [JsonPropertyName("origin_binding")]
        public List<string> OriginBinding { get; set; } = new();

        [JsonPropertyName("allowed_device_ids")]
        public List<string> AllowedDeviceIds { get; set; } = new();

        [JsonPropertyName("time_restrictions")]
        public List<string> TimeRestrictions { get; set; } = new();

        [JsonPropertyName("geo_restrictions")]
        public List<string> GeoRestrictions { get; set; } = new();

        [JsonPropertyName("network_restrictions")]
        public List<string> NetworkRestrictions { get; set; } = new();

        [JsonPropertyName("require_hardware_token")]
        public bool RequireHardwareToken { get; set; } = false;

        [JsonPropertyName("offline_only")]
        public bool OfflineOnly { get; set; } = false;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeviceBindingMode
    {

        None,

        Soft,

        Hard
    }

    public sealed class IdentityGroup
    {

        [JsonPropertyName("group_id")]
        public string GroupId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("identity_ids")]
        public List<string> IdentityIds { get; set; } = new();

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

