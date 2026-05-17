using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Platform.Android;
using PhantomVault.Android.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the vault unlock screen.
    /// Uses <see cref="MobileVaultService"/> to decrypt and load the vault database.
    /// </summary>
    public sealed partial class UnlockViewModel : BaseViewModel
    {
        private readonly MobileVaultService _vaultService;
        private readonly UsbVaultLocator _locator;
        private readonly AndroidBiometricService? _biometricService;

        [ObservableProperty]
        private string _masterPassword = string.Empty;

        [ObservableProperty]
        private bool _biometricAvailable;

        [ObservableProperty]
        private bool _isUnlocked;

        [ObservableProperty]
        private bool _vaultExists;

        [ObservableProperty]
        private bool _isDeviceBound;

        [ObservableProperty]
        private string _driveLabel = string.Empty;

        public event Action<VaultDatabase, string>? VaultOpened;
        /// <summary>Raised when the USB drive disappears or no vault exists on it — return to gate.</summary>
        public event Action? GateLost;

        public UnlockViewModel(
            MobileVaultService vaultService,
            UsbVaultLocator locator,
            AndroidBiometricService? biometricService = null)
        {
            _vaultService = vaultService;
            _locator = locator;
            _biometricService = biometricService;
            BiometricAvailable = biometricService?.IsBiometricAvailable ?? false;

            RefreshDriveContext();
            _locator.StateChanged += _ => RefreshDriveContext();
        }

        private void RefreshDriveContext()
        {
            var root = _locator.CurrentDriveRoot;
            _vaultService.SetDriveRoot(root);
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                DriveLabel    = root ?? string.Empty;
                VaultExists   = _vaultService.VaultExists();
                IsDeviceBound = root is not null && _locator.IsCurrentDeviceBound(root);
                UnlockCommand.NotifyCanExecuteChanged();
                if (root is null || !VaultExists)
                    GateLost?.Invoke();
            });
        }

        [RelayCommand(CanExecute = nameof(CanUnlock))]
        private async Task UnlockAsync()
        {
            await RunSafeAsync(async () =>
            {
                _vaultService.SetDriveRoot(_locator.CurrentDriveRoot);
                var db = await _vaultService.OpenVaultAsync(MasterPassword);

                // First successful unlock on this device → add it to the drive's
                // multi-device allowlist so the welcome gate auto-advances next time.
                if (_locator.CurrentDriveRoot is { } root && !_locator.IsCurrentDeviceBound(root))
                {
                    _locator.BindCurrentDevice(root);
                    IsDeviceBound = true;
                }

                IsUnlocked = true;
                var pw = MasterPassword;
                MasterPassword = string.Empty;
                VaultOpened?.Invoke(db, pw);
            }, "Unlocking…");
        }

        partial void OnMasterPasswordChanged(string value) => UnlockCommand.NotifyCanExecuteChanged();

        private bool CanUnlock() =>
            !IsBusy
            && _locator.CurrentDriveRoot is not null
            && _vaultService.VaultExists()
            && !string.IsNullOrEmpty(MasterPassword);

        [RelayCommand]
        private async Task UnlockWithBiometricAsync()
        {
            if (_biometricService is null) return;
            await RunSafeAsync(async () =>
            {
                var credId = LoadStoredBiometricCredentialId();
                if (credId is null)
                    throw new InvalidOperationException("No biometric credential enrolled for this vault.");

                var challenge = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                var ok = await _biometricService.AuthenticateAsync(credId, "phantomvault.local", challenge);
                if (!ok)
                    throw new UnauthorizedAccessException("Biometric authentication failed.");

                // Biometric auth succeeded — prompt for password or retrieve from keystore
                StatusMessage = "Biometric verified. Enter master password to decrypt.";
            }, "Verifying biometric…");
        }

        private static byte[]? LoadStoredBiometricCredentialId()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomObscura", "biometric_cred.bin");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }
}
