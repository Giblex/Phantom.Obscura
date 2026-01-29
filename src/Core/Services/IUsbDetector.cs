using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Cross-platform interface for detecting removable USB drives.
    /// Implementations provide platform-specific detection logic for desktop (Windows, macOS, Linux)
    /// and mobile (Android USB OTG, iOS USB-C/Lightning) platforms.
    /// </summary>
    public interface IUsbDetector : IDisposable
    {
        /// <summary>
        /// Raised when a new removable drive is detected. The argument
        /// contains the root path or identifier of the drive.
        ///
        /// Desktop: "E:\\" (Windows), "/media/username/USB" (Linux), "/Volumes/USB" (macOS)
        /// Android: Content URI or volume path from StorageManager
        /// iOS: File provider URL from UIDocumentPickerViewController
        /// </summary>
        event Action<string>? RemovableDriveInserted;

        /// <summary>
        /// Raised when a removable drive is removed or unmounted.
        /// </summary>
        event Action<string>? RemovableDriveRemoved;

        /// <summary>
        /// Enumerates all currently connected removable drives. This method
        /// returns a snapshot and does not monitor for changes. Use the
        /// events for real-time updates.
        ///
        /// Returns:
        /// - Desktop: File system paths (e.g., "E:\\", "/media/usb")
        /// - Android: Volume paths or content URIs
        /// - iOS: File provider URLs or bookmark paths
        /// </summary>
        IEnumerable<string> GetRemovableDrives();

        /// <summary>
        /// Checks if a given path represents a valid removable drive on this platform.
        /// Useful for validating user input or cached paths before attempting access.
        /// </summary>
        /// <param name="path">The path or identifier to validate</param>
        /// <returns>True if the path is a valid, currently-mounted removable drive</returns>
        bool IsValidRemovableDrive(string path);

        /// <summary>
        /// Gets user-friendly display information for a removable drive.
        /// Returns null if the drive is not accessible.
        /// </summary>
        /// <param name="path">The drive path or identifier</param>
        /// <returns>Drive information (label, size, free space) or null if unavailable</returns>
        RemovableDriveInfo? GetDriveInfo(string path);
    }
}
