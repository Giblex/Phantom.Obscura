using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<VaultProtectionTier>))]
    public enum VaultProtectionTier
    {
        StandardSecure = 0,
        StealthSecure = 1,
        BlackSecure = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter<VaultStorageTransport>))]
    public enum VaultStorageTransport
    {
        FileSystem = 0,
        PackedVolume = 1,
        RawDevice = 2
    }
}
