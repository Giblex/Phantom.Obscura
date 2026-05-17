using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class RecoveryViewModel : BaseViewModel
    {
        private readonly RecoveryCodeService _recoveryCodeService;

        [ObservableProperty]
        private List<string> _recoveryCodes = new();

        [ObservableProperty]
        private bool _codesGenerated;

        [ObservableProperty]
        private string _recoveryCodeInput = string.Empty;

        [ObservableProperty]
        private bool _verifyMode;

        public RecoveryViewModel(RecoveryCodeService recoveryCodeService)
        {
            _recoveryCodeService = recoveryCodeService;
        }

        [RelayCommand]
        private void GenerateCodes()
        {
            var codes = _recoveryCodeService.GenerateRecoveryCodes(10);
            RecoveryCodes = codes.ToList();
            CodesGenerated = true;
            StatusMessage = "Store these codes somewhere safe. Each code can only be used once.";
        }

        [RelayCommand]
        private async Task ShareCodesAsync()
        {
            if (RecoveryCodes.Count == 0) return;

            var text = "Phantom Obscura Recovery Codes\n" +
                       "================================\n" +
                       string.Join("\n", RecoveryCodes) +
                       "\n\nKeep these codes in a secure location.";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Phantom Obscura Recovery Codes",
                Text = text
            });
        }

        [RelayCommand]
        private async Task CopyAllCodesAsync()
        {
            if (RecoveryCodes.Count == 0) return;
            var text = string.Join("\n", RecoveryCodes);
            await Clipboard.Default.SetTextAsync(text);
            StatusMessage = "Codes copied to clipboard — remember to clear it!";
        }

        [RelayCommand]
        private void ToggleVerifyMode()
        {
            VerifyMode = !VerifyMode;
            RecoveryCodeInput = string.Empty;
            StatusMessage = string.Empty;
        }
    }
}
