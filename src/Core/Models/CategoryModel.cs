using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{

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

        [JsonPropertyName("tileColor")]
        public string? TileColor { get; set; } = null;
    }
}

