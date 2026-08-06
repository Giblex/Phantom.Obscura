using System;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Portable cryptographic identity of a single device authorized on a vault.
    /// The private keys for these public keys never leave the owning device (they
    /// are sealed at rest by the platform key protector). The public keys are what
    /// the manifest stores, so the vault trusts cryptographic identities rather than
    /// mutable hardware fingerprints.
    /// </summary>
    public sealed class DeviceIdentity
    {
        /// <summary>Stable, opaque device id (independent of hardware fingerprint).</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Human-friendly label, e.g. "AJ Desktop".</summary>
        public string? FriendlyName { get; set; }

        /// <summary>Base64 Ed25519 public key — the device's signing identity.</summary>
        public string SigningPublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Base64 X25519 public key — used to wrap the vault key to this device.</summary>
        public string AgreementPublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Role of the device on the vault (owner can authorize new devices).</summary>
        public DeviceRole Role { get; set; } = DeviceRole.Member;

        public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Authorization role of a device on a vault.</summary>
    public enum DeviceRole
    {
        /// <summary>Can use the vault but not authorize new devices.</summary>
        Member = 0,

        /// <summary>Can authorize/revoke other devices and re-sign the manifest.</summary>
        Owner = 1,
    }
}
