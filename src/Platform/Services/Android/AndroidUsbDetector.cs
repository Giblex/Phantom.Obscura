using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.Platform.Android
{
    /// <summary>
    /// Android-compatible USB/removable-storage detector.
    /// On Android, external storage paths are provided by the system via
    /// android.os.Environment.getExternalStorageDirectory() and the StorageManager.
    /// This implementation provides a platform-neutral fallback that works in the
    /// .NET Android context without direct Android API calls in the Core/Platform layer.
    /// The MAUI app layer injects actual Android storage paths via IAndroidStorageProvider.
    /// </summary>
    public sealed class AndroidUsbDetector : IUsbDetector
    {
        private readonly List<string> _mountedPaths = new();

        public event Action<string>? RemovableDriveInserted;
        public event Action<string>? RemovableDriveRemoved;

        /// <summary>
        /// Registers an external storage path discovered by the Android platform layer.
        /// Called from MAUI app code when USB OTG or SD card mounts are detected.
        /// </summary>
        public void NotifyDriveInserted(string path)
        {
            if (!_mountedPaths.Contains(path))
            {
                _mountedPaths.Add(path);
                RemovableDriveInserted?.Invoke(path);
            }
        }

        /// <summary>
        /// Notifies of drive removal.
        /// </summary>
        public void NotifyDriveRemoved(string path)
        {
            _mountedPaths.Remove(path);
            RemovableDriveRemoved?.Invoke(path);
        }

        public IEnumerable<string> GetRemovableDrives()
            => _mountedPaths.AsReadOnly();

        public bool IsValidRemovableDrive(string path)
            => _mountedPaths.Contains(path) && System.IO.Directory.Exists(path);

        public RemovableDriveInfo? GetDriveInfo(string path)
        {
            if (!System.IO.Directory.Exists(path))
                return null;
            try
            {
                var driveInfo = new System.IO.DriveInfo(path);
                return new RemovableDriveInfo
                {
                    Label = driveInfo.VolumeLabel,
                    TotalSizeBytes = driveInfo.TotalSize,
                    AvailableFreeSpaceBytes = driveInfo.AvailableFreeSpace,
                    RootPath = path
                };
            }
            catch
            {
                return new RemovableDriveInfo { Label = "External Storage" };
            }
        }

        public void Dispose() { }
    }
}
