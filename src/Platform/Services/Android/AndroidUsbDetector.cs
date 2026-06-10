using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.Platform.Android
{

    public sealed class AndroidUsbDetector : IUsbDetector
    {
        private readonly List<string> _mountedPaths = new();

        public event Action<string>? RemovableDriveInserted;
        public event Action<string>? RemovableDriveRemoved;

        public void NotifyDriveInserted(string path)
        {
            if (!_mountedPaths.Contains(path))
            {
                _mountedPaths.Add(path);
                RemovableDriveInserted?.Invoke(path);
            }
        }

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

