using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Represents a user-defined category for organizing credentials.
    /// Stored inside the encrypted manifest so it benefits from the same
    /// confidentiality and integrity protections.
    /// </summary>
    public class CategoryModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string? Icon { get; set; } = null;

        [JsonPropertyName("order")]
        public int Order { get; set; } = 0;

        [JsonPropertyName("isTrash")]
        public bool IsTrash { get; set; } = false;

        /// <summary>
        /// Optional hex color (e.g., #AABBCC) used by the UI to tint the category tile.
        /// When null or empty, the UI uses its default background.
        /// </summary>
        [JsonPropertyName("tileColor")]
        public string? TileColor { get; set; } = null;
    }
}
