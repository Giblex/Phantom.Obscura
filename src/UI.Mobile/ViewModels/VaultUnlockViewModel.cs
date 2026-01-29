using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;
using PhantomVault.Core.Models;
using PhantomVault.UI.Mobile.Services;

namespace PhantomVault.UI.Mobile.ViewModels
{
    /// <summary>
    /// ViewModel for the mobile vault unlock page.
    /// Handles USB drive detection, password entry, biometric authentication,
    /// and vault decryption on mobile platforms (Android and iOS).
    /// </summary>
    public sealed partial class VaultUnlockViewModel : ObservableObject
    {
        private readonly IUsbDetector _usbDetector;
        private readonly ManifestService _manifestService;
        private readonly VaultService _vaultService;
        private readonly UsbBindingService _usbBindingService;
        private bool _developerBypassMode = false;

        public VaultUnlockViewModel(
            IUsbDetector usbDetector,
            ManifestService manifestService,
            VaultService vaultService,
            UsbBindingService usbBindingService)
        {
            _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
            _usbBindingService = usbBindingService ?? throw new ArgumentNullException(nameof(usbBindingService));

            // Check for developer mode
#if DEBUG
            if (MobileDeveloperMode.IsEnabled)
            {
                _developerBypassMode = true;
                MobileDeveloperMode.Log("Developer bypass mode enabled in VaultUnlockViewModel");
                
                // Auto-fill developer credentials
                if (MobileDeveloperMode.BypassPhantomKey)
                {
                    Password = MobileDeveloperMode.DeveloperMasterPassword;
                    StatusMessage = "Developer Mode: Credentials auto-filled";
                }
            }
#endif

            // Subscribe to USB events
            _usbDetector.RemovableDriveInserted += OnUsbDriveInserted;
            _usbDetector.RemovableDriveRemoved += OnUsbDriveRemoved;

            // Load available drives
            RefreshDrives();

            // Check if biometrics are available on this device
            CheckBiometricsAvailability();
        }

        #region Observable Properties

        [ObservableProperty]
        private ObservableCollection<RemovableDriveInfo> _availableDrives = new();

        [ObservableProperty]
        private RemovableDriveInfo? _selectedDrive;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _isPasswordMasked = true;

        [ObservableProperty]
        private bool _useKeyfile = false;

        [ObservableProperty]
        private string _keyfilePath = string.Empty;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasError = false;

        [ObservableProperty]
        private bool _biometricsAvailable = false;

        [ObservableProperty]
        private bool _showKeyfileOption = true;

        #endregion

        #region Computed Properties

        public bool HasSelectedDrive => SelectedDrive != null;

        public bool CanUnlock => !IsBusy && HasSelectedDrive && !string.IsNullOrWhiteSpace(Password);

        public string DriveInfoText
        {
            get
            {
                if (SelectedDrive == null)
                    return string.Empty;

                var totalGB = SelectedDrive.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
                var freeGB = SelectedDrive.AvailableFreeSpaceBytes / (1024.0 * 1024.0 * 1024.0);

                return $"{SelectedDrive.FileSystem} • {totalGB:F1} GB total • {freeGB:F1} GB free";
            }
        }

        public string PasswordVisibilityIcon => IsPasswordMasked ? "👁" : "🔒";

        #endregion

        #region Commands

        [RelayCommand]
        private void TogglePasswordVisibility()
        {
            IsPasswordMasked = !IsPasswordMasked;
            OnPropertyChanged(nameof(PasswordVisibilityIcon));
        }

        [RelayCommand]
        private async Task BrowseDrive()
        {
            try
            {
                // On iOS, this would trigger the Files app document picker
                // On Android, this would show a folder picker dialog

                // Platform-specific implementation needed
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    await BrowseDriveIOS();
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    await BrowseDriveAndroid();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to browse drives: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task BrowseKeyfile()
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Keyfile",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "*/*" } },
                        { DevicePlatform.iOS, new[] { "public.data" } }
                    })
                });

                if (result != null)
                {
                    KeyfilePath = result.FullPath;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to select keyfile: {ex.Message}");
            }
        }

        [RelayCommand]
        private void DriveSelected()
        {
            OnPropertyChanged(nameof(HasSelectedDrive));
            OnPropertyChanged(nameof(DriveInfoText));
            OnPropertyChanged(nameof(CanUnlock));
            ClearError();
        }

        [RelayCommand(CanExecute = nameof(CanUnlock))]
        private async Task Unlock()
        {
#if DEBUG
            // Developer bypass for emulator testing
            if (_developerBypassMode && MobileDeveloperMode.BypassPhantomKey)
            {
                MobileDeveloperMode.Log("Using developer bypass for vault unlock");
                await UnlockWithDeveloperBypass();
                return;
            }
#endif

            if (SelectedDrive == null)
            {
                ShowError("Please select a USB vault drive");
                return;
            }

            IsBusy = true;
            HasError = false;
            StatusMessage = "Unlocking vault...";

            try
            {
                // Build path to manifest
                var manifestPath = Path.Combine(SelectedDrive.RootPath, ".phantom", "vault.manifest");

                if (!File.Exists(manifestPath))
                {
                    ShowError("No PhantomVault found on this drive. Please select a USB drive with a valid vault.");
                    return;
                }

                StatusMessage = "Verifying USB device binding...";

                // Verify USB device binding
                var deviceId = _usbBindingService.ComputeDeviceId(SelectedDrive.RootPath);
                var hiddenIdPath = Path.Combine(SelectedDrive.RootPath, ".phantom_device_id");

                if (File.Exists(hiddenIdPath))
                {
                    try
                    {
                        var storedDeviceId = _usbBindingService.ReadHiddenDeviceId(SelectedDrive.RootPath);
                        _usbBindingService.EnsureBoundToDevice(SelectedDrive.RootPath, storedDeviceId);
                    }
                    catch (Exception ex)
                    {
                        ShowError($"This vault is bound to a different USB device. Device authentication failed: {ex.Message}");
                        return;
                    }
                }

                StatusMessage = "Reading vault manifest...";

                // Read and decrypt manifest
                var keyfilePath = UseKeyfile ? KeyfilePath : null;
                VaultManifest? manifest;
                string? error;

                if (!_manifestService.TryReadManifest(
                    manifestPath,
                    Password,
                    keyfilePath,
                    out manifest,
                    out error,
                    usbSerial: SelectedDrive.DeviceIdentifier))
                {
                    ShowError(error ?? "Failed to unlock vault. Please check your password and try again.");
                    return;
                }

                if (manifest == null)
                {
                    ShowError("Vault manifest is corrupted or invalid.");
                    return;
                }

                StatusMessage = "Loading vault database...";

                // Load vault database
                var vaultDbPath = Path.Combine(SelectedDrive.RootPath, ".phantom", "vault.pvdb");

                if (!File.Exists(vaultDbPath))
                {
                    ShowError("Vault database not found on USB drive.");
                    return;
                }

                // Open vault (this would be handled by VaultService)
                // For now, just navigate to the main vault page
                StatusMessage = "Vault unlocked successfully!";
                await Task.Delay(500);

                // Navigate to vault page
                await Shell.Current.GoToAsync("//VaultPage", new Dictionary<string, object>
                {
                    { "Manifest", manifest },
                    { "VaultPath", vaultDbPath },
                    { "UsbPath", SelectedDrive.RootPath }
                });

                // Clear sensitive data
                Password = string.Empty;
                KeyfilePath = string.Empty;
            }
            catch (Exception ex)
            {
                ShowError($"Unlock failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                StatusMessage = string.Empty;
            }
        }

        [RelayCommand]
        private async Task UnlockWithBiometrics()
        {
            if (!BiometricsAvailable)
            {
                ShowError("Biometric authentication is not available on this device.");
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Waiting for biometric authentication...";

                // Platform-specific biometric authentication
                // This would use:
                // - Android: BiometricPrompt API
                // - iOS: LocalAuthentication framework (Face ID / Touch ID)

                // For now, placeholder
                var authenticated = await AuthenticateWithBiometrics();

                if (authenticated)
                {
                    // Retrieve stored password from secure storage
                    var storedPassword = await SecureStorage.GetAsync("vault_password");

                    if (!string.IsNullOrEmpty(storedPassword))
                    {
                        Password = storedPassword;
                        await Unlock();
                    }
                    else
                    {
                        ShowError("No saved password found. Please unlock manually first.");
                    }
                }
                else
                {
                    ShowError("Biometric authentication failed.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Biometric authentication error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                StatusMessage = string.Empty;
            }
        }

        #endregion

        #region Private Methods

        private void RefreshDrives()
        {
            try
            {
                AvailableDrives.Clear();

                var drives = _usbDetector.GetRemovableDrives();

                foreach (var drivePath in drives)
                {
                    var driveInfo = _usbDetector.GetDriveInfo(drivePath);
                    if (driveInfo != null)
                    {
                        AvailableDrives.Add(driveInfo);
                    }
                }

                // Auto-select first drive if only one available
                if (AvailableDrives.Count == 1)
                {
                    SelectedDrive = AvailableDrives[0];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultUnlockViewModel] Error refreshing drives: {ex.Message}");
            }
        }

        private void OnUsbDriveInserted(string drivePath)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var driveInfo = _usbDetector.GetDriveInfo(drivePath);
                if (driveInfo != null && !AvailableDrives.Any(d => d.RootPath == drivePath))
                {
                    AvailableDrives.Add(driveInfo);

                    // Auto-select if this is the only drive
                    if (AvailableDrives.Count == 1)
                    {
                        SelectedDrive = driveInfo;
                    }

                    StatusMessage = $"USB drive detected: {driveInfo.Label}";
                }
            });
        }

        private void OnUsbDriveRemoved(string drivePath)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var driveToRemove = AvailableDrives.FirstOrDefault(d => d.RootPath == drivePath);
                if (driveToRemove != null)
                {
                    AvailableDrives.Remove(driveToRemove);

                    if (SelectedDrive?.RootPath == drivePath)
                    {
                        SelectedDrive = null;
                        ShowError("Selected USB drive was removed.");
                    }
                }
            });
        }

        private async Task<bool> AuthenticateWithBiometrics()
        {
            // Platform-specific biometric authentication implementation
            // This is a placeholder - actual implementation would be in platform services
            await Task.Delay(1000); // Simulate biometric prompt
            return false; // TODO: Implement platform-specific biometrics
        }

        private async Task BrowseDriveIOS()
        {
            // iOS-specific folder picker using UIDocumentPickerViewController
            // This would be implemented in the iOS platform layer
            await Task.CompletedTask;
        }

        private async Task BrowseDriveAndroid()
        {
            // Android-specific folder picker using SAF (Storage Access Framework)
            // This would be implemented in the Android platform layer
            await Task.CompletedTask;
        }

        private async void CheckBiometricsAvailability()
        {
            try
            {
#if DEBUG
                // Skip biometric check in developer mode
                if (_developerBypassMode && MobileDeveloperMode.SkipBiometricAuth)
                {
                    BiometricsAvailable = false;
                    MobileDeveloperMode.Log("Biometric authentication skipped in developer mode");
                    return;
                }
#endif
                // Check if device supports biometrics
                // This would use platform-specific APIs
                BiometricsAvailable = false; // TODO: Implement platform check
            }
            catch
            {
                BiometricsAvailable = false;
            }

            await Task.CompletedTask;
        }

#if DEBUG
        /// <summary>
        /// Developer bypass method for emulator testing
        /// </summary>
        private async Task UnlockWithDeveloperBypass()
        {
            IsBusy = true;
            StatusMessage = "Developer Mode: Bypassing PhantomKey verification...";

            try
            {
                MobileDeveloperMode.Log("Starting developer bypass unlock");

                // Simulate USB binding check bypass
                if (MobileDeveloperMode.SkipUsbBindingCheck)
                {
                    StatusMessage = "Developer Mode: Skipping USB binding check";
                    await Task.Delay(500);
                }

                // Create/use developer vault
                var vaultPath = MobileDeveloperMode.DeveloperVaultPath;
                
                if (!Directory.Exists(vaultPath))
                {
                    Directory.CreateDirectory(vaultPath);
                    MobileDeveloperMode.Log($"Created developer vault at: {vaultPath}");
                }

                StatusMessage = "Developer Mode: Vault unlocked successfully!";
                await Task.Delay(1000);

                // TODO: Navigate to vault view with developer credentials
                MobileDeveloperMode.Log("Developer bypass unlock completed");

                // For now, show success message
                StatusMessage = $"Developer vault ready at: {Path.GetFileName(vaultPath)}";
            }
            catch (Exception ex)
            {
                MobileDeveloperMode.Log($"Developer bypass failed: {ex.Message}");
                ShowError($"Developer mode unlock failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
#endif

        private void ShowError(string message)
        {
            ErrorMessage = message;
            HasError = true;
            IsBusy = false;
            StatusMessage = string.Empty;
        }

        private void ClearError()
        {
            ErrorMessage = string.Empty;
            HasError = false;
        }

        #endregion

        public void Dispose()
        {
            _usbDetector.RemovableDriveInserted -= OnUsbDriveInserted;
            _usbDetector.RemovableDriveRemoved -= OnUsbDriveRemoved;
        }
    }
}
