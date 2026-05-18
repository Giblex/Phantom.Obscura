using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Mobile stub for PasskeyService. On Android, passkeys are handled via the
    /// Android Credential Manager API in the Platform.Android layer.
    /// This stub satisfies compile-time requirements for Core on non-Windows targets.
    /// </summary>
    public sealed class PasskeyService : IPasskeyService
    {
        /// <summary>
        /// Returns false on mobile – the mobile platform layer provides biometric auth separately.
        /// </summary>
        public bool IsSupported => false;

        /// <summary>
        /// Returns false – biometric availability is checked by the Android platform service.
        /// </summary>
        public bool IsBiometricAvailable => false;

        /// <inheritdoc />
        public string AuthenticatorDescription =>
            "Passkey/Windows-Hello integration is not available on this platform. " +
            "Use biometric unlock via Android Credential Manager instead.";

        /// <inheritdoc />
        public Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
            => throw new PlatformNotSupportedException(
                "PasskeyService.RegisterAsync is not supported on Android. " +
                "Use IAndroidBiometricService for biometric credential registration.");

        /// <inheritdoc />
        public Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
            => throw new PlatformNotSupportedException(
                "PasskeyService.AuthenticateAsync is not supported on Android. " +
                "Use IAndroidBiometricService for biometric authentication.");

        /// <inheritdoc />
        public Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId)
            => throw new PlatformNotSupportedException(
                "PasskeyService credential rotation is not supported on Android.");

        /// <inheritdoc />
        public Task DeleteCredentialAsync(byte[] credentialId)
            => Task.CompletedTask;

        // Extended methods that exist on the Windows implementation
        public void AddToRpAllowlist(string rpId) { }
        public void RemoveFromRpAllowlist(string rpId) { }
        public void ClearRpAllowlist() { }
        public bool IsRpAllowed(string rpId) => false;
    }
}
