using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using UIKit;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Mobile.Platforms.iOS
{
    /// <summary>
    /// iOS implementation of USB detection for USB-C and Lightning external drives.
    /// Uses Files app integration and UIDocumentPickerViewController for USB access.
    /// Note: iOS doesn't provide automatic USB detection like Android/desktop, so this
    /// implementation focuses on user-initiated drive selection via the Files app.
    /// </summary>
    public sealed class iOSUsbDetector : IUsbDetector
    {
        private readonly NSFileManager _fileManager;
        private readonly HashSet<string> _knownDrives = new();
        private bool _isDisposed;

        public iOSUsbDetector()
        {
            _fileManager = NSFileManager.DefaultManager;

            // Monitor for external storage notifications (iOS 13+)
            if (UIDevice.CurrentDevice.CheckSystemVersion(13, 0))
            {
                NSNotificationCenter.DefaultCenter.AddObserver(
                    UIApplication.DidBecomeActiveNotification,
                    OnAppBecameActive);
            }
        }

        public event Action<string>? RemovableDriveInserted;
        public event Action<string>? RemovableDriveRemoved;

        /// <summary>
        /// Enumerates currently accessible external storage volumes.
        /// On iOS, this scans the app's accessible file provider locations.
        /// External drives appear under /private/var/mobile/Library/LiveFiles/
        /// </summary>
        public IEnumerable<string> GetRemovableDrives()
        {
            var drives = new List<string>();

            try
            {
                // iOS 11+ external storage locations
                // External drives are accessible via Files app at specific paths
                var potentialPaths = new[]
                {
                    "/private/var/mobile/Library/LiveFiles/",
                    NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User).FirstOrDefault()?.Path
                };

                foreach (var basePath in potentialPaths)
                {
                    if (string.IsNullOrWhiteSpace(basePath) || !_fileManager.FileExists(basePath))
                        continue;

                    NSError? error;
                    var contents = _fileManager.GetDirectoryContent(basePath, out error);

                    if (error == null && contents != null)
                    {
                        foreach (var item in contents)
                        {
                            var fullPath = System.IO.Path.Combine(basePath, item);

                            // Check if it's an external volume (not local storage)
                            if (IsExternalVolume(fullPath))
                            {
                                drives.Add(fullPath);
                            }
                        }
                    }
                }

                // Check for bookmark-restored URLs (previously accessed external drives)
                drives.AddRange(GetBookmarkedDrives());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[iOSUsbDetector] Error enumerating USB drives: {ex.Message}");
            }

            return drives;
        }

        /// <summary>
        /// Checks if a path represents a valid external volume.
        /// </summary>
        public bool IsValidRemovableDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                if (!_fileManager.FileExists(path))
                    return false;

                return IsExternalVolume(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets drive information for an external volume.
        /// </summary>
        public RemovableDriveInfo? GetDriveInfo(string path)
        {
            if (!IsValidRemovableDrive(path))
                return null;

            try
            {
                NSError? error;
                var attributes = _fileManager.GetFileSystemAttributes(path, out error);

                if (error != null || attributes == null)
                    return null;

                long totalSize = 0;
                long freeSize = 0;

                if (attributes.TryGetValue(NSFileManager.SystemSize, out var totalSizeObj) && totalSizeObj is NSNumber totalNum)
                {
                    totalSize = totalNum.Int64Value;
                }

                if (attributes.TryGetValue(NSFileManager.SystemFreeSize, out var freeSizeObj) && freeSizeObj is NSNumber freeNum)
                {
                    freeSize = freeNum.Int64Value;
                }

                return new RemovableDriveInfo
                {
                    Label = GetVolumeLabel(path),
                    TotalSizeBytes = totalSize,
                    AvailableFreeSpaceBytes = freeSize,
                    FileSystem = GetFileSystemType(path),
                    DeviceIdentifier = ComputeDeviceIdentifier(path),
                    RootPath = path
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[iOSUsbDetector] Error getting drive info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Prompts the user to select a USB drive using the Files app picker.
        /// This is the primary way to access external storage on iOS.
        /// Returns the selected drive path or null if cancelled.
        /// </summary>
        public string? PromptForUsbDriveSelection()
        {
            // This would be called from the UI layer
            // Implementation would use UIDocumentPickerViewController
            // For now, return null - actual implementation in ViewModel
            return null;
        }

        private bool IsExternalVolume(string path)
        {
            try
            {
                // Check volume attributes to determine if it's external
                NSError? error;
                var url = NSUrl.FromFilename(path);
                var values = url.GetResourceValues(new[] { NSUrl.VolumeIsRemovableKey }, out error);

                if (error == null && values != null)
                {
                    if (values.TryGetValue(NSUrl.VolumeIsRemovableKey, out var isRemovable) && isRemovable is NSNumber num)
                    {
                        return num.BoolValue;
                    }
                }
            }
            catch { }

            // Fallback: check if path is outside app sandbox
            var documentsPath = NSFileManager.DefaultManager
                .GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User)
                .FirstOrDefault()?.Path;

            return !string.IsNullOrEmpty(documentsPath) && !path.StartsWith(documentsPath, StringComparison.OrdinalIgnoreCase);
        }

        private string GetVolumeLabel(string path)
        {
            try
            {
                NSError? error;
                var url = NSUrl.FromFilename(path);
                var values = url.GetResourceValues(new[] { NSUrl.VolumeNameKey }, out error);

                if (error == null && values != null)
                {
                    if (values.TryGetValue(NSUrl.VolumeNameKey, out var volumeName) && volumeName is NSString name)
                    {
                        return name.ToString();
                    }
                }
            }
            catch { }

            return System.IO.Path.GetFileName(path.TrimEnd('/')) ?? "External Drive";
        }

        private string GetFileSystemType(string path)
        {
            try
            {
                NSError? error;
                var url = NSUrl.FromFilename(path);
                var values = url.GetResourceValues(new[] { NSUrl.VolumeLocalizedFormatDescriptionKey }, out error);

                if (error == null && values != null)
                {
                    if (values.TryGetValue(NSUrl.VolumeLocalizedFormatDescriptionKey, out var formatDesc) && formatDesc is NSString desc)
                    {
                        return desc.ToString();
                    }
                }
            }
            catch { }

            return "Unknown";
        }

        private string ComputeDeviceIdentifier(string path)
        {
            try
            {
                NSError? error;
                var url = NSUrl.FromFilename(path);
                var values = url.GetResourceValues(new[] { NSUrl.VolumeUuidStringKey }, out error);

                if (error == null && values != null)
                {
                    if (values.TryGetValue(NSUrl.VolumeUuidStringKey, out var uuid) && uuid is NSString uuidStr)
                    {
                        return uuidStr.ToString();
                    }
                }
            }
            catch { }

            // Fallback: hash the path
            return path.GetHashCode().ToString("X8");
        }

        private IEnumerable<string> GetBookmarkedDrives()
        {
            // iOS security-scoped bookmarks allow persistent access to user-selected files/folders
            // This would retrieve previously saved bookmarks from NSUserDefaults
            var drives = new List<string>();

            try
            {
                var defaults = NSUserDefaults.StandardUserDefaults;
                var bookmarks = defaults.ArrayForKey("SavedUsbDriveBookmarks");

                if (bookmarks != null)
                {
                    foreach (var item in bookmarks)
                    {
                        if (item is NSData bookmarkData)
                        {
                            NSError? error;
                            bool isStale;
                            var url = NSUrl.FromBookmarkData(bookmarkData, NSUrlBookmarkResolutionOptions.WithoutUI, null, out isStale, out error);

                            if (error == null && url != null && !isStale)
                            {
                                // Start accessing security-scoped resource
                                if (url.StartAccessingSecurityScopedResource())
                                {
                                    drives.Add(url.Path ?? string.Empty);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[iOSUsbDetector] Error loading bookmarks: {ex.Message}");
            }

            return drives;
        }

        private void OnAppBecameActive(NSNotification notification)
        {
            // Scan for new drives when app becomes active
            var currentDrives = GetRemovableDrives().ToHashSet();

            // Detect newly inserted drives
            foreach (var drive in currentDrives)
            {
                if (!_knownDrives.Contains(drive))
                {
                    _knownDrives.Add(drive);
                    RemovableDriveInserted?.Invoke(drive);
                }
            }

            // Detect removed drives
            var removedDrives = _knownDrives.Where(d => !currentDrives.Contains(d)).ToList();
            foreach (var drive in removedDrives)
            {
                _knownDrives.Remove(drive);
                RemovableDriveRemoved?.Invoke(drive);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            NSNotificationCenter.DefaultCenter.RemoveObserver(this);

            // Stop accessing security-scoped resources
            foreach (var drive in _knownDrives)
            {
                try
                {
                    var url = NSUrl.FromFilename(drive);
                    url?.StopAccessingSecurityScopedResource();
                }
                catch { }
            }

            _knownDrives.Clear();
            _isDisposed = true;
        }
    }
}
