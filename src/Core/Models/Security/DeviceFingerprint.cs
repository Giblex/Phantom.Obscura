using System;

namespace PhantomVault.Core.Models.Security
{

    public class DeviceFingerprint
    {

        public string MachineId { get; set; } = string.Empty;

        public string OsFamily { get; set; } = string.Empty;

        public string OsVersion { get; set; } = string.Empty;

        public string Hostname { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string? FriendlyName { get; set; }

        public DateTimeOffset TrustedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastAccessAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

