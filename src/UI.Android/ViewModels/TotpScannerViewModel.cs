using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the TOTP secret scanner / manual entry page.
    /// Parses otpauth:// URIs and populates a new TotpGenerator credential.
    /// Camera-based QR scanning can be layered on top by setting OtpAuthUri from the scanner result.
    /// </summary>
    public sealed partial class TotpScannerViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vaultVm;
        private readonly TotpService _totpService;

        [ObservableProperty] private string _otpAuthUri = string.Empty;
        [ObservableProperty] private string _manualSecret = string.Empty;
        [ObservableProperty] private string _manualIssuer = string.Empty;
        [ObservableProperty] private string _manualAccount = string.Empty;
        [ObservableProperty] private string _previewCode = string.Empty;
        [ObservableProperty] private bool _secretValid;

        public TotpScannerViewModel(VaultViewModel vaultVm, TotpService totpService)
        {
            _vaultVm = vaultVm;
            _totpService = totpService;
        }

        partial void OnManualSecretChanged(string value)
        {
            PreviewCode = string.Empty;
            SecretValid = false;
            if (string.IsNullOrWhiteSpace(value)) return;
            try
            {
                var code = _totpService.GenerateCode(value.Trim().ToUpperInvariant());
                PreviewCode = code;
                SecretValid = true;
            }
            catch { /* invalid base32 */ }
        }

        partial void OnOtpAuthUriChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            ParseOtpAuthUri(value.Trim());
        }

        private void ParseOtpAuthUri(string uri)
        {
            // otpauth://totp/Issuer:account?secret=BASE32&issuer=X&digits=6&period=30
            try
            {
                if (!uri.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase)) return;
                var rest = uri["otpauth://totp/".Length..];
                var qIdx = rest.IndexOf('?');
                var labelPart = qIdx >= 0 ? Uri.UnescapeDataString(rest[..qIdx]) : rest;
                var query = qIdx >= 0 ? rest[(qIdx + 1)..] : string.Empty;

                // Parse label
                var colonIdx = labelPart.IndexOf(':');
                if (colonIdx >= 0)
                {
                    ManualIssuer = labelPart[..colonIdx].Trim();
                    ManualAccount = labelPart[(colonIdx + 1)..].Trim();
                }
                else
                {
                    ManualAccount = labelPart.Trim();
                }

                // Parse query params
                foreach (var param in query.Split('&'))
                {
                    var eq = param.IndexOf('=');
                    if (eq < 0) continue;
                    var key = param[..eq].ToLowerInvariant();
                    var val = Uri.UnescapeDataString(param[(eq + 1)..]);
                    if (key == "secret") ManualSecret = val;
                    else if (key == "issuer" && string.IsNullOrEmpty(ManualIssuer)) ManualIssuer = val;
                }
            }
            catch { /* malformed URI */ }
        }

        [RelayCommand(CanExecute = nameof(SecretValid))]
        private void AddToVault()
        {
            var credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                EntryType = EntryType.TotpGenerator,
                Title = string.IsNullOrWhiteSpace(ManualIssuer) ? ManualAccount : ManualIssuer,
                TotpSecret = ManualSecret.Trim().ToUpperInvariant(),
                TotpIssuer = ManualIssuer,
                TotpAccountName = ManualAccount,
                TotpDigits = 6,
                TotpTimeStep = 30,
                TotpAlgorithm = "SHA1",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow
            };

            _vaultVm.UpsertCredential(credential);
            StatusMessage = $"TOTP for \"{credential.Title}\" added to vault";

            // Reset form
            ManualSecret = ManualIssuer = ManualAccount = OtpAuthUri = string.Empty;
            PreviewCode = string.Empty;
            SecretValid = false;
        }

        partial void OnSecretValidChanged(bool value) => AddToVaultCommand.NotifyCanExecuteChanged();
    }
}
