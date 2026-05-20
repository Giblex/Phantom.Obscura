using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Binding strength classification for device identity.
    /// </summary>
    public enum DeviceBindingStrength
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VolumeSerial = 4
    }

    /// <summary>
    /// Mobile-compatible implementation of <c>UsbBindingService</c>.
    ///
    /// Policy (Phantom Obscura): USB-vault binding is created and rotated
    /// <b>only on the desktop client</b>, which has access to volume serial
    /// numbers via WMI and to the full bind/rotate ceremony. The mobile
    /// (Android) client is permitted to <b>verify</b> an already-bound USB
    /// vault that was provisioned on the desktop — it must never create,
    /// initialise, or rewrite a binding.
    ///
    /// Therefore this stub:
    ///   • reads the hidden marker file written by the desktop and returns
    ///     the device id it carries,
    ///   • verifies an attached drive against an expected device id, and
    ///   • fails closed with <see cref="PlatformNotSupportedException"/> on
    ///     any method that would write a binding to the drive.
    /// </summary>
    public sealed class UsbBindingService
    {
        private const string HiddenIdFileName = ".phantom_device_id";

        private const string BindingForbiddenOnMobile =
            "USB-vault binding can only be performed on the desktop client. " +
            "Insert a USB vault that was already bound on the desktop to unlock on Android.";

        /// <summary>
        /// Reads the device id that was written by the desktop binding step.
        /// Throws if no marker file exists — the phone never synthesises one.
        /// </summary>
        public string ComputeDeviceId(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot))
                throw new ArgumentNullException(nameof(driveRoot));

            var markerPath = Path.Combine(driveRoot, HiddenIdFileName);
            if (!File.Exists(markerPath))
            {
                throw new InvalidOperationException(
                    "This USB volume is not bound to a Phantom Obscura vault. " +
                    "Bind the drive on the desktop client first.");
            }

            var stored = File.ReadAllText(markerPath).Trim();
            if (string.IsNullOrEmpty(stored))
            {
                throw new InvalidOperationException(
                    "Device binding marker is empty or corrupted.");
            }

            return stored;
        }

        /// <summary>
        /// Reports the strength of the binding the desktop wrote to the drive,
        /// or <see cref="DeviceBindingStrength.None"/> if no marker is present.
        /// Phones can never raise this — only the desktop can rewrite it.
        /// </summary>
        public DeviceBindingStrength GetBindingStrength(string driveRoot)
        {
            var markerPath = Path.Combine(driveRoot, HiddenIdFileName);
            return File.Exists(markerPath) ? DeviceBindingStrength.Medium : DeviceBindingStrength.None;
        }

        /// <summary>
        /// Verifies that the inserted drive matches the expected device id.
        /// </summary>
        public void EnsureBoundToDevice(string driveRoot, string expectedDeviceId)
        {
            var actualId = ComputeDeviceId(driveRoot);
            if (!string.Equals(actualId, expectedDeviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Device identity mismatch. The vault is bound to a different storage device.");
            }
        }

        /// <summary>
        /// Reads (but does not create) the hidden device id marker.
        /// </summary>
        public string ReadHiddenDeviceId(string driveRoot, byte[] vaultSalt)
        {
            _ = vaultSalt; // signature parity with desktop — not consumed on read
            var markerPath = Path.Combine(driveRoot, HiddenIdFileName);
            if (!File.Exists(markerPath))
                throw new FileNotFoundException("Device ID marker not found.", markerPath);
            return File.ReadAllText(markerPath).Trim();
        }

        /// <summary>
        /// Returns whether the drive carries a marker written by the desktop.
        /// </summary>
        public bool HasHiddenDeviceId(string driveRoot)
        {
            var markerPath = Path.Combine(driveRoot, HiddenIdFileName);
            return File.Exists(markerPath);
        }

        /// <summary>
        /// Recomputes the high-assurance id from the salt and drive metadata
        /// <i>without writing it</i>. Used by callers that want to compare the
        /// stored marker against a freshly-derived value as part of verification.
        /// </summary>
        public string ComputeHighAssuranceDeviceId(string driveRoot, byte[] vaultSalt)
        {
            var baseData = Encoding.UTF8.GetBytes(driveRoot ?? string.Empty);
            using var hmac = new HMACSHA256(vaultSalt);
            var derived = hmac.ComputeHash(baseData);
            return Convert.ToHexString(derived).ToLowerInvariant();
        }

        // ── Write paths — disabled on mobile by policy ───────────────────────

        /// <summary>Mobile policy: phones cannot create a binding.</summary>
        public string CreateHiddenDeviceId(string driveRoot, byte[] vaultSalt)
            => throw new PlatformNotSupportedException(BindingForbiddenOnMobile);

        /// <summary>Mobile policy: phones cannot initialise a binding.</summary>
        public string InitializeHighAssuranceBinding(string driveRoot, byte[] vaultSalt)
            => throw new PlatformNotSupportedException(BindingForbiddenOnMobile);
    }
}
