using System.Text.Json.Serialization;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Models;

namespace GiblexVault.Security.ZK
{

    internal sealed record HeaderDocDto(string Type, string Version, CipherSuite Suite, KdfParams Kdf, byte[] WrappedDek, string? Note);

    internal sealed record AadPreview(string t, string v, string s);
}

