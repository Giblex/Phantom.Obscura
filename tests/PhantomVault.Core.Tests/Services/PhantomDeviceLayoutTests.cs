#nullable enable
using System.IO;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Verifies that every PhantomVault-owned device path is nested under the single
/// <c>.phantom</c> container folder so the drive root stays clean.
/// </summary>
public sealed class PhantomDeviceLayoutTests
{
    private const string DriveRoot = @"D:\";

    [Fact]
    public void GetPhantomRoot_NestsUnderDotPhantom()
    {
        Assert.Equal(Path.Combine(DriveRoot, ".phantom"), PhantomDeviceLayout.GetPhantomRoot(DriveRoot));
    }

    [Fact]
    public void GetSystemVolumePath_IsInsidePhantomFolder()
    {
        var expected = Path.Combine(DriveRoot, ".phantom", "system.bin");
        Assert.Equal(expected, PhantomDeviceLayout.GetSystemVolumePath(DriveRoot));
    }

    [Fact]
    public void GetDeviceIdPath_IsInsidePhantomFolder()
    {
        var expected = Path.Combine(DriveRoot, ".phantom", "device.id");
        Assert.Equal(expected, PhantomDeviceLayout.GetDeviceIdPath(DriveRoot));
    }

    [Fact]
    public void GetQuarantineDir_IsInsidePhantomFolder()
    {
        var expected = Path.Combine(DriveRoot, ".phantom", "quarantine");
        Assert.Equal(expected, PhantomDeviceLayout.GetQuarantineDir(DriveRoot));
    }

    [Fact]
    public void SystemVolumeRelativePath_IsNestedRelativePath()
    {
        Assert.Equal(Path.Combine(".phantom", "system.bin"), PhantomDeviceLayout.SystemVolumeRelativePath);
    }

    [Fact]
    public void EnsurePhantomRoot_CreatesFolder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "phantom-layout-test-" + Path.GetRandomFileName());
        try
        {
            var root = PhantomDeviceLayout.EnsurePhantomRoot(temp);
            Assert.True(Directory.Exists(root));
            Assert.Equal(Path.Combine(temp, ".phantom"), root);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }
}
