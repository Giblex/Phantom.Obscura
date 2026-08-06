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

        /// <summary>
        /// Base64 Ed25519 public key — the device's cryptographic signing identity.
        /// Null on legacy fingerprints created before public-key device identity.
        /// </summary>
        public string? SigningPublicKeyBase64 { get; set; }

        /// <summary>
        /// Base64 X25519 public key — used to wrap the vault key to this device.
        /// Null on legacy fingerprints created before public-key device identity.
        /// </summary>
        public string? AgreementPublicKeyBase64 { get; set; }

        /// <summary>
        /// Authorization role of this device on the vault. Owner devices can
        /// authorize new devices and re-sign the manifest.
        /// </summary>
        public DeviceRole Role { get; set; } = DeviceRole.Member;

        /// <summary>
        /// The vault key wrapped (ECIES over X25519) to this device's agreement
        /// public key. Only this device can unwrap it. Null until the device has
        /// been granted access during enrollment.
        /// </summary>
        public string? WrappedVaultKeyBase64 { get; set; }

        /// <summary>Stable device id matching the device's local identity record.</summary>
        public string? DeviceId { get; set; }

        public DateTimeOffset TrustedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastAccessAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

