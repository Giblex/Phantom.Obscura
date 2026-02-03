using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Models.DomainStores
{
    /// <summary>
    /// Obscura domain store - encrypted vault/credential storage ONLY.
    ///
    /// This store contains:
    /// - Credential entries (passwords, notes, cards, etc.)
    /// - Categories for organization
    /// - Vault metadata (name, description)
    ///
    /// This store does NOT contain:
    /// - TOTP secrets (moved to Attestor)
    /// - Passkey data (moved to Attestor)
    /// - Recovery codes (moved to Recovery)
    ///
    /// Encrypted with: K_obscura (domain key)
    /// File: obscura.store.encrypted
    /// </summary>
    public sealed class ObscuraStore
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
        /// Human-readable vault name.
        /// </summary>
        [JsonPropertyName("vault_name")]
        public string VaultName { get; set; } = string.Empty;

        /// <summary>
        /// Optional description.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

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
        /// Credential entries in this vault.
        /// </summary>
        [JsonPropertyName("credentials")]
        public List<CredentialEntry> Credentials { get; set; } = new();

        /// <summary>
        /// Categories for organizing credentials.
        /// </summary>
        [JsonPropertyName("categories")]
        public List<CategoryModel> Categories { get; set; } = new();

        /// <summary>
        /// Autofill settings for this vault.
        /// </summary>
        [JsonPropertyName("autofill")]
        public AutofillSettings Autofill { get; set; } = new();

        /// <summary>
        /// Domain whitelist for autofill.
        /// </summary>
        [JsonPropertyName("domain_whitelist")]
        public List<string> DomainWhitelist { get; set; } = new();

        /// <summary>
        /// Explicit threat model for this vault.
        /// </summary>
        [JsonPropertyName("threat_model")]
        public ThreatModelMetadata? ThreatModel { get; set; }

        /// <summary>
        /// AI access policy for this vault.
        /// </summary>
        [JsonPropertyName("ai_policy")]
        public AIAccessPolicy? AiPolicy { get; set; }

        /// <summary>
        /// Supply chain evidence for forensics.
        /// </summary>
        [JsonPropertyName("supply_chain")]
        public SupplyChainEvidence? SupplyChain { get; set; }

        /// <summary>
        /// Security capabilities manifest for compliance.
        /// </summary>
        [JsonPropertyName("security_capabilities")]
        public SecurityCapabilitiesManifest? SecurityCapabilities { get; set; }
    }

    /// <summary>
    /// A single credential entry in the Obscura store.
    /// </summary>
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

        /// <summary>
        /// Entry type for specialized rendering.
        /// </summary>
        [JsonPropertyName("entry_type")]
        public CredentialEntryType EntryType { get; set; } = CredentialEntryType.Login;

        /// <summary>
        /// Custom fields for this entry.
        /// </summary>
        [JsonPropertyName("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new();
    }

    /// <summary>
    /// Types of credential entries.
    /// </summary>
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

    /// <summary>
    /// Custom field for extensible credential data.
    /// </summary>
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

    /// <summary>
    /// Autofill settings for the vault.
    /// </summary>
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
