using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class TotpSettingsViewModel : BaseViewModel
    {
        private readonly TotpService _totpService;

        [ObservableProperty] private bool _isTotpEnabled;
        [ObservableProperty] private bool _hasTotpSecret;
        [ObservableProperty] private string _totpSecret = string.Empty;
        [ObservableProperty] private string _testCode = string.Empty;

        public TotpSettingsViewModel(TotpService totpService) => _totpService = totpService;

        [RelayCommand]
        private async Task GenerateTotpSecretAsync()
        {
            await RunSafeAsync(async () =>
            {
                await Task.Delay(200);
                TotpSecret = TotpService.GenerateSecret(20);
                HasTotpSecret = true;
                IsTotpEnabled = true;
                StatusMessage = "Secret generated — scan it into your authenticator app.";
            }, "Generating…");
        }

        [RelayCommand]
        private async Task RemoveTotpSecretAsync()
        {
            bool confirmed = await Shell.Current.DisplayAlert("Remove TOTP",
                "Remove TOTP configuration from this vault?", "Remove", "Cancel");
            if (!confirmed) return;
            TotpSecret = string.Empty;
            TestCode = string.Empty;
            HasTotpSecret = false;
            IsTotpEnabled = false;
            StatusMessage = "TOTP configuration removed.";
        }

        [RelayCommand]
        private async Task VerifyTotpCodeAsync()
        {
            if (string.IsNullOrEmpty(TotpSecret)) { ErrorMessage = "No secret configured."; return; }
            if (string.IsNullOrWhiteSpace(TestCode)) { ErrorMessage = "Enter a 6-digit code."; return; }
            await RunSafeAsync(async () =>
            {
                await Task.Delay(100);
                var expected = _totpService.GenerateCode(TotpSecret);
                StatusMessage = expected == TestCode.Trim()
                    ? "✅ TOTP code verified successfully!"
                    : "❌ Invalid code — check your authenticator app.";
            }, "Verifying…");
        }

        [RelayCommand]
        private async Task CopySecretAsync()
        {
            if (string.IsNullOrEmpty(TotpSecret)) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(TotpSecret);
            StatusMessage = "Secret copied to clipboard — will be cleared after 30s.";
            _ = Task.Delay(30_000).ContinueWith(async _ =>
            {
                var current = await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.GetTextAsync();
                if (current == TotpSecret)
                    await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(string.Empty);
            });
        }
    }
}
