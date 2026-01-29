using System;
using System.Reactive;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for Passkey (WebAuthn/FIDO2) authentication settings.
    /// Manages passkey registration, verification, and device management.
    /// Uses real PasskeyService for Windows Hello/WebAuthn operations.
    /// </summary>
    public sealed class PasskeySettingsViewModel : ReactiveObject
    {
        private const string PasskeyCredentialName = "PhantomVault.Passkey";
        
        private bool _isPasskeyAvailable;
        private bool _isPasskeyEnabled;
        private bool _hasRegisteredPasskey;
        private string _registrationStatus = "No passkeys registered";
        private string _statusMessage = string.Empty;
        private bool _isBusy;
        private string _deviceName = Environment.MachineName;
        private Window? _ownerWindow;
        
        private readonly IPasskeyService _passkeyService;

        public PasskeySettingsViewModel() : this(new PasskeyService())
        {
        }

        public PasskeySettingsViewModel(IPasskeyService passkeyService)
        {
            _passkeyService = passkeyService ?? throw new ArgumentNullException(nameof(passkeyService));
            
            RegisterPasskeyCommand = ReactiveCommand.CreateFromTask(RegisterPasskey);
            RemovePasskeyCommand = ReactiveCommand.CreateFromTask(RemovePasskey);
            TestPasskeyCommand = ReactiveCommand.CreateFromTask(TestPasskey);
            CheckAvailabilityCommand = ReactiveCommand.CreateFromTask(CheckAvailability);

            // Initialize availability check
            _ = CheckAvailability();
        }

        public bool IsPasskeyAvailable
        {
            get => _isPasskeyAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isPasskeyAvailable, value);
        }

        public bool IsPasskeyEnabled
        {
            get => _isPasskeyEnabled;
            set => this.RaiseAndSetIfChanged(ref _isPasskeyEnabled, value);
        }

        public bool HasRegisteredPasskey
        {
            get => _hasRegisteredPasskey;
            private set => this.RaiseAndSetIfChanged(ref _hasRegisteredPasskey, value);
        }

        public string RegistrationStatus
        {
            get => _registrationStatus;
            private set => this.RaiseAndSetIfChanged(ref _registrationStatus, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string DeviceName
        {
            get => _deviceName;
            set => this.RaiseAndSetIfChanged(ref _deviceName, value);
        }

        public ReactiveCommand<Unit, Unit> RegisterPasskeyCommand { get; }
        public ReactiveCommand<Unit, Unit> RemovePasskeyCommand { get; }
        public ReactiveCommand<Unit, Unit> TestPasskeyCommand { get; }
        public ReactiveCommand<Unit, Unit> CheckAvailabilityCommand { get; }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        private async Task CheckAvailability()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Checking WebAuthn/FIDO2 support...";

                // Use PasskeyService to check real platform authenticator availability
                IsPasskeyAvailable = _passkeyService.IsSupported;
                
                if (IsPasskeyAvailable)
                {
                    // Check if we have an existing passkey credential
                    HasRegisteredPasskey = await CheckExistingPasskeyAsync().ConfigureAwait(false);
                    
                    StatusMessage = HasRegisteredPasskey 
                        ? $"Passkey registered on {DeviceName}"
                        : _passkeyService.AuthenticatorDescription;
                        
                    if (HasRegisteredPasskey)
                    {
                        RegistrationStatus = $"Registered on {DeviceName}";
                    }
                }
                else
                {
                    StatusMessage = _passkeyService.AuthenticatorDescription;
                    HasRegisteredPasskey = false;
                    RegistrationStatus = "No passkeys registered";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking availability: {ex.Message}";
                IsPasskeyAvailable = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RegisterPasskey()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Initiating passkey registration...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "Platform authenticator is not available";
                    return;
                }

                // Generate a cryptographic challenge for the WebAuthn ceremony
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // Use PasskeyService to register with the platform authenticator
                // This triggers the actual Windows Hello/biometric prompt
                var credentialId = await _passkeyService.RegisterAsync(
                    userId: Environment.UserName,
                    userName: $"{Environment.UserName}@{DeviceName}",
                    rpId: "phantomvault.local",
                    challenge: challenge
                ).ConfigureAwait(false);

                if (credentialId != null && credentialId.Length > 0)
                {
                    // Store the credential ID securely
                    await StorePasskeyCredentialAsync(credentialId).ConfigureAwait(false);
                    
                    HasRegisteredPasskey = true;
                    RegistrationStatus = $"Registered on {DeviceName}";
                    StatusMessage = "Passkey registration successful!";
                }
                else
                {
                    StatusMessage = "Passkey registration was cancelled";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "Platform authenticator is not available on this device";
                IsPasskeyAvailable = false;
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Registration failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Registration failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RemovePasskey()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Removing passkey credentials...";

                // Remove the stored credential
                await RemoveStoredPasskeyAsync().ConfigureAwait(false);

                HasRegisteredPasskey = false;
                RegistrationStatus = "No passkeys registered";
                StatusMessage = "Passkey credentials removed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to remove passkey: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TestPasskey()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Testing passkey authentication...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "Platform authenticator is not available";
                    return;
                }

                // Get the stored credential ID
                var credentialId = await GetStoredPasskeyCredentialAsync().ConfigureAwait(false);
                if (credentialId == null || credentialId.Length == 0)
                {
                    StatusMessage = "No passkey registered - please register first";
                    return;
                }

                // Generate a test challenge for the WebAuthn assertion ceremony
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // Use PasskeyService to verify with the platform authenticator
                var authenticated = await _passkeyService.AuthenticateAsync(
                    credentialId: credentialId,
                    rpId: "phantomvault.local",
                    challenge: challenge
                ).ConfigureAwait(false);

                if (authenticated)
                {
                    StatusMessage = "Passkey authentication test successful!";
                }
                else
                {
                    StatusMessage = "Passkey authentication test failed - verification declined";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "Platform authenticator is not available on this device";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Authentication test failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Checks if a passkey credential exists for this application.
        /// </summary>
        private async Task<bool> CheckExistingPasskeyAsync()
        {
            try
            {
                var credentialId = await GetStoredPasskeyCredentialAsync().ConfigureAwait(false);
                return credentialId != null && credentialId.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stores the passkey credential ID securely using DPAPI.
        /// </summary>
        private static Task StorePasskeyCredentialAsync(byte[] credentialId)
        {
            var credentialPath = GetPasskeyStoragePath();
            var directory = System.IO.Path.GetDirectoryName(credentialPath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            
            // Encrypt the credential ID with DPAPI for the current user
            var protectedData = ProtectedData.Protect(
                credentialId,
                System.Text.Encoding.UTF8.GetBytes(PasskeyCredentialName),
                DataProtectionScope.CurrentUser);
            
            System.IO.File.WriteAllBytes(credentialPath, protectedData);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves the stored passkey credential ID.
        /// </summary>
        private static Task<byte[]?> GetStoredPasskeyCredentialAsync()
        {
            var credentialPath = GetPasskeyStoragePath();
            if (!System.IO.File.Exists(credentialPath))
            {
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                var protectedData = System.IO.File.ReadAllBytes(credentialPath);
                var credentialId = ProtectedData.Unprotect(
                    protectedData,
                    System.Text.Encoding.UTF8.GetBytes(PasskeyCredentialName),
                    DataProtectionScope.CurrentUser);
                
                return Task.FromResult<byte[]?>(credentialId);
            }
            catch
            {
                return Task.FromResult<byte[]?>(null);
            }
        }

        /// <summary>
        /// Removes the stored passkey credential.
        /// </summary>
        private static Task RemoveStoredPasskeyAsync()
        {
            var credentialPath = GetPasskeyStoragePath();
            if (System.IO.File.Exists(credentialPath))
            {
                System.IO.File.Delete(credentialPath);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the path for storing the passkey credential ID securely.
        /// </summary>
        private static string GetPasskeyStoragePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(appDataPath, "PhantomVault", "Passkey", "credential.bin");
        }
    }
}
