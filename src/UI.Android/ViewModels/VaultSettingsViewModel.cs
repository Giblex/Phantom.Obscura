using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Android.Services;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class VaultSettingsViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vault;
        private readonly MobileVaultService _vaultService;

        [ObservableProperty] private string _currentVaultName = string.Empty;
        [ObservableProperty] private string _vaultCreated = string.Empty;
        [ObservableProperty] private int _credentialCount;
        [ObservableProperty] private string _newVaultName = string.Empty;
        [ObservableProperty] private string _currentPassword = string.Empty;
        [ObservableProperty] private string _newPassword = string.Empty;
        [ObservableProperty] private string _confirmPassword = string.Empty;

        private string _masterPassword = string.Empty;

        public VaultSettingsViewModel(VaultViewModel vault, MobileVaultService vaultService)
        {
            _vault = vault;
            _vaultService = vaultService;
            Refresh();
        }

        public void SetMasterPassword(string mp) => _masterPassword = mp;

        public void Refresh()
        {
            CurrentVaultName = _vault.VaultName;
            CredentialCount = _vault.Credentials.Count;
        }

        [RelayCommand]
        private async Task RenameVaultAsync()
        {
            if (string.IsNullOrWhiteSpace(NewVaultName))
            {
                ErrorMessage = "Please enter a vault name.";
                return;
            }
            await RunSafeAsync(async () =>
            {
                var db = await _vaultService.OpenVaultAsync(_masterPassword);
                db.VaultName = NewVaultName.Trim();
                await _vaultService.SaveVaultAsync(db, _masterPassword);
                _vault.LoadCredentials(db);
                CurrentVaultName = db.VaultName;
                NewVaultName = string.Empty;
                StatusMessage = "Vault renamed successfully.";
            }, "Renaming…");
        }

        [RelayCommand]
        private async Task ChangePasswordAsync()
        {
            if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword))
            {
                ErrorMessage = "All password fields are required.";
                return;
            }
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "New passwords do not match.";
                return;
            }
            await RunSafeAsync(async () =>
            {
                var db = await _vaultService.OpenVaultAsync(CurrentPassword);
                await _vaultService.SaveVaultAsync(db, NewPassword);
                _masterPassword = NewPassword;
                CurrentPassword = string.Empty;
                NewPassword = string.Empty;
                ConfirmPassword = string.Empty;
                StatusMessage = "Master password changed successfully.";
            }, "Changing password…");
        }

        [RelayCommand]
        private async Task DeleteVaultAsync()
        {
            bool confirmed = await Shell.Current.DisplayAlert(
                "Delete Vault",
                "This will permanently delete all vault data. This cannot be undone. Are you sure?",
                "Delete", "Cancel");
            if (!confirmed) return;

            await RunSafeAsync(async () =>
            {
                await Task.Run(() =>
                {
                    if (System.IO.File.Exists(_vaultService.VaultFilePath))
                        System.IO.File.Delete(_vaultService.VaultFilePath);
                });
                _vault.ClearVault();
                await Shell.Current.GoToAsync("//unlock");
            }, "Deleting vault…");
        }
    }
}
