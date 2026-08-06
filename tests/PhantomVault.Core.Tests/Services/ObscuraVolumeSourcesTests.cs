#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers the streamed repack path used by the writable virtual-drive mount, which
/// rebuilds an OBSCUR01 container from in-memory sources without staging plaintext.
/// </summary>
public sealed class ObscuraVolumeSourcesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obscura_src_tests_" + Guid.NewGuid().ToString("N"));
    private readonly ObscuraVolumeService _svc = new();

    public ObscuraVolumeSourcesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ObscuraVolumeSource Src(string path, string content)
        => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));

    [Fact]
    public async Task CreateFromSources_RoundTrips_EntriesAndContent()
    {
        string vol = Path.Combine(_dir, "system.bin");
        var sources = new[]
        {
            Src("alpha.txt", "alpha-content"),
            Src("dir/beta.txt", "beta-content-longer"),
        };

        await _svc.CreateVolumeFromSourcesAsync(vol, sources);

        Assert.True(await _svc.IsObscuraVolumeAsync(vol));
        string outDir = Path.Combine(_dir, "out");
        await _svc.ExtractVolumeAsync(vol, outDir, progress: null, verify: true);

        Assert.Equal("alpha-content", File.ReadAllText(Path.Combine(outDir, "alpha.txt")));
        Assert.Equal("beta-content-longer", File.ReadAllText(Path.Combine(outDir, "dir", "beta.txt")));
    }

    [Fact]
    public async Task CreateFromSources_OrdersEntries_DeterministicallyByPath()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[]
        {
            Src("zeta.txt", "z"),
            Src("alpha.txt", "a"),
            Src("mid/file.txt", "m"),
        });

        var manifest = await _svc.ReadManifestAsync(vol);
        var paths = manifest.Entries.Select(e => e.Path).ToArray();
        var sorted = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(sorted, paths);
    }

    [Fact]
    public async Task CreateFromSources_VerifyExtract_PassesIntegrity()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[]
        {
            Src("one.bin", new string('x', 5000)),
            Src("two.bin", new string('y', 1)),
        });

        string outDir = Path.Combine(_dir, "out");
        await _svc.ExtractVolumeAsync(vol, outDir, progress: null, verify: true);
        Assert.True(await _svc.VerifyExtractedVolumeAsync(vol, outDir));
    }

    [Fact]
    public async Task CreateFromSources_Empty_ProducesValidEmptyVolume()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, Array.Empty<ObscuraVolumeSource>());

        Assert.True(await _svc.IsObscuraVolumeAsync(vol));
        var manifest = await _svc.ReadManifestAsync(vol);
        Assert.Empty(manifest.Entries);
    }
}
