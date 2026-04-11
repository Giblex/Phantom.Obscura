using System;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for Windows Hello settings.
    /// Manages Windows Hello-backed enrollment, verification, and configuration.
    /// Uses the current platform-backed authenticator service.
    /// </summary>
    public sealed class WindowsHelloSettingsViewModel : ReactiveObject
    {
        private const string VaultCredentialName = "PhantomVault.WindowsHello";
        
        private bool _isWindowsHelloAvailable;
        private bool _isWindowsHelloEnabled;
        private bool _isBiometricEnrolled;
        private string _enrollmentStatus = "Not enrolled";
        private string _statusMessage = string.Empty;
        private bool _isBusy;
        private Window? _ownerWindow;
        
        private readonly IPasskeyService _passkeyService;

        public WindowsHelloSettingsViewModel() : this(new PasskeyService())
        {
        }

        public WindowsHelloSettingsViewModel(IPasskeyService passkeyService)
        {
            _passkeyService = passkeyService ?? throw new ArgumentNullException(nameof(passkeyService));
            
            EnrollBiometricCommand = ReactiveCommand.CreateFromTask(EnrollBiometric);
            RemoveBiometricCommand = ReactiveCommand.CreateFromTask(RemoveBiometric);
            TestBiometricCommand = ReactiveCommand.CreateFromTask(TestBiometric);
            CheckAvailabilityCommand = ReactiveCommand.CreateFromTask(CheckAvailability);

            // Initialize availability check
            _ = CheckAvailability();
        }

        public bool IsWindowsHelloAvailable
        {
            get => _isWindowsHelloAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isWindowsHelloAvailable, value);
        }

        public bool IsWindowsHelloEnabled
        {
            get => _isWindowsHelloEnabled;
            set => this.RaiseAndSetIfChanged(ref _isWindowsHelloEnabled, value);
        }

        public bool IsBiometricEnrolled
        {
            get => _isBiometricEnrolled;
            private set => this.RaiseAndSetIfChanged(ref _isBiometricEnrolled, value);
        }

        public string EnrollmentStatus
        {
            get => _enrollmentStatus;
            private set => this.RaiseAndSetIfChanged(ref _enrollmentStatus, value);
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

        public ReactiveCommand<Unit, Unit> EnrollBiometricCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveBiometricCommand { get; }
        public ReactiveCommand<Unit, Unit> TestBiometricCommand { get; }
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
                StatusMessage = "Checking Windows Hello availability...";

                // Use the PasskeyService to check real Windows Hello availability
                IsWindowsHelloAvailable = _passkeyService.IsSupported;
                
                if (IsWindowsHelloAvailable)
                {
                    // Check if biometric specifically is available (not just PIN)
                    var biometricAvailable = _passkeyService.IsBiometricAvailable;
                    
                    // Check if we have an existing credential for this app
                    IsBiometricEnrolled = await CheckExistingCredentialAsync().ConfigureAwait(false);
                    
                    if (biometricAvailable)
                    {
                        StatusMessage = IsBiometricEnrolled 
                            ? "Windows Hello is set up and ready"
                            : "Windows Hello is available. Set it up to use this local authenticator flow.";
                    }
                    else
                    {
                        StatusMessage = "Windows Hello is available, but biometric hardware was not detected. PIN-based verification may still work.";
                    }
                }
                else
                {
                    StatusMessage = "Windows Hello is not available on this device";
                    IsBiometricEnrolled = false;
                }

                if (IsBiometricEnrolled)
                {
                    EnrollmentStatus = "Enrolled - Biometric authentication active";
                }
                else
                {
                    EnrollmentStatus = "Not enrolled";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking availability: {ex.Message}";
                IsWindowsHelloAvailable = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnrollBiometric()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Starting Windows Hello setup...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "Windows Hello is not available on this device";
                    return;
                }

                // Generate a challenge for the registration
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // Use PasskeyService to register with Windows Hello
                // This triggers the actual Windows Hello prompt
                var credentialId = await _passkeyService.RegisterAsync(
                    userId: Environment.UserName,
                    userName: $"{Environment.UserName}@{Environment.MachineName}",
                    rpId: "phantomvault.local",
                    challenge: challenge
                ).ConfigureAwait(false);

                if (credentialId != null && credentialId.Length > 0)
                {
                    // Store the credential ID for later authentication
                    await StoreCredentialIdAsync(credentialId).ConfigureAwait(false);
                    
                    IsBiometricEnrolled = true;
                    EnrollmentStatus = "Enrolled - Biometric authentication active";
                    StatusMessage = "Windows Hello setup completed successfully.";
                }
                else
                {
                    StatusMessage = "Windows Hello setup was cancelled.";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "Windows Hello is not available on this device";
                IsWindowsHelloAvailable = false;
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Enrollment failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Enrollment failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RemoveBiometric()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Removing stored Windows Hello credentials...";

                // Remove the stored credential ID
                await RemoveStoredCredentialAsync().ConfigureAwait(false);

                IsBiometricEnrolled = false;
                EnrollmentStatus = "Not enrolled";
                StatusMessage = "Stored Windows Hello credentials removed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to remove credentials: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TestBiometric()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Testing Windows Hello authentication...";

                if (!_passkeyService.IsSupported)
                {
                    StatusMessage = "Windows Hello is not available";
                    return;
                }

                // Get the stored credential ID
                var credentialId = await GetStoredCredentialIdAsync().ConfigureAwait(false);
                if (credentialId == null || credentialId.Length == 0)
                {
                    StatusMessage = "No Windows Hello credential is enrolled yet. Set one up first.";
                    return;
                }

                // Generate a test challenge
                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                // Use PasskeyService to verify with Windows Hello
                var authenticated = await _passkeyService.AuthenticateAsync(
                    credentialId: credentialId,
                    rpId: "phantomvault.local",
                    challenge: challenge
                ).ConfigureAwait(false);

                if (authenticated)
                {
                    StatusMessage = "Windows Hello authentication test succeeded.";
                }
                else
                {
                    StatusMessage = "Windows Hello authentication test failed because verification was declined.";
                }
            }
            catch (PlatformNotSupportedException)
            {
                StatusMessage = "Windows Hello is not available on this device";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Windows Hello authentication test failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Checks if a Windows Hello credential exists for this application.
        /// </summary>
        private async Task<bool> CheckExistingCredentialAsync()
        {
            try
            {
                var credentialId = await GetStoredCredentialIdAsync().ConfigureAwait(false);
                return credentialId != null && credentialId.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stores the credential ID in a secure location.
        /// In a production app, this would be stored in the Windows Credential Manager
        /// or in the vault manifest encrypted.
        /// </summary>
        private static Task StoreCredentialIdAsync(byte[] credentialId)
        {
            // Store in isolated storage or user profile
            var credentialPath = GetCredentialStoragePath();
            var directory = System.IO.Path.GetDirectoryName(credentialPath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            
            // Encrypt the credential ID with DPAPI for the current user
            var protectedData = System.Security.Cryptography.ProtectedData.Protect(
                credentialId,
                System.Text.Encoding.UTF8.GetBytes(VaultCredentialName),
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            
            System.IO.File.WriteAllBytes(credentialPath, protectedData);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves the stored credential ID.
        /// </summary>
        private static Task<byte[]?> GetStoredCredentialIdAsync()
        {
            var credentialPath = GetCredentialStoragePath();
            if (!System.IO.File.Exists(credentialPath))
            {
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                var protectedData = System.IO.File.ReadAllBytes(credentialPath);
                var credentialId = System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedData,
                    System.Text.Encoding.UTF8.GetBytes(VaultCredentialName),
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                
                return Task.FromResult<byte[]?>(credentialId);
            }
            catch
            {
                return Task.FromResult<byte[]?>(null);
            }
        }

        /// <summary>
        /// Removes the stored credential.
        /// </summary>
        private static Task RemoveStoredCredentialAsync()
        {
            var credentialPath = GetCredentialStoragePath();
            if (System.IO.File.Exists(credentialPath))
            {
                System.IO.File.Delete(credentialPath);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the path for storing the credential ID securely.
        /// </summary>
        private static string GetCredentialStoragePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(appDataPath, "PhantomVault", "WindowsHello", "credential.bin");
        }
    }
}
