using System;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Validated, parsed manifest. Only constructed by <see cref="IUpdateVerifier"/>
    /// after a successful Ed25519 signature check, so consumers can trust every
    /// field without re-validating.
    /// </summary>
    public sealed record UpdateInfo
    {
        public required int Schema { get; init; }
        public required UpdateChannel Channel { get; init; }
        public required Version Version { get; init; }
        public required DateTimeOffset ReleasedAtUtc { get; init; }
        public Version? MinPreviousVersion { get; init; }
        public required Uri AssetUrl { get; init; }
        public required long AssetSize { get; init; }
        public required byte[] AssetSha256 { get; init; }
        public string? AuthenticodeSubject { get; init; }
        public byte[]? AuthenticodeThumbprintSha256 { get; init; }
    }
}
