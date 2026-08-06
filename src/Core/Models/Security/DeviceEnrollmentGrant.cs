using System;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Enrollment grant a trusted device returns to a newly authorized device.
    /// Carries the vault key wrapped to the new device's agreement key (only that
    /// device can unwrap it) plus the vault's manifest signing public key so the
    /// new device can verify future manifests.
    /// </summary>
    public sealed class DeviceEnrollmentGrant
    {
        /// <summary>Identifier of the vault the device was granted access to.</summary>
        public string? VaultId { get; set; }

        public string? VaultLabel { get; set; }

        /// <summary>Echoes the granted device's id so the recipient can match it.</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Base64 ECIES-wrapped vault key, decryptable only by the granted device.</summary>
        public string WrappedVaultKeyBase64 { get; set; } = string.Empty;

        /// <summary>Base64 Ed25519 public key the vault signs its manifest with.</summary>
        public string? ManifestSigningPublicKeyBase64 { get; set; }

        public DeviceRole GrantedRole { get; set; } = DeviceRole.Member;

        public DateTimeOffset GrantedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(DeviceId)
            && !string.IsNullOrWhiteSpace(WrappedVaultKeyBase64);
    }
}
