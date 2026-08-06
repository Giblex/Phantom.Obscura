using System;
using System.IO;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Single source of truth for the on-device file layout. All PhantomVault-owned
    /// data lives under a single <c>.phantom</c> folder at the drive root so the
    /// device root stays clean. Anti-index sentinels and externally provisioned
    /// policy files (e.g. usb_key.json) are intentionally NOT covered here because
    /// they must remain at the volume root to function.
    /// </summary>
    public static class PhantomDeviceLayout
    {
        public const string PhantomFolderName = ".phantom";
        public const string SystemVolumeFileName = "system.bin";
        public const string DeviceIdFileName = "device.id";
        public const string PhantomKeyBindingTokenFileName = "phantomkey.auth.pkauth";
        public const string QuarantineFolderName = "quarantine";
        public const string AuditLogFileName = "vault.audit";

        public static string GetPhantomRoot(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            return Path.Combine(driveRoot, PhantomFolderName);
        }

        public static string EnsurePhantomRoot(string driveRoot)
        {
            var root = GetPhantomRoot(driveRoot);
            Directory.CreateDirectory(root);
            return root;
        }

        public static string GetSystemVolumePath(string driveRoot)
            => Path.Combine(GetPhantomRoot(driveRoot), SystemVolumeFileName);

        public static string SystemVolumeRelativePath
            => Path.Combine(PhantomFolderName, SystemVolumeFileName);

        public static string GetDeviceIdPath(string driveRoot)
            => Path.Combine(GetPhantomRoot(driveRoot), DeviceIdFileName);

        public static string GetPhantomKeyBindingTokenPath(string driveRoot)
            => Path.Combine(GetPhantomRoot(driveRoot), PhantomKeyBindingTokenFileName);

        public static string GetQuarantineDir(string driveRoot)
            => Path.Combine(GetPhantomRoot(driveRoot), QuarantineFolderName);

        /// <summary>
        /// Canonical location of the hash-chained activity/audit log, kept inside the
        /// <c>.phantom</c> folder so the device root stays clean.
        /// </summary>
        public static string GetAuditLogPath(string driveRoot)
            => Path.Combine(GetPhantomRoot(driveRoot), AuditLogFileName);
    }
}
