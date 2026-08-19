using System;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.ViewModels
{

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

                IsWindowsHelloAvailable = _passkeyService.IsSupported;

                if (IsWindowsHelloAvailable)
                {

                    var biometricAvailable = _passkeyService.IsBiometricAvailable;

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
                Log.Warning(ex, "[WindowsHello] Availability check failed.");
                StatusMessage = "Windows Hello availability could not be checked. Try again.";
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

                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

                var credentialId = await _passkeyService.RegisterAsync(
                    userId: Environment.UserName,
                    userName: $"{Environment.UserName}@{Environment.MachineName}",
                    rpId: "phantomvault.local",
                    challenge: challenge
                ).ConfigureAwait(false);

                if (credentialId != null && credentialId.Length > 0)
                {

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
                Log.Warning(ex, "[WindowsHello] Enrollment was rejected.");
                StatusMessage = "Windows Hello enrollment could not be completed. Check Windows Hello and try again.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WindowsHello] Enrollment failed.");
                StatusMessage = "Windows Hello enrollment could not be completed. Try again.";
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

                await RemoveStoredCredentialAsync().ConfigureAwait(false);

                IsBiometricEnrolled = false;
                EnrollmentStatus = "Not enrolled";
                StatusMessage = "Stored Windows Hello credentials removed.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WindowsHello] Failed to remove stored credentials.");
                StatusMessage = "Stored Windows Hello credentials could not be removed. Try again.";
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

                var credentialId = await GetStoredCredentialIdAsync().ConfigureAwait(false);
                if (credentialId == null || credentialId.Length == 0)
                {
                    StatusMessage = "No Windows Hello credential is enrolled yet. Set one up first.";
                    return;
                }

                var challenge = new byte[32];
                RandomNumberGenerator.Fill(challenge);

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
                Log.Warning(ex, "[WindowsHello] Authentication test failed.");
                StatusMessage = "Windows Hello verification failed. Confirm device authentication and try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

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

        private static Task StoreCredentialIdAsync(byte[] credentialId)
        {
            WindowsCredentialStore.WriteSecret(
                VaultCredentialName,
                credentialId,
                "Phantom Obscura Windows Hello credential identifier");

            return Task.CompletedTask;
        }

        private static Task<byte[]?> GetStoredCredentialIdAsync()
        {
            try
            {
                return Task.FromResult<byte[]?>(WindowsCredentialStore.ReadSecret(VaultCredentialName));
            }
            catch
            {
                return Task.FromResult<byte[]?>(null);
            }
        }

        private static Task RemoveStoredCredentialAsync()
        {
            WindowsCredentialStore.DeleteSecret(VaultCredentialName);
            return Task.CompletedTask;
        }
    }
}

