#nullable enable
using System;
using System.IO;
using System.Text;
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

    public ObscuraVolumeCommitRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ObscuraVolumeSource Src(string path, string content)
        => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));

    [Fact]
    public async Task SuccessfulCommit_LeavesNoJournalTempOrBackupArtifacts()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("a.txt", "hello") });

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
        await _svc.CreateVolumeFromSourcesAsync(good, new[] { Src("keep.txt", "important-data") });
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
        await _svc.ExtractVolumeAsync(vol, outDir, progress: null, verify: true);
        Assert.Equal("important-data", File.ReadAllText(Path.Combine(outDir, "keep.txt")));
    }

    [Fact]
    public async Task Recover_KeepsLiveVolume_WhenValidAndJournalPresent()
    {
        string vol = Path.Combine(_dir, "system.bin");
        await _svc.CreateVolumeFromSourcesAsync(vol, new[] { Src("live.txt", "current") });

        // Stale backup + journal from an interrupted commit that actually completed the swap.
        File.Copy(vol, vol + ".bak");
        File.WriteAllText(vol + ".commit-journal", "{\"Version\":1}");

        var result = ObscuraVolumeService.RecoverPendingCommit(vol);

        Assert.True(result.RecoveryPerformed);
        Assert.False(result.BackupRestored);
        Assert.False(File.Exists(vol + ".commit-journal"));
        Assert.False(File.Exists(vol + ".bak"));

        string outDir = Path.Combine(_dir, "out");
        await _svc.ExtractVolumeAsync(vol, outDir, progress: null, verify: true);
        Assert.Equal("current", File.ReadAllText(Path.Combine(outDir, "live.txt")));
    }
}
