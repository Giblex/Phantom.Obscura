using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Mobile stub for BlackSecureRawVolumeService.
    /// Raw device transport (writing directly to USB block devices bypassing the file system)
    /// is not available on Android without root access. This stub surfaces all operations
    /// as not-supported so that feature-availability checks degrade gracefully.
    /// </summary>
    public sealed class BlackSecureRawVolumeService
    {
        public const string RawSelectionPrefix = "RAWUSB:";

        private const string NotSupportedMessage =
            "Black Secure raw volume transport is not available on Android. " +
            "Standard file-based vault storage is used instead.";

        public IReadOnlyList<string> GetSelectableRawDevices()
            => Array.Empty<string>();

        public bool IsRawSelection(string? selection)
            => selection?.StartsWith(RawSelectionPrefix, StringComparison.Ordinal) == true;

        public string? TryResolvePhysicalDevicePathFromSelection(string? selection)
            => null;

        public bool TryResolvePhysicalDevicePathFromDriveRoot(string driveRoot, out string? physicalDevicePath)
        {
            physicalDevicePath = null;
            return false;
        }

        public Task<bool> IsBlackSecureVolumeAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task CreateVolumeFromDirectoryAsync(string physicalDevicePath, string sourceRoot, CancellationToken cancellationToken = default)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public Task InvalidateVolumeHeaderAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public Task<string> ExtractVolumeAsync(string physicalDevicePath, string destinationRoot, CancellationToken cancellationToken = default)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public Task<BlackSecureRawManifest> ReadManifestAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
            => throw new PlatformNotSupportedException(NotSupportedMessage);
    }

    /// <summary>
    /// Black Secure raw volume manifest (mobile stub type).
    /// </summary>
    public sealed class BlackSecureRawManifest
    {
        public int Version { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string PayloadHash { get; set; } = string.Empty;
        public List<BlackSecureRawEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Entry in a Black Secure raw volume manifest.
    /// </summary>
    public sealed class BlackSecureRawEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Length { get; set; }
    }
}
