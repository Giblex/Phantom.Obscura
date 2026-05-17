using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Android.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run vault creation wizard.
    /// </summary>
    public sealed partial class SetupWizardViewModel : BaseViewModel
    {
        private readonly MobileVaultService _vaultService;
        private readonly UsbVaultLocator _locator;

        [ObservableProperty]
        private string _vaultName = "My Vault";

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private bool _passwordVisible;

        [ObservableProperty]
        private string _targetDriveLabel = string.Empty;

        public event System.Action? VaultCreated;

        public SetupWizardViewModel(MobileVaultService vaultService, UsbVaultLocator locator)
        {
            _vaultService = vaultService;
            _locator = locator;
            RefreshDriveContext();
            _locator.StateChanged += _ => RefreshDriveContext();
        }

        private void RefreshDriveContext()
        {
            var root = _locator.CurrentDriveRoot;
            _vaultService.SetDriveRoot(root);
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                TargetDriveLabel = root ?? string.Empty;
                CreateVaultCommand.NotifyCanExecuteChanged();
            });
        }

        partial void OnPasswordChanged(string value) => CreateVaultCommand.NotifyCanExecuteChanged();
        partial void OnConfirmPasswordChanged(string value) => CreateVaultCommand.NotifyCanExecuteChanged();
        partial void OnVaultNameChanged(string value) => CreateVaultCommand.NotifyCanExecuteChanged();

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task CreateVaultAsync()
        {
            if (Password != ConfirmPassword)
            {
                StatusMessage = "Passwords do not match.";
                return;
            }

            await RunSafeAsync(async () =>
            {
                // Ensure the vault service is pointed at the freshly-mounted drive,
                // create the vault on it, and bind THIS device so subsequent unlocks
                // are authorised automatically.
                _vaultService.SetDriveRoot(_locator.CurrentDriveRoot);
                await _vaultService.CreateVaultAsync(VaultName, Password);
                if (_locator.CurrentDriveRoot is { } root)
                    _locator.BindCurrentDevice(root);

                VaultCreated?.Invoke();
            }, "Creating vault on USB drive…");
        }

        [RelayCommand]
        private void TogglePasswordVisibility() => PasswordVisible = !PasswordVisible;

        private bool CanCreate()
            => !IsBusy
               && _locator.CurrentDriveRoot is not null
               && !string.IsNullOrWhiteSpace(VaultName)
               && !string.IsNullOrEmpty(Password)
               && !string.IsNullOrEmpty(ConfirmPassword);
    }
}
