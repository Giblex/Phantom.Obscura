using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.Platform.Android
{
    /// <summary>
    /// Android implementation of IPasskeyService using Android Credential Manager FIDO2 API.
    /// On Android 9+ (API 28+), this wraps the BiometricPrompt-backed FIDO2 path.
    /// Requires the MAUI app layer to register a platform callback before use.
    /// </summary>
    public sealed class AndroidBiometricService : IPasskeyService
    {
        private Func<byte[], string, byte[], Task<bool>>? _authenticateHandler;
        private Func<string, string, string, byte[], Task<byte[]>>? _registerHandler;

        public bool IsSupported => true;
        public bool IsBiometricAvailable => true;
        public string AuthenticatorDescription => "Android Biometric / Credential Manager";

        /// <summary>
        /// Registers platform callbacks from the MAUI Android layer.
        /// Must be called before authentication is attempted.
        /// </summary>
        public void RegisterHandlers(
            Func<string, string, string, byte[], Task<byte[]>> registerHandler,
            Func<byte[], string, byte[], Task<bool>> authenticateHandler)
        {
            _registerHandler = registerHandler;
            _authenticateHandler = authenticateHandler;
        }

        public async Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            if (_registerHandler is null)
                throw new InvalidOperationException("Android biometric handler not registered.");
            return await _registerHandler(userId, userName, rpId, challenge);
        }

        public async Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            if (_authenticateHandler is null)
                throw new InvalidOperationException("Android biometric handler not registered.");
            return await _authenticateHandler(credentialId, rpId, challenge);
        }

        /// <summary>
        /// Rotates a credential by registering a fresh attestation under the same
        /// relying party / user and best-effort scheduling deletion of the prior
        /// credential id. On Android, hard deletion is initiated by the user
        /// through the Credential Manager UI — see <see cref="DeleteCredentialAsync"/>
        /// for the breadcrumb contract.
        /// </summary>
        public async Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId)
        {
            if (_registerHandler is null)
                throw new InvalidOperationException("Android biometric handler not registered.");
            if (oldCredentialId is null || oldCredentialId.Length == 0)
                throw new ArgumentException("oldCredentialId is required.", nameof(oldCredentialId));
            if (string.IsNullOrWhiteSpace(relyingParty))
                throw new ArgumentException("relyingParty is required.", nameof(relyingParty));
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("userId is required.", nameof(userId));

            // Fresh 32-byte challenge for the new attestation. WebAuthn requires
            // a high-entropy challenge per registration; we generate locally
            // because rotation is initiated by the app, not by a remote RP.
            var challenge = RandomNumberGenerator.GetBytes(32);

            byte[] newCredentialId;
            try
            {
                newCredentialId = await _registerHandler(userId, userId, relyingParty, challenge);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(challenge);
            }

            // Best-effort retire of the previous credential. Failure here is
            // intentionally non-fatal: rotation has already produced a valid
            // replacement, and Android cannot guarantee app-initiated deletion.
            try
            {
                await DeleteCredentialAsync(oldCredentialId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidBiometricService] Rotation: best-effort delete of prior credential failed: {ex.GetType().Name}");
            }

            return new PasskeyCredential
            {
                CredentialId = newCredentialId,
                CreatedAt = DateTimeOffset.UtcNow,
                Name = $"Rotated for {relyingParty}"
            };
        }

        /// <summary>
        /// On Android, app-side passkey deletion is not authoritative — the user
        /// must remove the credential from the system Credential Manager UI.
        /// This implementation therefore records a breadcrumb (no sensitive data)
        /// and returns success so callers can continue the rotation/cleanup flow.
        /// </summary>
        public Task DeleteCredentialAsync(byte[] credentialId)
        {
            Debug.WriteLine($"[AndroidBiometricService] DeleteCredentialAsync: app-side deletion is advisory only on Android (id length={credentialId?.Length ?? 0}).");
            return Task.CompletedTask;
        }
    }
}

