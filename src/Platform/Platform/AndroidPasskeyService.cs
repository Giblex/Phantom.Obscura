using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

#if ANDROID
using Android.Content;
using AndroidX.Biometric;
using AndroidX.Fragment.App;
#endif

namespace PhantomVault.Core.Services.Platform
{
#if ANDROID
    /// <summary>
    /// Android implementation of passkey service using BiometricPrompt API.
    /// Requires Android 9.0 (API 28) or later for full biometric support.
    /// Falls back to device credentials (PIN/pattern/password) on older devices.
    /// </summary>
    public sealed class AndroidPasskeyService : IPasskeyService
    {
        private readonly FragmentActivity _activity;
        private readonly Context _context;

        public AndroidPasskeyService(FragmentActivity activity, Context context)
        {
            _activity = activity ?? throw new ArgumentNullException(nameof(activity));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public bool IsSupported => Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.P;

        public bool IsBiometricAvailable
        {
            get
            {
                try
                {
                    var biometricManager = BiometricManager.From(_context);
                    var canAuthenticate = biometricManager.CanAuthenticate(BiometricManager.Authenticators.BiometricStrong);
                    return canAuthenticate == BiometricManager.BiometricSuccess;
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
                if (IsBiometricAvailable)
                    return "Fingerprint / Face Unlock";
                return "Device Credentials";
            }
        }

        public async Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));
            if (string.IsNullOrEmpty(userName)) throw new ArgumentException("User name cannot be empty.", nameof(userName));
            if (challenge == null) throw new ArgumentException("Challenge cannot be null.", nameof(challenge));

            // For Android, we'll use the Android Keystore to generate a hardware-backed key
            // The credential ID will be the key alias
            var credentialId = $"phantomvault_{userId}_{Guid.NewGuid():N}";
            
            try
            {
                // Prompt for biometric authentication to confirm registration
                var authenticated = await AuthenticateBiometricAsync("Register new credential", "Confirm your identity to create a new vault credential");
                
                if (!authenticated)
                    throw new InvalidOperationException("User cancelled biometric authentication.");

                // Generate hardware-backed key in Android Keystore
                var keyStore = Java.Security.KeyStore.GetInstance("AndroidKeyStore");
                keyStore?.Load(null);

                var keyPairGenerator = Java.Security.KeyPairGenerator.GetInstance(
                    Java.Security.KeyFactory.DefaultType, "AndroidKeyStore");
                
                var builder = new Android.Security.Keystore.KeyGenParameterSpec.Builder(
                    credentialId,
                    Android.Security.Keystore.KeyStorePurpose.Sign | Android.Security.Keystore.KeyStorePurpose.Verify)
                    .SetDigests(Android.Security.Keystore.KeyProperties.DigestSha256)
                    .SetSignaturePaddings(Android.Security.Keystore.KeyProperties.SignaturePaddingRsaPkcs1)
                    .SetUserAuthenticationRequired(true)
                    .SetUserAuthenticationValidityDurationSeconds(30);

                keyPairGenerator?.Initialize(builder.Build());
                keyPairGenerator?.GenerateKeyPair();

                return System.Text.Encoding.UTF8.GetBytes(credentialId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to register Android credential: {ex.Message}", ex);
            }
        }

        public async Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            if (credentialId == null || credentialId.Length == 0) throw new ArgumentException("Credential ID cannot be empty.", nameof(credentialId));
            if (challenge == null) throw new ArgumentException("Challenge cannot be null.", nameof(challenge));

            try
            {
                var credentialIdStr = System.Text.Encoding.UTF8.GetString(credentialId);
                
                // Verify the key exists in Keystore
                var keyStore = Java.Security.KeyStore.GetInstance("AndroidKeyStore");
                keyStore?.Load(null);
                
                if (keyStore?.ContainsAlias(credentialIdStr) != true)
                    return false;

                // Prompt for biometric authentication
                return await AuthenticateBiometricAsync("Unlock vault", "Confirm your identity to unlock the vault");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Task<bool> AuthenticateBiometricAsync(string title, string subtitle)
        {
            var tcs = new TaskCompletionSource<bool>();

            _activity.RunOnUiThread(() =>
            {
                try
                {
                    var promptInfo = new BiometricPrompt.PromptInfo.Builder()
                        .SetTitle(title)
                        .SetSubtitle(subtitle)
                        .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong | 
                                                  BiometricManager.Authenticators.DeviceCredential)
                        .Build();

                    var biometricPrompt = new BiometricPrompt(_activity, new BiometricAuthCallback(tcs));
                    biometricPrompt.Authenticate(promptInfo);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private class BiometricAuthCallback : BiometricPrompt.AuthenticationCallback
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public BiometricAuthCallback(TaskCompletionSource<bool> tcs)
            {
                _tcs = tcs;
            }

            public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
            {
                base.OnAuthenticationSucceeded(result);
                _tcs.TrySetResult(true);
            }

            public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence? errString)
            {
                base.OnAuthenticationError(errorCode, errString);
                _tcs.TrySetResult(false);
            }

            public override void OnAuthenticationFailed()
            {
                base.OnAuthenticationFailed();
                // Don't complete the task yet - user can retry
            }
        }
    }
#else
    /// <summary>
    /// Placeholder for when Android-specific code is not being compiled.
    /// </summary>
    public sealed class AndroidPasskeyService : IPasskeyService
    {
        public bool IsSupported => false;
        public bool IsBiometricAvailable => false;
        public string AuthenticatorDescription => "Not Available";

        public Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge)
        {
            throw new PlatformNotSupportedException("Android passkey service is only available on Android.");
        }

        public Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge)
        {
            throw new PlatformNotSupportedException("Android passkey service is only available on Android.");
        }

        public Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId)
        {
            throw new PlatformNotSupportedException("Android passkey service is only available on Android.");
        }

        public Task DeleteCredentialAsync(byte[] credentialId)
        {
            throw new PlatformNotSupportedException("Android passkey service is only available on Android.");
        }
    }
#endif
}
