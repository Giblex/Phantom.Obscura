#nullable enable
using System;
using System.IO;
using System.Linq;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers the read/verify path used by the in-app Security Activity viewer. The audit log is
/// hash-chained, so tampering (editing or reordering lines) must be detected, and a clean log
/// must round-trip every entry.
/// </summary>
public sealed class AuditServiceReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "audit_" + Guid.NewGuid().ToString("N"));
    private readonly string _log;

    public AuditServiceReadTests()
    {
        Directory.CreateDirectory(_dir);
        _log = Path.Combine(_dir, "vault.audit");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ReadEntries_MissingFile_ReturnsEmptyValid()
    {
        var svc = new AuditService();
        var result = svc.ReadEntries(Path.Combine(_dir, "does-not-exist.audit"));
        Assert.Empty(result.Entries);
        Assert.True(result.ChainValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ReadEntries_RoundTripsAllEntriesInOrder()
    {
        var svc = new AuditService();
        svc.LogEvent(_log, "unlock", "first");
        svc.LogEvent(_log, "mount", "second");
        svc.LogEvent(_log, "lock", "third");

        var result = svc.ReadEntries(_log);

        Assert.True(result.ChainValid);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(new[] { "unlock", "mount", "lock" }, result.Entries.Select(e => e.Category));
        Assert.Equal(new[] { "first", "second", "third" }, result.Entries.Select(e => e.Message));
    }

    [Fact]
    public void ReadEntries_DetectsTampering()
    {
        var svc = new AuditService();
        svc.LogEvent(_log, "unlock", "legit");
        svc.LogEvent(_log, "mount", "legit");

        // Tamper: rewrite the message in the first line, breaking the hash chain.
        var lines = File.ReadAllLines(_log);
        lines[0] = lines[0].Replace("legit", "evil");
        File.WriteAllLines(_log, lines);

        var result = svc.ReadEntries(_log);

        Assert.False(result.ChainValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ReadEntries_DetectsReordering()
    {
        var svc = new AuditService();
        svc.LogEvent(_log, "unlock", "a");
        svc.LogEvent(_log, "mount", "b");

        var lines = File.ReadAllLines(_log);
        File.WriteAllLines(_log, new[] { lines[1], lines[0] });

        var result = svc.ReadEntries(_log);
        Assert.False(result.ChainValid);
    }
}
