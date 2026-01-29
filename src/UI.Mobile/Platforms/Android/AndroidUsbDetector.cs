using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS.Storage;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Mobile.Platforms.Android
{
    /// <summary>
    /// Android implementation of USB detection using USB OTG (On-The-Go).
    /// Detects external USB drives connected via USB-C or micro-USB adapters.
    /// Requires READ_EXTERNAL_STORAGE and WRITE_EXTERNAL_STORAGE permissions.
    /// </summary>
    public sealed class AndroidUsbDetector : IUsbDetector
    {
        private readonly Context _context;
        private readonly UsbReceiver _usbReceiver;
        private readonly StorageManager? _storageManager;
        private bool _isDisposed;

        public AndroidUsbDetector(Context context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _usbReceiver = new UsbReceiver(this);
            _storageManager = (StorageManager?)_context.GetSystemService(Context.StorageService);

            // Register broadcast receiver for USB device attach/detach
            var filter = new IntentFilter();
            filter.AddAction(UsbManager.ActionUsbDeviceAttached);
            filter.AddAction(UsbManager.ActionUsbDeviceDetached);
            _context.RegisterReceiver(_usbReceiver, filter);
        }

        public event Action<string>? RemovableDriveInserted;
        public event Action<string>? RemovableDriveRemoved;

        /// <summary>
        /// Enumerates all currently mounted removable USB drives.
        /// Uses StorageManager reflection to access volume paths (Android doesn't expose this publicly).
        /// </summary>
        public IEnumerable<string> GetRemovableDrives()
        {
            if (_storageManager == null)
                return Enumerable.Empty<string>();

            var drives = new List<string>();

            try
            {
                // Use reflection to access hidden StorageManager.getVolumes() method
                var getVolumesMethod = _storageManager.Class?
                    .GetMethod("getVolumes");

                if (getVolumesMethod != null)
                {
                    var volumes = (Java.Util.IList?)getVolumesMethod.Invoke(_storageManager);

                    if (volumes != null)
                    {
                        for (int i = 0; i < volumes.Size(); i++)
                        {
                            var volume = volumes.Get(i);
                            if (volume == null) continue;

                            // Check if volume is removable
                            var isRemovableMethod = volume.Class?.GetMethod("isRemovable");
                            var isRemovable = isRemovableMethod?.Invoke(volume) as Java.Lang.Boolean;

                            if (isRemovable?.BooleanValue() == true)
                            {
                                // Get volume path using reflection
                                var getPathMethod = volume.Class?.GetMethod("getPath");
                                var pathFile = getPathMethod?.Invoke(volume) as Java.IO.File;

                                if (pathFile != null && pathFile.Exists())
                                {
                                    drives.Add(pathFile.AbsolutePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidUsbDetector] Error enumerating USB drives: {ex.Message}");
            }

            return drives;
        }

        /// <summary>
        /// Checks if a path is a valid, currently-mounted removable drive.
        /// </summary>
        public bool IsValidRemovableDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var file = new Java.IO.File(path);
                if (!file.Exists() || !file.IsDirectory)
                    return false;

                // Check if the path is in the list of removable drives
                return GetRemovableDrives().Contains(path, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets drive information for a removable USB drive.
        /// </summary>
        public RemovableDriveInfo? GetDriveInfo(string path)
        {
            if (!IsValidRemovableDrive(path))
                return null;

            try
            {
                var file = new Java.IO.File(path);
                var statFs = new global::Android.OS.StatFs(path);

                // Android API level determines available methods
                long totalBytes;
                long availableBytes;

                if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.JellyBeanMr2)
                {
                    totalBytes = statFs.TotalBytes;
                    availableBytes = statFs.AvailableBytes;
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    totalBytes = (long)statFs.BlockCount * statFs.BlockSize;
                    availableBytes = (long)statFs.AvailableBlocks * statFs.BlockSize;
#pragma warning restore CS0618
                }

                return new RemovableDriveInfo
                {
                    Label = GetVolumeLabel(path),
                    TotalSizeBytes = totalBytes,
                    AvailableFreeSpaceBytes = availableBytes,
                    FileSystem = GetFileSystemType(path),
                    DeviceIdentifier = ComputeDeviceIdentifier(path),
                    RootPath = path
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidUsbDetector] Error getting drive info: {ex.Message}");
                return null;
            }
        }

        private string GetVolumeLabel(string path)
        {
            try
            {
                // Try to get volume label from storage manager
                if (_storageManager != null)
                {
                    var getVolumesMethod = _storageManager.Class?.GetMethod("getVolumes");
                    var volumes = (Java.Util.IList?)getVolumesMethod?.Invoke(_storageManager);

                    if (volumes != null)
                    {
                        for (int i = 0; i < volumes.Size(); i++)
                        {
                            var volume = volumes.Get(i);
                            var getPathMethod = volume?.Class?.GetMethod("getPath");
                            var pathFile = getPathMethod?.Invoke(volume) as Java.IO.File;

                            if (pathFile?.AbsolutePath == path)
                            {
                                var getDescriptionMethod = volume?.Class?.GetMethod("getDescription", _context.Class);
                                var description = getDescriptionMethod?.Invoke(volume, _context)?.ToString();
                                if (!string.IsNullOrWhiteSpace(description))
                                    return description;
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback: use directory name
            return System.IO.Path.GetFileName(path.TrimEnd('/')) ?? "USB Drive";
        }

        private string GetFileSystemType(string path)
        {
            try
            {
                // Android doesn't easily expose filesystem type
                // Common USB formats: FAT32, exFAT, NTFS
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string ComputeDeviceIdentifier(string path)
        {
            try
            {
                // Use volume UUID if available
                if (_storageManager != null)
                {
                    var getVolumesMethod = _storageManager.Class?.GetMethod("getVolumes");
                    var volumes = (Java.Util.IList?)getVolumesMethod?.Invoke(_storageManager);

                    if (volumes != null)
                    {
                        for (int i = 0; i < volumes.Size(); i++)
                        {
                            var volume = volumes.Get(i);
                            var getPathMethod = volume?.Class?.GetMethod("getPath");
                            var pathFile = getPathMethod?.Invoke(volume) as Java.IO.File;

                            if (pathFile?.AbsolutePath == path)
                            {
                                var getUuidMethod = volume?.Class?.GetMethod("getUuid");
                                var uuid = getUuidMethod?.Invoke(volume)?.ToString();
                                if (!string.IsNullOrWhiteSpace(uuid))
                                    return uuid;
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback: use path hash
            return path.GetHashCode().ToString("X8");
        }

        internal void OnUsbDeviceAttached(string devicePath)
        {
            RemovableDriveInserted?.Invoke(devicePath);
        }

        internal void OnUsbDeviceDetached(string devicePath)
        {
            RemovableDriveRemoved?.Invoke(devicePath);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            try
            {
                _context.UnregisterReceiver(_usbReceiver);
            }
            catch { }

            _isDisposed = true;
        }

        /// <summary>
        /// Broadcast receiver for USB device attach/detach events.
        /// </summary>
        private sealed class UsbReceiver : BroadcastReceiver
        {
            private readonly AndroidUsbDetector _detector;

            public UsbReceiver(AndroidUsbDetector detector)
            {
                _detector = detector;
            }

            public override void OnReceive(Context? context, Intent? intent)
            {
                if (intent == null) return;

                var action = intent.Action;
                if (action == UsbManager.ActionUsbDeviceAttached)
                {
                    var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
                    if (device != null)
                    {
                        // Wait a moment for the system to mount the drive
                        System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                        {
                            var drives = _detector.GetRemovableDrives().ToList();
                            if (drives.Any())
                            {
                                // Notify about the newest drive
                                _detector.OnUsbDeviceAttached(drives.Last());
                            }
                        });
                    }
                }
                else if (action == UsbManager.ActionUsbDeviceDetached)
                {
                    var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
                    if (device != null)
                    {
                        // Cannot reliably get the path of the detached device
                        // Just notify with device name
                        _detector.OnUsbDeviceDetached(device.DeviceName);
                    }
                }
            }
        }
    }
}
