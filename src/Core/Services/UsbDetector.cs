using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Desktop implementation of USB drive detection for Windows, macOS, and Linux.
    /// This service abstracts platform differences and exposes a simple enumeration API.
    /// For Linux and macOS it scans common mount points; for Windows it
    /// inspects <see cref="DriveInfo"/> entries. Consumers may subscribe to
    /// the <see cref="RemovableDriveInserted"/> and <see cref="RemovableDriveRemoved"/>
    /// events to be notified of changes.
    /// </summary>
    public sealed class UsbDetector : IUsbDetector
    {
        private readonly FileSystemWatcher? _linuxWatcher;
        private readonly FileSystemWatcher? _mediaWatcher;
        private readonly HashSet<string> _currentDevices = new(StringComparer.OrdinalIgnoreCase);

        public UsbDetector()
        {
            // On Unix-like systems, monitor common mount roots for newly
            // attached drives. These watchers may not fire on all systems,
            // but they provide a best effort without requiring polling.
            if (!OperatingSystem.IsWindows())
            {
                // '/media' is used by most desktop distributions
                if (Directory.Exists("/media"))
                {
                    _linuxWatcher = new FileSystemWatcher("/media")
                    {
                        NotifyFilter = NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false
                    };
                    _linuxWatcher.Created += (_, e) => OnDriveInserted(e.FullPath);
                    _linuxWatcher.Deleted += (_, e) => OnDriveRemoved(e.FullPath);
                    _linuxWatcher.EnableRaisingEvents = true;
                }
                // '/Volumes' is used by macOS for removable devices
                if (Directory.Exists("/Volumes"))
                {
                    _mediaWatcher = new FileSystemWatcher("/Volumes")
                    {
                        NotifyFilter = NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false
                    };
                    _mediaWatcher.Created += (_, e) => OnDriveInserted(e.FullPath);
                    _mediaWatcher.Deleted += (_, e) => OnDriveRemoved(e.FullPath);
                    _mediaWatcher.EnableRaisingEvents = true;
                }
            }
        }

        /// <summary>
        /// Raised when a new removable drive is detected. The argument
        /// contains the root path of the drive (e.g. "E:\\" on Windows or
        /// "/media/username/USB" on Linux).
        /// </summary>
        public event Action<string>? RemovableDriveInserted;

        /// <summary>
        /// Raised when a removable drive is removed.
        /// </summary>
        public event Action<string>? RemovableDriveRemoved;

        /// <summary>
        /// Enumerates all currently connected removable drives. This method
        /// returns a snapshot and does not monitor for changes. Use the
        /// events for real‑time updates.
        /// </summary>
        public IEnumerable<string> GetRemovableDrives()
        {
            if (OperatingSystem.IsWindows())
            {
                return DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable)
                    .Select(d => d.RootDirectory.FullName);
            }
            else
            {
                var candidates = new List<string>();
                foreach (var root in new[] { "/media", "/Volumes", "/run/media" })
                {
                    if (Directory.Exists(root))
                    {
                        candidates.AddRange(Directory.GetDirectories(root));
                    }
                }
                return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void OnDriveInserted(string path)
        {
            lock (_currentDevices)
            {
                if (_currentDevices.Add(path))
                {
                    RemovableDriveInserted?.Invoke(path);
                }
            }
        }

        private void OnDriveRemoved(string path)
        {
            lock (_currentDevices)
            {
                if (_currentDevices.Remove(path))
                {
                    RemovableDriveRemoved?.Invoke(path);
                }
            }
        }

        /// <summary>
        /// Checks if a given path represents a valid, currently-mounted removable drive.
        /// </summary>
        public bool IsValidRemovableDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var drive = new DriveInfo(path);
                    return drive.IsReady && drive.DriveType == DriveType.Removable;
                }
                else
                {
                    // On Unix, check if the path exists and is in a known mount location
                    if (!Directory.Exists(path))
                        return false;

                    return path.StartsWith("/media/", StringComparison.Ordinal) ||
                           path.StartsWith("/Volumes/", StringComparison.Ordinal) ||
                           path.StartsWith("/run/media/", StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets detailed information about a removable drive.
        /// Returns null if the drive is not accessible.
        /// </summary>
        public RemovableDriveInfo? GetDriveInfo(string path)
        {
            if (!IsValidRemovableDrive(path))
                return null;

            try
            {
                var driveInfo = new DriveInfo(path);
                if (!driveInfo.IsReady)
                    return null;

                return new RemovableDriveInfo
                {
                    Label = string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                        ? "Removable Drive"
                        : driveInfo.VolumeLabel,
                    TotalSizeBytes = driveInfo.TotalSize,
                    AvailableFreeSpaceBytes = driveInfo.AvailableFreeSpace,
                    FileSystem = driveInfo.DriveFormat,
                    DeviceIdentifier = ComputeDeviceIdentifier(path),
                    RootPath = path
                };
            }
            catch
            {
                return null;
            }
        }

        private string ComputeDeviceIdentifier(string path)
        {
            // Use UsbBindingService logic for consistency
            if (OperatingSystem.IsWindows())
            {
                // Try to get volume serial number
                var drive = new DriveInfo(path);
                // Note: DriveInfo doesn't expose serial directly, so we use a hash
                return $"{drive.VolumeLabel}_{drive.TotalSize}";
            }
            else
            {
                // Unix: use path-based identifier
                return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        public void Dispose()
        {
            _linuxWatcher?.Dispose();
            _mediaWatcher?.Dispose();
        }
    }
}