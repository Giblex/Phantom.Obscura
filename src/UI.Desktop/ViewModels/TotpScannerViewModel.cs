using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Web;
using ReactiveUI;
using Avalonia.Media;
using PhantomObscuraV6.UI.Desktop.Services;
using ZXing;
using ZXing.Common;

namespace PhantomVault.UI.ViewModels
{
    public class TotpScannerViewModel : ReactiveObject
    {
        private string _issuer = string.Empty;
        private string _accountName = string.Empty;
        private string _secretKey = string.Empty;
        private int _digits = 6;
        private int _period = 30;
        private string _algorithm = "SHA1";
        private string _qrScanStatus = string.Empty;
        private string _secretValidationMessage = string.Empty;
        private IBrush _secretValidationColor = Brushes.Red;
        private string _previewCode = "------";
        private int _previewTimeRemaining = 0;
        private bool _linkToCurrentEntry = true;
        private bool _canSave = false;
        private Timer? _previewTimer;

        public event EventHandler<TotpScanResult?>? CloseRequested;

        private bool _isEditing;

        public TotpScannerViewModel()
        {
            StartCameraCommand = ReactiveCommand.Create(StartCamera);
            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
            DeleteTotpCommand = ReactiveCommand.Create(DeleteTotp);

            this.WhenAnyValue(x => x.SecretKey)
                .Subscribe(_ => ValidateAndUpdatePreview());
        }

        public ReactiveCommand<Unit, Unit> StartCameraCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteTotpCommand { get; }

        public bool IsEditing
        {
            get => _isEditing;
            set => this.RaiseAndSetIfChanged(ref _isEditing, value);
        }

        public string Issuer
        {
            get => _issuer;
            set => this.RaiseAndSetIfChanged(ref _issuer, value);
        }

        public string AccountName
        {
            get => _accountName;
            set => this.RaiseAndSetIfChanged(ref _accountName, value);
        }

        public string SecretKey
        {
            get => _secretKey;
            set
            {

                var normalized = value?.Replace(" ", "").Replace("-", "").ToUpperInvariant() ?? string.Empty;
                this.RaiseAndSetIfChanged(ref _secretKey, normalized);
            }
        }

        public int Digits
        {
            get => _digits;
            set
            {
                this.RaiseAndSetIfChanged(ref _digits, value);
                ValidateAndUpdatePreview();
            }
        }

        public int Period
        {
            get => _period;
            set
            {
                this.RaiseAndSetIfChanged(ref _period, value);
                ValidateAndUpdatePreview();
            }
        }

        public string Algorithm
        {
            get => _algorithm;
            set
            {
                this.RaiseAndSetIfChanged(ref _algorithm, value);
                ValidateAndUpdatePreview();
            }
        }

        public string QrScanStatus
        {
            get => _qrScanStatus;
            private set
            {
                this.RaiseAndSetIfChanged(ref _qrScanStatus, value);
                this.RaisePropertyChanged(nameof(HasQrScanStatus));
            }
        }

        public bool HasQrScanStatus => !string.IsNullOrWhiteSpace(QrScanStatus);

        public string SecretValidationMessage
        {
            get => _secretValidationMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _secretValidationMessage, value);
                this.RaisePropertyChanged(nameof(HasSecretValidationMessage));
            }
        }

        public IBrush SecretValidationColor
        {
            get => _secretValidationColor;
            private set => this.RaiseAndSetIfChanged(ref _secretValidationColor, value);
        }

        public bool HasSecretValidationMessage => !string.IsNullOrWhiteSpace(SecretValidationMessage);

        public string PreviewCode
        {
            get => _previewCode;
            private set => this.RaiseAndSetIfChanged(ref _previewCode, value);
        }

        public int PreviewTimeRemaining
        {
            get => _previewTimeRemaining;
            private set => this.RaiseAndSetIfChanged(ref _previewTimeRemaining, value);
        }

        public bool HasValidSecret => !string.IsNullOrWhiteSpace(SecretKey) && TotpIntegrationHelper.IsValidSecret(SecretKey);

        public bool LinkToCurrentEntry
        {
            get => _linkToCurrentEntry;
            set => this.RaiseAndSetIfChanged(ref _linkToCurrentEntry, value);
        }

        public bool CanSave
        {
            get => _canSave;
            private set => this.RaiseAndSetIfChanged(ref _canSave, value);
        }

        private async void StartCamera()
        {
            QrScanStatus = "Camera scanning feature available. For now, please scan QR with mobile app and enter details manually.";

        }

        private void ParseOtpAuthUrl(string url)
        {
            try
            {
                if (!url.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase))
                {
                    QrScanStatus = "Invalid QR code format. Expected TOTP authenticator code.";
                    return;
                }

                var uri = new Uri(url);
                var path = uri.LocalPath.TrimStart('/');

                if (path.Contains(':'))
                {
                    var parts = path.Split(':');
                    Issuer = Uri.UnescapeDataString(parts[0]);
                    AccountName = Uri.UnescapeDataString(parts[1]);
                }
                else
                {
                    AccountName = Uri.UnescapeDataString(path);
                }

                var query = HttpUtility.ParseQueryString(uri.Query);

                var secret = query["secret"];
                if (!string.IsNullOrEmpty(secret))
                {
                    SecretKey = secret;
                }

                if (int.TryParse(query["digits"], out var digits))
                {
                    Digits = digits;
                }

                if (int.TryParse(query["period"], out var period))
                {
                    Period = period;
                }

                var algorithm = query["algorithm"];
                if (!string.IsNullOrEmpty(algorithm))
                {
                    Algorithm = algorithm.ToUpper();
                }

                var issuerParam = query["issuer"];
                if (!string.IsNullOrEmpty(issuerParam) && string.IsNullOrEmpty(Issuer))
                {
                    Issuer = Uri.UnescapeDataString(issuerParam);
                }

                QrScanStatus = $"✓ Successfully scanned TOTP for {Issuer}";
                ValidateAndUpdatePreview();
            }
            catch (Exception ex)
            {
                QrScanStatus = $"Failed to parse QR code: {ex.Message}";
            }
        }

        private void ValidateAndUpdatePreview()
        {
            if (string.IsNullOrWhiteSpace(SecretKey))
            {
                SecretValidationMessage = string.Empty;
                CanSave = false;
                PreviewCode = "------";
                _previewTimer?.Dispose();
                _previewTimer = null;
                this.RaisePropertyChanged(nameof(HasValidSecret));
                return;
            }

            if (!TotpIntegrationHelper.IsValidSecret(SecretKey))
            {
                SecretValidationMessage = "❌ Invalid secret key format. Must be Base32 (A-Z, 2-7).";
                SecretValidationColor = Brushes.Red;
                CanSave = false;
                PreviewCode = "------";
                _previewTimer?.Dispose();
                _previewTimer = null;
                this.RaisePropertyChanged(nameof(HasValidSecret));
                return;
            }

            SecretValidationMessage = "✓ Valid secret key";
            SecretValidationColor = Brushes.Green;
            CanSave = true;
            this.RaisePropertyChanged(nameof(HasValidSecret));

            UpdatePreview();
            _previewTimer?.Dispose();
            _previewTimer = new Timer(_ => UpdatePreview(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void UpdatePreview()
        {
            try
            {
                var code = TotpIntegrationHelper.GenerateCode(SecretKey, Period, Digits, Algorithm);
                PreviewCode = FormatCode(code, Digits);
                PreviewTimeRemaining = TotpIntegrationHelper.GetRemainingSeconds(Period);
            }
            catch
            {
                PreviewCode = "ERROR";
                PreviewTimeRemaining = 0;
            }
        }

        private string FormatCode(string code, int digits)
        {

            if (code.Length <= 3)
                return code;

            var mid = code.Length / 2;
            return $"{code.Substring(0, mid)} {code.Substring(mid)}";
        }

        private void Save()
        {
            var result = new TotpScanResult
            {
                Success = true,
                Issuer = Issuer,
                AccountName = AccountName,
                Secret = SecretKey,
                Digits = Digits,
                Period = Period,
                Algorithm = Algorithm,
                LinkToCurrentEntry = LinkToCurrentEntry
            };

            _previewTimer?.Dispose();
            _previewTimer = null;
            CloseRequested?.Invoke(this, result);
        }

        private void Cancel()
        {
            _previewTimer?.Dispose();
            _previewTimer = null;
            CloseRequested?.Invoke(this, null);
        }

        private void DeleteTotp()
        {
            var result = new TotpScanResult
            {
                Success = true,
                Deleted = true
            };

            _previewTimer?.Dispose();
            _previewTimer = null;
            CloseRequested?.Invoke(this, result);
        }

        public void Dispose()
        {
            _previewTimer?.Dispose();
            _previewTimer = null;
        }
    }

    public class TotpScanResult
    {
        public bool Success { get; set; }
        public bool Deleted { get; set; }
        public string Issuer { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public int Digits { get; set; } = 6;
        public int Period { get; set; } = 30;
        public string Algorithm { get; set; } = "SHA1";
        public bool LinkToCurrentEntry { get; set; } = true;
    }
}

