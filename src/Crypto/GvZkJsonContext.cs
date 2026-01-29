using System.Text.Json.Serialization;

namespace GiblexVault.Security.ZK
{
    [JsonSerializable(typeof(HeaderDocDto))]
    [JsonSerializable(typeof(AadPreview))]
    internal partial class GvZkJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
