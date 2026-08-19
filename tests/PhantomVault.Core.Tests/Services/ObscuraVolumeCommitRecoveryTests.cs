#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers the crash-safe commit journal: an interrupted repack must never leave the live
/// container corrupt, and recovery on the next open must clean up or restore as needed.
/// </summary>
public sealed class ObscuraVolumeCommitRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obscura_recovery_" + Guid.NewGuid().ToString("N"));
    private readonly ObscuraVolumeService _svc = new();

    /// <summary>
    /// The volume header is encrypted under this, so every create/extract needs it. The vault
    /// password is deliberately not involved — see ObscuraVolumeFormat.DeriveHeaderKey.
    /// </summary>
    private readonly string _keyfile;

    public ObscuraVolumeCommitRecoveryTests()
    {
        Directory.CreateDirectory(_dir);
        _keyfile = Path.Combine(_dir, "vault.key");
        File.WriteAllBytes(_keyfile, System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ObscuraVolumeSource Src(string path, string content)
        => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));

    private static void WriteLegacyVolume(string path, params (string Path, byte[] Content)[] files)
    {
        long offset = 0;
        using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new System.Collections.Generic.List<ObscuraVolumeEntry>();
        foreach (var file in files)
        {
            byte[] hash = SHA256.HashData(file.Content);
            entries.Add(new ObscuraVolumeEntry
            {
                Path = file.Path,
                Offset = offset,
                Length = file.Content.Length,
                Sha256 = Convert.ToBase64String(hash)
            });
            offset += file.Content.Length;
            payloadHasher.AppendData(hash);
        }

        var manifest = new ObscuraVolumeManifest
        {
            Version = 1,
            CreatedUtc = DateTimeOffset.UtcNow,
            Entries = entries,
            PayloadHash = Convert.ToBase64String(payloadHasher.GetHashAndReset())
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest);
        using var output = File.Create(path);
        output.Write(Encoding.ASCII.GetBytes("OBSCUR01"));
        output.Write(BitConverter.GetBytes(json.Length));
        output.Write(json);
        foreach (var file in files) output.Write(file.Content);
    }

    [Fact]
    public async Task SuccessfulCommit_LeavesNoJournalTempOrBackupArtifacts()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("a.txt", "hello") }, _keyfile);

        Assert.True(File.Exists(vol));
        Assert.False(File.Exists(vol + ".tmp"));
        Assert.False(File.Exists(vol + ".bak"));
        Assert.False(File.Exists(vol + ".commit-journal"));
    }

    [Fact]
    public void Recover_SweepsStaleTemp_WithNoJournal()
    {
        string vol = Path.Combine(_dir, "system.bin");
        File.WriteAllBytes(vol, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(vol + ".tmp", new byte[] { 9 });

        var result = ObscuraVolumeService.RecoverPendingCommit(vol);

        Assert.False(result.RecoveryPerformed);
        Assert.True(result.StaleTempRemoved);
        Assert.False(File.Exists(vol + ".tmp"));
    }

    [Fact]
    public async Task Recover_RestoresFromBackup_WhenLiveVolumeCorruptAndJournalPresent()
    {
        string vol = Path.Combine(_dir, "system.bin");

        // Produce a real, valid volume to use as the backup copy.
        string good = Path.Combine(_dir, "good.bin");
        await _svc.CreateVolumeFromSourcesAsync(good, new[] { Src("keep.txt", "important-data") }, _keyfile);
        File.Copy(good, vol + ".bak");

        // Simulate a crash mid-File.Replace: live file is garbage, journal still present.
        File.WriteAllBytes(vol, new byte[] { 0, 0, 0, 0 });
        File.WriteAllText(vol + ".commit-journal", "{\"Version\":1}");

        var result = ObscuraVolumeService.RecoverPendingCommit(vol);

        Assert.True(result.RecoveryPerformed);
        Assert.True(result.BackupRestored);
        Assert.False(File.Exists(vol + ".commit-journal"));
        Assert.False(File.Exists(vol + ".bak"));

        // Restored volume must be readable and contain the backed-up data.
        string outDir = Path.Combine(_dir, "out");
        await _svc.ExtractVolumeAsync(vol, outDir, _keyfile, progress: null, verify: true);
        Assert.Equal("important-data", File.ReadAllText(Path.Combine(outDir, "keep.txt")));
    }

    [Fact]
    public async Task Recover_KeepsLiveVolume_WhenValidAndJournalPresent()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("live.txt", "current") }, _keyfile);

        // Stale backup + journal from an interrupted commit that actually completed the swap.
        File.Copy(vol, vol + ".bak");
        File.WriteAllText(vol + ".commit-journal", "{\"Version\":1}");

        var result = ObscuraVolumeService.RecoverPendingCommit(vol);

        Assert.True(result.RecoveryPerformed);
        Assert.False(result.BackupRestored);
        Assert.False(File.Exists(vol + ".commit-journal"));
        Assert.False(File.Exists(vol + ".bak"));

        string outDir = Path.Combine(_dir, "out");
        await _svc.ExtractVolumeAsync(vol, outDir, _keyfile, progress: null, verify: true);
        Assert.Equal("current", File.ReadAllText(Path.Combine(outDir, "live.txt")));
    }

    [Fact]
    public async Task Volume_round_trips_through_the_encrypted_header()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol,
            new[] { Src("root/a.txt", "alpha"), Src("decoy/b.txt", "beta") }, _keyfile);

        string outDir = Path.Combine(_dir, "rt");
        await _svc.ExtractVolumeAsync(vol, outDir, _keyfile, progress: null, verify: true);

        Assert.Equal("alpha", File.ReadAllText(Path.Combine(outDir, "root", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(outDir, "decoy", "b.txt")));
    }

    [Fact]
    public async Task The_header_no_longer_exposes_entry_paths_in_plaintext()
    {
        // The whole point of the change: "decoy" must not be legible to anyone who opens
        // system.bin in a text editor, because naming the decoy destroys its deniability.
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol,
            new[] { Src("decoy/decoy.database.pmeta", "x"), Src("root/root.pvault", "y") }, _keyfile);

        byte[] raw = File.ReadAllBytes(vol);
        string asText = Encoding.ASCII.GetString(raw);

        Assert.DoesNotContain("decoy", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pvault", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayloadHash", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("OBSCUR01", asText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_keyfile_cannot_open_the_volume()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("a.txt", "secret") }, _keyfile);

        string wrong = Path.Combine(_dir, "wrong.key");
        File.WriteAllBytes(wrong, System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(
            () => _svc.ReadManifestAsync(vol, wrong));
    }

    [Fact]
    public async Task ResolveKeyfile_picks_the_one_that_works_and_reports_failure_otherwise()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("a.txt", "secret") }, _keyfile);

        string wrong = Path.Combine(_dir, "wrong.key");
        File.WriteAllBytes(wrong, System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        Assert.Equal(_keyfile, await _svc.ResolveKeyfileAsync(vol, new[] { wrong, _keyfile }));
        Assert.Null(await _svc.ResolveKeyfileAsync(vol, new[] { wrong }));
    }

    [Fact]
    public async Task Volume_size_is_padded_to_a_bucket()
    {
        // A tiny vault and a larger one must not be tellable apart by file size.
        string small = Path.Combine(_dir, "small.bin");
        string larger = Path.Combine(_dir, "larger.bin");
        await _svc.CreateVolumeFromSourcesAsync(small, new[] { Src("a.txt", "x") }, _keyfile);
        await _svc.CreateVolumeFromSourcesAsync(larger, new[] { Src("a.txt", new string('y', 200_000)) }, _keyfile);

        Assert.Equal(new FileInfo(small).Length, new FileInfo(larger).Length);
        Assert.Equal(0, new FileInfo(small).Length % (64L * 1024 * 1024));
    }

    [Fact]
    public async Task Legacy_upgrade_rewrites_only_the_header_and_round_trips_payload()
    {
        string vol = Path.Combine(_dir, "legacy.bin");
        WriteLegacyVolume(vol,
            ("root/a.txt", Encoding.UTF8.GetBytes("alpha")),
            ("decoy/decoy.database.pmeta", Encoding.UTF8.GetBytes("beta")));

        Assert.True(await _svc.IsLegacyVolumeAsync(vol));
        Assert.True(await _svc.UpgradeLegacyVolumeAsync(vol, _keyfile));
        Assert.False(await _svc.IsLegacyVolumeAsync(vol));
        Assert.False(await _svc.UpgradeLegacyVolumeAsync(vol, _keyfile));

        string rawText = Encoding.ASCII.GetString(File.ReadAllBytes(vol));
        Assert.DoesNotContain("OBSCUR01", rawText, StringComparison.Ordinal);
        Assert.DoesNotContain("decoy", rawText, StringComparison.OrdinalIgnoreCase);

        string extracted = Path.Combine(_dir, "upgraded");
        await _svc.ExtractVolumeAsync(vol, extracted, _keyfile, progress: null, verify: true);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(extracted, "root", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(extracted, "decoy", "decoy.database.pmeta")));
        Assert.False(File.Exists(vol + ".tmp"));
        Assert.False(File.Exists(vol + ".bak"));
        Assert.False(File.Exists(vol + ".commit-journal"));
    }

    [Fact]
    public async Task Legacy_upgrade_refuses_corrupt_payload_and_preserves_original()
    {
        string vol = Path.Combine(_dir, "corrupt-legacy.bin");
        WriteLegacyVolume(vol, ("root/a.txt", Encoding.UTF8.GetBytes("original")));
        byte[] before = File.ReadAllBytes(vol);
        before[^1] ^= 0x7f;
        File.WriteAllBytes(vol, before);

        await Assert.ThrowsAsync<CryptographicException>(
            () => _svc.UpgradeLegacyVolumeAsync(vol, _keyfile));

        Assert.True(await _svc.IsLegacyVolumeAsync(vol));
        Assert.Equal(before, File.ReadAllBytes(vol));
        Assert.False(File.Exists(vol + ".commit-journal"));
    }
}
