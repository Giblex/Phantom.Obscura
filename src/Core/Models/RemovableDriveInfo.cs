namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Platform-agnostic information about a removable USB drive.
    /// Used by IUsbDetector implementations for cross-platform USB detection.
    /// </summary>
    public sealed class RemovableDriveInfo
    {
        /// <summary>
        /// User-friendly label or name of the drive (e.g., "PHANTOM_VAULT", "USB Drive")
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Total capacity of the drive in bytes
        /// </summary>
        public long TotalSizeBytes { get; set; }

        /// <summary>
        /// Available free space in bytes
        /// </summary>
        public long AvailableFreeSpaceBytes { get; set; }

        /// <summary>
        /// File system format (e.g., "NTFS", "FAT32", "exFAT", "APFS")
        /// </summary>
        public string FileSystem { get; set; } = string.Empty;

        /// <summary>
        /// Platform-specific identifier (volume serial, UUID, etc.)
        /// Used by UsbBindingService for device authentication
        /// </summary>
        public string DeviceIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Root path or URI for accessing files on this drive
        /// </summary>
        public string RootPath { get; set; } = string.Empty;
    }
}
