#nullable enable
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers the pure rollback-evaluation and vault-identity logic that backs the
/// host-local volume trust anchor.
/// </summary>
public sealed class VolumeRollbackEvaluatorTests
{
    [Fact]
    public void Evaluate_NoAnchor_IsFirstUse()
    {
        Assert.Equal(VolumeIntegrityVerdict.FirstUse, VolumeRollbackEvaluator.Evaluate(null, 5));
    }

    [Fact]
    public void Evaluate_CurrentBehindAnchor_IsRollback()
    {
        Assert.Equal(VolumeIntegrityVerdict.Rollback, VolumeRollbackEvaluator.Evaluate(10, 3));
    }

    [Fact]
    public void Evaluate_CurrentEqualsAnchor_IsOk()
    {
        Assert.Equal(VolumeIntegrityVerdict.Ok, VolumeRollbackEvaluator.Evaluate(7, 7));
    }

    [Fact]
    public void Evaluate_CurrentAheadOfAnchor_IsOk()
    {
        Assert.Equal(VolumeIntegrityVerdict.Ok, VolumeRollbackEvaluator.Evaluate(7, 12));
    }

    [Fact]
    public void ComputeVaultId_IsStableForSameInputs()
    {
        var a = VolumeRollbackEvaluator.ComputeVaultId("device-1", "salt-AAA");
        var b = VolumeRollbackEvaluator.ComputeVaultId("device-1", "salt-AAA");
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeVaultId_DiffersPerVault()
    {
        var a = VolumeRollbackEvaluator.ComputeVaultId("device-1", "salt-AAA");
        var b = VolumeRollbackEvaluator.ComputeVaultId("device-1", "salt-BBB");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeVaultId_NullWhenNoIdentityMaterial()
    {
        Assert.Null(VolumeRollbackEvaluator.ComputeVaultId(null, null));
        Assert.Null(VolumeRollbackEvaluator.ComputeVaultId("", "  "));
    }
}
