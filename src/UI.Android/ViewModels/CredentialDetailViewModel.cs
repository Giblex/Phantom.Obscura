using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the credential detail view.
    /// Handles copy-to-clipboard, TOTP generation, and reveal/hide password.
    /// </summary>
    public sealed partial class CredentialDetailViewModel : BaseViewModel
    {
        private readonly TotpService _totpService;

        [ObservableProperty]
        private Credential? _credential;

        [ObservableProperty]
        private bool _passwordVisible;

        [ObservableProperty]
        private string? _totpCode;

        [ObservableProperty]
        private int _totpSecondsRemaining;

        private System.Timers.Timer? _totpTimer;

        public CredentialDetailViewModel(TotpService totpService)
        {
            _totpService = totpService;
        }

        partial void OnCredentialChanged(Credential? value)
        {
            PasswordVisible = false;
            TotpCode = null;
            _totpTimer?.Stop();
            _totpTimer?.Dispose();

            if (value?.EntryType == EntryType.TotpGenerator && value.TotpSecret != null)
                StartTotpRefresh(value.TotpSecret);
        }

        private void StartTotpRefresh(string secret)
        {
            RefreshTotp(secret);
            _totpTimer = new System.Timers.Timer(1000);
            _totpTimer.Elapsed += (_, _) => RefreshTotp(secret);
            _totpTimer.Start();
        }

        private void RefreshTotp(string secret)
        {
            var now = DateTimeOffset.UtcNow;
            TotpCode = _totpService.GenerateCode(secret);
            TotpSecondsRemaining = 30 - (int)(now.ToUnixTimeSeconds() % 30);
        }

        [RelayCommand]
        private async Task EditAsync()
        {
            if (Credential is null) return;
            await Shell.Current.GoToAsync("edit", new System.Collections.Generic.Dictionary<string, object>
            {
                ["credential"] = Credential,
                ["isEdit"] = true
            });
        }

        [RelayCommand]
        private void TogglePasswordVisibility() => PasswordVisible = !PasswordVisible;

        [RelayCommand]
        private async Task CopyUsernameAsync()
        {
            if (Credential?.Username is null) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(Credential.Username);
            StatusMessage = "Username copied";
            await ClearClipboardAfterDelayAsync();
        }

        [RelayCommand]
        private async Task CopyPasswordAsync()
        {
            if (Credential?.Password is null) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(Credential.Password);
            StatusMessage = "Password copied — clears in 30s";
            await ClearClipboardAfterDelayAsync();
        }

        [RelayCommand]
        private async Task CopyTotpAsync()
        {
            if (TotpCode is null) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(TotpCode);
            StatusMessage = "TOTP copied";
        }

        [RelayCommand]
        private async Task CopyUrlAsync()
        {
            if (Credential?.Url is null) return;
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(Credential.Url);
            StatusMessage = "URL copied";
        }

        private static async Task ClearClipboardAfterDelayAsync()
        {
            await Task.Delay(30_000);
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(string.Empty);
        }
    }
}
