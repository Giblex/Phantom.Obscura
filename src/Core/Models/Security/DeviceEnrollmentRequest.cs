using System;

namespace PhantomVault.Core.Models.Security
{
    /// <summary>
    /// Portable enrollment request a new (unenrolled) device shows — as text or a
    /// QR code — to a trusted device. Contains only public material, so it is safe
    /// to display. The trusted device uses it to authorize the new device.
    /// </summary>
    public sealed class DeviceEnrollmentRequest
    {
        public string DeviceId { get; set; } = string.Empty;

        public string? FriendlyName { get; set; }

        /// <summary>Base64 Ed25519 signing public key of the requesting device.</summary>
        public string SigningPublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Base64 X25519 agreement public key — vault key is wrapped to this.</summary>
        public string AgreementPublicKeyBase64 { get; set; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(DeviceId)
            && !string.IsNullOrWhiteSpace(SigningPublicKeyBase64)
            && !string.IsNullOrWhiteSpace(AgreementPublicKeyBase64);
    }
}
