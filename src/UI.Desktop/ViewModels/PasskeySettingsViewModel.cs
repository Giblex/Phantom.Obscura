using System;
using System.Reactive;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the platform-backed device-authenticator settings surface.
    /// Manages local credential registration, verification, and device management.
    /// Uses the current platform-backed authenticator surface exposed by PasskeyService.
    /// </summary>
    public sealed class PasskeySettingsViewModel : ReactiveObject
    {
        private const string PasskeyCredentialName = "PhantomVault.Passkey";
        
        private bool _isPasskeyAvailable;
        private bool _isPasskeyEnabled;
        private bool _hasRegisteredPasskey;
        private string _registrationStatus = "No device credentials registered";
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
                StatusMessage = "Checking platform authenticator availability...";

                // Use PasskeyService to check real platform authenticator availability
                IsPasskeyAvailable = _passkeyService.IsSupported;
                
                if (IsPasskeyAvailable)
                {
                    // Check if we have an existing passkey credential
                    HasRegisteredPasskey = await CheckExistingPasskeyAsync().ConfigureAwait(false);
                    
                    StatusMessage = HasRegisteredPasskey 
                        ? $"Device credential registered on {DeviceName}"
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
                    RegistrationStatus = "No device credentials registered";
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
                StatusMessage = "Starting platform authenticator registration...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "The platform authenticator is not available on this device";
                    return;
                }

                // Generate a challenge for the current platform-authenticator flow
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
                    StatusMessage = "Local device credential registration successful.";
                }
                else
                {
                    StatusMessage = "Authenticator registration was cancelled.";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "The platform authenticator is not available on this device";
                IsPasskeyAvailable = false;
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Authenticator registration failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Authenticator registration failed: {ex.Message}";
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
                StatusMessage = "Removing stored authenticator credentials...";

                // Remove the stored credential
                await RemoveStoredPasskeyAsync().ConfigureAwait(false);

                HasRegisteredPasskey = false;
                RegistrationStatus = "No device credentials registered";
                StatusMessage = "Stored authenticator credentials removed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to remove stored authenticator credentials: {ex.Message}";
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
                StatusMessage = "Testing platform authenticator authentication...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "The platform authenticator is not available on this device";
                    return;
                }

                // Get the stored credential ID
                var credentialId = await GetStoredPasskeyCredentialAsync().ConfigureAwait(false);
                if (credentialId == null || credentialId.Length == 0)
                {
                    StatusMessage = "No authenticator credential is registered yet. Register one first.";
                    return;
                }

                // Generate a test challenge for the current authenticator assertion flow
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
                    StatusMessage = "Authenticator test succeeded.";
                }
                else
                {
                    StatusMessage = "Authenticator test failed because verification was declined.";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "The platform authenticator is not available on this device";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Authenticator test failed: {ex.Message}";
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
        /// Stores the passkey credential ID in the Windows Credential Manager.
        /// </summary>
        private static Task StorePasskeyCredentialAsync(byte[] credentialId)
        {
            WindowsCredentialStore.WriteSecret(
                PasskeyCredentialName,
                credentialId,
                "Phantom Obscura platform authenticator credential identifier");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves the stored passkey credential ID.
        /// </summary>
        private static Task<byte[]?> GetStoredPasskeyCredentialAsync()
        {
            try
            {
                return Task.FromResult<byte[]?>(WindowsCredentialStore.ReadSecret(PasskeyCredentialName));
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
            WindowsCredentialStore.DeleteSecret(PasskeyCredentialName);
            return Task.CompletedTask;
        }
    }
}
