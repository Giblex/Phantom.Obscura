using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.DomainStores
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CryptoDomain
    {

        Obscura,

        Attestor,

        Recovery
    }
}

