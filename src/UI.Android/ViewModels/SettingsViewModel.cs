using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// Settings hub ViewModel.
    /// Houses sub-section flags and links to security / autofill sub-settings.
    /// </summary>
    public sealed partial class SettingsViewModel : BaseViewModel
    {
        private readonly IdleLockService _idleLockService;

        [ObservableProperty]
        private int _autoLockMinutes = 5;

        [ObservableProperty]
        private bool _biometricEnabled;

        [ObservableProperty]
        private bool _clipboardClearEnabled = true;

        [ObservableProperty]
        private int _clipboardClearSeconds = 30;

        [ObservableProperty]
        private string _appVersion = "1.0.0";

        public SettingsViewModel(IdleLockService idleLockService)
        {
            _idleLockService = idleLockService;
            AppVersion = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }

        partial void OnAutoLockMinutesChanged(int value)
        {
            // IdleLockService timeout is set at construction; restart with new timeout if needed
            _ = value; // timeout change requires re-injection; handled by Settings save action
        }

        [RelayCommand]
        private async Task OpenSecuritySettingsAsync()
            => await (Shell.Current?.GoToAsync("security") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenAutofillSettingsAsync()
            => await (Shell.Current?.GoToAsync("autofill") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenImportExportAsync()
            => await (Shell.Current?.GoToAsync("importexport") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenPasswordGeneratorAsync()
            => await (Shell.Current?.GoToAsync("password-generator") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenPasswordHealthAsync()
            => await (Shell.Current?.GoToAsync("password-health") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenRecoveryAsync()
            => await (Shell.Current?.GoToAsync("recovery") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenTrashAsync()
            => await (Shell.Current?.GoToAsync("trash") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenCategoriesAsync()
            => await (Shell.Current?.GoToAsync("categories") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenDuplicateScanAsync()
            => await (Shell.Current?.GoToAsync("duplicate-scan") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenTotpScannerAsync()
            => await (Shell.Current?.GoToAsync("totp-scanner") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenPasskeyManagementAsync()
            => await (Shell.Current?.GoToAsync("passkeys") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task ClearClipboardNowAsync()
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(string.Empty);
            StatusMessage = "Clipboard cleared";
        }

        [RelayCommand]
        private async Task OpenVaultSettingsAsync()
            => await (Shell.Current?.GoToAsync("vault-settings") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenTotpSettingsAsync()
            => await (Shell.Current?.GoToAsync("totp-settings") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenBackupSnapshotsAsync()
            => await (Shell.Current?.GoToAsync("backup-snapshots") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenSecurityDashboardAsync()
            => await (Shell.Current?.GoToAsync("security-dashboard") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenShareAsync()
            => await (Shell.Current?.GoToAsync("share") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenThemeSettingsAsync()
            => await (Shell.Current?.GoToAsync("theme-settings") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenAboutAsync()
            => await (Shell.Current?.GoToAsync("about") ?? Task.CompletedTask);

        [RelayCommand]
        private async Task OpenAccessibilityAsync()
            => await (Shell.Current?.GoToAsync("accessibility") ?? Task.CompletedTask);
    }
}
