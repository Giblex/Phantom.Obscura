using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Attestor;

namespace PhantomVault.Core.Models.DomainStores
{

    public sealed class AttestorStore
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("store_id")]
        public string StoreId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("profile")]
        public AttestorProfile Profile { get; set; } = new();

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("modified_utc")]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("totp_identities")]
        public List<TotpIdentity> TotpIdentities { get; set; } = new();

        [JsonPropertyName("passkeys")]
        public List<PasskeyIdentity> Passkeys { get; set; } = new();

        [JsonPropertyName("certificates")]
        public List<CertificateIdentity> Certificates { get; set; } = new();

        [JsonPropertyName("ssh_keys")]
        public List<SshKeyIdentity> SshKeys { get; set; } = new();

        [JsonPropertyName("groups")]
        public List<IdentityGroup> Groups { get; set; } = new();

        [JsonPropertyName("policy_sets")]
        public List<PolicySet> PolicySets { get; set; } = new();

        [JsonPropertyName("recent_approvals")]
        public List<ApprovalArtifact> RecentApprovals { get; set; } = new();

        [JsonPropertyName("max_approvals_retained")]
        public int MaxApprovalsRetained { get; set; } = 100;

        [JsonPropertyName("hardware_tokens")]
        public List<HardwareTokenBinding> HardwareTokens { get; set; } = new();

        [JsonPropertyName("rp_allowlist")]
        public List<RelyingPartyEntry> RpAllowlist { get; set; } = new();
    }

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

    public sealed class PasskeyIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("rp_id")]
        public string RpId { get; set; } = string.Empty;

        [JsonPropertyName("rp_name")]
        public string? RpName { get; set; }

        [JsonPropertyName("credential_id")]
        public string CredentialId { get; set; } = string.Empty;

        [JsonPropertyName("user_handle")]
        public string? UserHandle { get; set; }

        [JsonPropertyName("public_key")]
        public string PublicKey { get; set; } = string.Empty;

        [JsonPropertyName("private_key_encrypted")]
        public string PrivateKeyEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "ES256";

        [JsonPropertyName("sign_count")]
        public uint SignCount { get; set; } = 0;

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }

        [JsonPropertyName("policy_set_id")]
        public string? PolicySetId { get; set; }

        [JsonPropertyName("user_verification_required")]
        public bool UserVerificationRequired { get; set; } = true;

        [JsonPropertyName("device_bound")]
        public bool DeviceBound { get; set; } = true;

        [JsonPropertyName("usb_presence_required")]
        public bool UsbPresenceRequired { get; set; } = true;
    }

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

        [JsonPropertyName("certificate")]
        public string Certificate { get; set; } = string.Empty;

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

    public sealed class SshKeyIdentity
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("public_key")]
        public string PublicKey { get; set; } = string.Empty;

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

        [JsonPropertyName("authorized_hosts")]
        public List<string> AuthorizedHosts { get; set; } = new();
    }

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

