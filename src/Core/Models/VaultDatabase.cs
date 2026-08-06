using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    public class VaultDatabase
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "2.0";

        [JsonPropertyName("encryptionType")]
        public string EncryptionType { get; set; } = "ZeroKnowledge-VaultFileZk";

        [JsonPropertyName("created")]
        public System.DateTime Created { get; set; }

        [JsonPropertyName("vaultName")]
        public string VaultName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Monotonic counter incremented on every successful vault save. Used together
        /// with a host-local trust anchor to detect whole-volume rollback at unlock.
        /// </summary>
        [JsonPropertyName("saveSequence")]
        public long SaveSequence { get; set; }

        [JsonPropertyName("groups")]
        public List<VaultGroup>? Groups { get; set; } = new();
    }

    public class VaultGroup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("entries")]
        public List<Credential>? Entries { get; set; } = new();
    }
}

