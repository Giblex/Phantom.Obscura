using System;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Represents a unique device fingerprint used to detect vault access from new/unknown devices.
    /// Stored in VaultManifest.TrustedDevices to track which devices are authorized.
    /// </summary>
    public class DeviceFingerprint
    {
        /// <summary>
        /// Stable machine identifier (hash of hardware/OS/SID or persisted app ID).
        /// Primary key for device comparison.
        /// </summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>
        /// Operating system family (Windows, Linux, macOS).
        /// </summary>
        public string OsFamily { get; set; } = string.Empty;

        /// <summary>
        /// Operating system version string.
        /// </summary>
        public string OsVersion { get; set; } = string.Empty;

        /// <summary>
        /// Machine hostname at time of first vault access.
        /// </summary>
        public string Hostname { get; set; } = string.Empty;

        /// <summary>
        /// Username at time of first vault access.
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Optional friendly name for this device (e.g., "Work Laptop", "Home Desktop").
        /// Can be set by user for easier device management.
        /// </summary>
        public string? FriendlyName { get; set; }

        /// <summary>
        /// Timestamp when this device was first trusted.
        /// </summary>
        public DateTimeOffset TrustedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Timestamp of last vault access from this device.
        /// </summary>
        public DateTimeOffset LastAccessAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
