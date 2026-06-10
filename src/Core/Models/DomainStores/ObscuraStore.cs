using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Models.DomainStores
{

    public sealed class ObscuraStore
    {

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("store_id")]
        public string StoreId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("vault_name")]
        public string VaultName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("modified_utc")]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("credentials")]
        public List<CredentialEntry> Credentials { get; set; } = new();

        [JsonPropertyName("categories")]
        public List<CategoryModel> Categories { get; set; } = new();

        [JsonPropertyName("autofill")]
        public AutofillSettings Autofill { get; set; } = new();

        [JsonPropertyName("domain_whitelist")]
        public List<string> DomainWhitelist { get; set; } = new();

        [JsonPropertyName("threat_model")]
        public ThreatModelMetadata? ThreatModel { get; set; }

        [JsonPropertyName("ai_policy")]
        public AIAccessPolicy? AiPolicy { get; set; }

        [JsonPropertyName("supply_chain")]
        public SupplyChainEvidence? SupplyChain { get; set; }

        [JsonPropertyName("security_capabilities")]
        public SecurityCapabilitiesManifest? SecurityCapabilities { get; set; }
    }

    public sealed class CredentialEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password_encrypted")]
        public string? PasswordEncrypted { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("notes_encrypted")]
        public string? NotesEncrypted { get; set; }

        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("modified_utc")]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("last_used_utc")]
        public DateTimeOffset? LastUsedUtc { get; set; }

        [JsonPropertyName("use_count")]
        public int UseCount { get; set; }

        [JsonPropertyName("entry_type")]
        public CredentialEntryType EntryType { get; set; } = CredentialEntryType.Login;

        [JsonPropertyName("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CredentialEntryType
    {
        Login,
        SecureNote,
        CreditCard,
        Identity,
        WifiPassword,
        ApiKey,
        SshKey,
        Document
    }

    public sealed class CustomField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value_encrypted")]
        public string? ValueEncrypted { get; set; }

        [JsonPropertyName("is_sensitive")]
        public bool IsSensitive { get; set; } = true;

        [JsonPropertyName("field_type")]
        public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CustomFieldType
    {
        Text,
        Password,
        Url,
        Email,
        Phone,
        Date,
        Number
    }

    public sealed class AutofillSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("autofill_username")]
        public bool AutofillUsername { get; set; } = true;

        [JsonPropertyName("autofill_password")]
        public bool AutofillPassword { get; set; } = true;

        [JsonPropertyName("require_user_confirmation")]
        public bool RequireUserConfirmation { get; set; } = true;

        [JsonPropertyName("clear_clipboard_seconds")]
        public int ClearClipboardSeconds { get; set; } = 30;
    }
}

