using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

#if IOS || MACCATALYST
using Foundation;
using LocalAuthentication;
using Security;
#endif

namespace PhantomVault.Core.Services.Platform
{
#if IOS || MACCATALYST

    public sealed class IOSPasskeyService : IPasskeyService
    {
        private readonly LAContext _context;

        public IOSPasskeyService()
        {
            _context = new LAContext();
        }

        public bool IsSupported => true;

        public bool IsBiometricAvailable
        {
            get
            {
                try
                {
                    NSError? error;
                    return _context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out error);
                }
                catch
                {
                    return false;
                }
            }
        }

        public string AuthenticatorDescription
        {
            get
            {
                if (!IsBiometricAvailable)
                    return "Device Passcode";

                switch (_context.BiometryType)
                {
                    case LABiometryType.FaceId:
                        return "Face ID";
                    case LABiometryType.TouchId:
                        return "Touch ID";
                    default:
                        return "Biometric Authentication";
                }
            }
        }

        public async Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));
            if (string.IsNullOrEmpty(userName)) throw new ArgumentException("User name cannot be empty.", nameof(userName));
            if (challenge == null) throw new ArgumentException("Challenge cannot be null.", nameof(challenge));

            var credentialId = $"phantomvault_{userId}_{Guid.NewGuid():N}";

            try
            {

                var authenticated = await AuthenticateAsync("Register new credential", "Confirm your identity to create a new vault credential");

                if (!authenticated)
                    throw new InvalidOperationException("User cancelled biometric authentication.");

                var keyQuery = new SecRecord(SecKind.Key)
                {
                    Account = credentialId,
                    Service = "com.phantomvault.credentials",
                    Label = $"PhantomVault - {userName}",
                    Accessible = SecAccessible.WhenPasscodeSetThisDeviceOnly,
                    UseOperationPrompt = "Authenticate to access vault"
                };

                var keyData = NSData.FromArray(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                keyQuery.ValueData = keyData;

                var status = SecKeyChain.Add(keyQuery);
                if (status != SecStatusCode.Success && status != SecStatusCode.DuplicateItem)
                {
                    throw new InvalidOperationException($"Failed to store credential in Keychain: {status}");
                }

                return System.Text.Encoding.UTF8.GetBytes(credentialId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to register iOS credential: {ex.Message}", ex);
            }
        }

        public async Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            if (credentialId == null || credentialId.Length == 0) throw new ArgumentException("Credential ID cannot be empty.", nameof(credentialId));
            if (challenge == null) throw new ArgumentException("Challenge cannot be null.", nameof(challenge));

            try
            {
                var credentialIdStr = System.Text.Encoding.UTF8.GetString(credentialId);

                var query = new SecRecord(SecKind.Key)
                {
                    Account = credentialIdStr,
                    Service = "com.phantomvault.credentials"
                };

                var status = SecKeyChain.QueryAsData(query, false, out var _);
                if (status != SecStatusCode.Success)
                    return false;

                return await AuthenticateAsync("Unlock vault", "Confirm your identity to unlock the vault");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<bool> AuthenticateAsync(string reason, string fallbackTitle = "")
        {
            try
            {
                var context = new LAContext
                {
                    LocalizedFallbackTitle = fallbackTitle
                };

                var policy = IsBiometricAvailable
                    ? LAPolicy.DeviceOwnerAuthenticationWithBiometrics
                    : LAPolicy.DeviceOwnerAuthentication;

                var result = await context.EvaluatePolicyAsync(policy, reason);
                return result.Item1;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
#else

    public sealed class IOSPasskeyService : IPasskeyService
    {
        public bool IsSupported => false;
        public bool IsBiometricAvailable => false;
        public string AuthenticatorDescription => "Not Available";

        public Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            throw new PlatformNotSupportedException("iOS passkey service is only available on iOS/iPadOS.");
        }

        public Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            throw new PlatformNotSupportedException("iOS passkey service is only available on iOS/iPadOS.");
        }

        public Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId)
        {
            throw new PlatformNotSupportedException("iOS passkey service is only available on iOS/iPadOS.");
        }

        public Task DeleteCredentialAsync(byte[] credentialId)
        {
            throw new PlatformNotSupportedException("iOS passkey service is only available on iOS/iPadOS.");
        }
    }
#endif
}

