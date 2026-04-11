using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Persisted USB binding metadata authored during setup for the mandatory
    /// three-container USB layout.
    /// </summary>
    public sealed class UsbBindingRecord
    {
        [JsonPropertyName("bindingId")]
        public string BindingId { get; set; } = string.Empty;

        [JsonPropertyName("bindingGuid")]
        public string BindingGuid { get; set; } = string.Empty;

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("guuid")]
        public string? Guuid { get; set; }

        [JsonPropertyName("driveRoot")]
        public string DriveRoot { get; set; } = string.Empty;

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("rootContainerPath")]
        public string RootContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("vaultContainerPath")]
        public string VaultContainerPath { get; set; } = string.Empty;

        [JsonPropertyName("objectContainerPath")]
        public string ObjectContainerPath { get; set; } = string.Empty;
    }
}
