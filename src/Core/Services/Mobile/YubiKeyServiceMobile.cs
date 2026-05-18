using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// FIDO2 credential registration result.
    /// </summary>
    public sealed class Fido2CredentialResult
    {
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
        public string AttestationFormat { get; set; } = string.Empty;
        public int SerialNumber { get; set; }
    }

    /// <summary>
    /// FIDO2 assertion (authentication) result.
    /// </summary>
    public sealed class Fido2AssertionResult
    {
        public byte[] Signature { get; set; } = Array.Empty<byte>();
        public byte[] AuthenticatorData { get; set; } = Array.Empty<byte>();
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Mobile stub for YubiKeyService. YubiKey USB HID is not available on Android.
    /// NFC-based YubiKey interaction would be handled by a dedicated platform layer.
    /// This stub keeps Core compiling on Android while surfacing clear not-supported errors.
    /// </summary>
    public sealed class YubiKeyService
    {
        private const string NotSupportedMessage =
            "YubiKey USB HID is not available on Android. " +
            "Use NFC-based YubiKey interaction if required.";

        /// <summary>
        /// Returns false – no USB HID YubiKey support on Android.
        /// </summary>
        public bool IsTokenPresent() => false;

        public Fido2CredentialResult RegisterCredential(
            string relyingPartyId,
            string userId,
            string userName,
            byte[] challenge,
            string? pin = null)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public Fido2AssertionResult Authenticate(
            string relyingPartyId,
            byte[] credentialId,
            byte[] challenge,
            byte[] publicKeyBytes,
            string? pin = null)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public bool VerifyAssertion(Fido2AssertionResult assertion, byte[] challenge, byte[] publicKeyBytes)
            => false;

        public bool VerifyDeviceMatch(int expectedSerial) => false;

        public void SetPin(string newPin, string? currentPin = null)
            => throw new PlatformNotSupportedException(NotSupportedMessage);

        public int? GetPinRetries() => null;

        public bool? IsPinSet() => null;

        public string? GetDeviceInfo() => null;

        public bool IsConfigured() => false;

        public bool SupportsFido2() => false;
    }
}
