using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.Core.Services
{

    public sealed class UsbDetector : IUsbDetector
    {
        private readonly FileSystemWatcher? _linuxWatcher;
        private readonly FileSystemWatcher? _mediaWatcher;
        private readonly HashSet<string> _currentDevices = new(StringComparer.OrdinalIgnoreCase);

        public UsbDetector()
        {

            if (!OperatingSystem.IsWindows())
            {

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

        public event Action<string>? RemovableDriveInserted;

        public event Action<string>? RemovableDriveRemoved;

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

            if (OperatingSystem.IsWindows())
            {

                var drive = new DriveInfo(path);

                return $"{drive.VolumeLabel}_{drive.TotalSize}";
            }
            else
            {

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

