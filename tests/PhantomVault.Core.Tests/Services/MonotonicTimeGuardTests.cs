#nullable enable
using System;
using System.IO;
using PhantomVault.Core.Services.Licensing;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers clock-rollback detection. A local time-based license must not be revivable by
/// winding the system clock backwards.
/// </summary>
public sealed class MonotonicTimeGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clockguard_" + Guid.NewGuid().ToString("N"));

    public MonotonicTimeGuardTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private MonotonicTimeGuard NewGuard(TimeSpan? tol = null)
        => new(Path.Combine(_dir, "clock.watermark"), tol ?? TimeSpan.FromHours(1));

    [Fact]
    public void FirstObservation_IsNeverRollback()
    {
        var guard = NewGuard();
        Assert.False(guard.IsRollback(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MovingForward_IsNotRollback()
    {
        var guard = NewGuard();
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.False(guard.IsRollback(t0));
        Assert.False(guard.IsRollback(t0.AddDays(1)));
        Assert.False(guard.IsRollback(t0.AddDays(30)));
    }

    [Fact]
    public void WindingClockBack_IsDetectedAsRollback()
    {
        var guard = NewGuard();
        var t0 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.False(guard.IsRollback(t0));

        // Attacker sets the date back a month to revive an expired license.
        Assert.True(guard.IsRollback(t0.AddDays(-30)));
    }

    [Fact]
    public void SmallBackwardDrift_WithinTolerance_IsNotRollback()
    {
        var guard = NewGuard(TimeSpan.FromHours(1));
        var t0 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.False(guard.IsRollback(t0));

        // A 5-minute NTP correction must not be treated as an attack.
        Assert.False(guard.IsRollback(t0.AddMinutes(-5)));
    }

    [Fact]
    public void Watermark_PersistsAcrossInstances()
    {
        var t0 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        NewGuard().IsRollback(t0.AddDays(10)); // establish a forward watermark

        // A brand-new instance (e.g. app restart) still detects the rollback.
        var fresh = NewGuard();
        Assert.True(fresh.IsRollback(t0));
    }
}
