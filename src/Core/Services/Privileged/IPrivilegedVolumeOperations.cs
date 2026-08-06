using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Privileged
{
    /// <summary>
    /// The narrow set of operations that genuinely require administrator rights
    /// (diskpart / MSFT_Partition attribute changes and raw <c>\\.\PhysicalDrive</c>
    /// I/O). When the desktop app runs non-elevated, these calls are forwarded to
    /// the elevated <c>PhantomVault.PrivilegedBroker</c> Windows service over a
    /// hardened named pipe, so the UI never needs a per-launch UAC prompt.
    ///
    /// Implemented by the UI-side named-pipe client only. The broker process and
    /// any already-elevated process execute the underlying Core services directly.
    /// </summary>
    public interface IPrivilegedVolumeOperations
    {
        /// <summary>Applies USB write-protection (read-only / hidden / GPT type). The
        /// supplied <paramref name="state"/> is updated in place with the results
        /// (sentinel list + last-asserted timestamp).</summary>
        bool ApplyProtection(string driveRoot, UsbWriteProtectionState state);

        /// <summary>Clears read-only protection so the device can be written.</summary>
        bool EnableWriteAccess(string driveRoot);

        /// <summary>Re-asserts read-only protection on the device.</summary>
        bool DisableWriteAccess(string driveRoot);

        /// <summary>Writes a Black Secure raw volume from a staged directory tree.</summary>
        Task CreateVolumeFromDirectoryAsync(string physicalDevicePath, string sourceRoot, CancellationToken cancellationToken = default);

        /// <summary>Zeroes the Black Secure volume header (logical destroy).</summary>
        Task InvalidateVolumeHeaderAsync(string physicalDevicePath, CancellationToken cancellationToken = default);

        /// <summary>Extracts a Black Secure raw volume to a destination directory.</summary>
        Task<string> ExtractVolumeAsync(string physicalDevicePath, string destinationRoot, bool verify, IProgress<double>? progress, CancellationToken cancellationToken = default);

        /// <summary>True if the device begins with the Black Secure raw magic.</summary>
        Task<bool> IsBlackSecureVolumeAsync(string physicalDevicePath, CancellationToken cancellationToken = default);

        // ─────────────────────────────────────────────────────────────────────
        // Suite-shared Phantom Volume (VHDX) operations
        // These serve PhantomKey, Obscura, and Attestor. The broker is the
        // single elevated authority so the user never sees per-launch UAC.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Create + NTFS-format a hidden VHDX at <paramref name="containerPath"/> if it
        /// does not exist. Idempotent — returns true if the file already existed or was
        /// just provisioned. Sets Hidden+System attributes and volume label PHANTOMKEY.
        /// </summary>
        bool ProvisionPhantomVolume(string containerPath, long sizeBytes);

        /// <summary>
        /// Attach the VHDX at <paramref name="containerPath"/> as a virtual disk with
        /// an auto-assigned drive letter. Returns the mount root (e.g. "M:\") or empty
        /// on failure. Safe to call while other Phantom Suite VHDXs are mounted.
        /// </summary>
        string MountPhantomVolume(string containerPath);

        /// <summary>Detach the VHDX at <paramref name="containerPath"/>. Idempotent.</summary>
        bool UnmountPhantomVolume(string containerPath);
    }
}
