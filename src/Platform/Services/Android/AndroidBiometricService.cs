using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Services.Platform.Android
{

    public sealed class AndroidBiometricService : IPasskeyService
    {
        private Func<byte[], string, byte[], Task<bool>>? _authenticateHandler;
        private Func<string, string, string, byte[], Task<byte[]>>? _registerHandler;

        public bool IsSupported => true;
        public bool IsBiometricAvailable => true;
        public string AuthenticatorDescription => "Android Biometric / Credential Manager";

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

        public Task DeleteCredentialAsync(byte[] credentialId)
        {
            Debug.WriteLine($"[AndroidBiometricService] DeleteCredentialAsync: app-side deletion is advisory only on Android (id length={credentialId?.Length ?? 0}).");
            return Task.CompletedTask;
        }
    }
}

