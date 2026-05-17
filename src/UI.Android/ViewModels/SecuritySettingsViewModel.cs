using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services.Platform.Android;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class SecuritySettingsViewModel : BaseViewModel
    {
        private readonly AndroidBiometricService _biometricService;
        private readonly AndroidKeystoreService _keystoreService;

        [ObservableProperty]
        private bool _biometricAvailable;

        [ObservableProperty]
        private bool _biometricEnabled;

        [ObservableProperty]
        private bool _keystoreHardwareBacked;

        [ObservableProperty]
        private string _keystoreInfo = string.Empty;

        [ObservableProperty]
        private bool _screenCaptureBlocked = true;

        [ObservableProperty]
        private bool _clipboardProtectionEnabled = true;

        public SecuritySettingsViewModel(AndroidBiometricService biometricService, AndroidKeystoreService keystoreService)
        {
            _biometricService = biometricService;
            _keystoreService = keystoreService;
        }

        public Task InitializeAsync()
        {
            BiometricAvailable = _biometricService.IsBiometricAvailable;
            return Task.CompletedTask;
        }

        [RelayCommand]
        private void OpenSystemBiometricSettings()
        {
#if ANDROID
            try
            {
                var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionSecuritySettings);
                intent.SetFlags(global::Android.Content.ActivityFlags.NewTask);
                global::Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open settings: {ex.Message}";
            }
#endif
        }
    }
}
