using System;
using System.Reactive;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{

    public sealed class TotpSettingsViewModel : ReactiveObject
    {
        private readonly TotpService? _totpService;
        private bool _isTotpEnabled;
        private bool _hasTotpSecret;
        private string _totpSecret = string.Empty;
        private string _qrCodeData = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isBusy;
        private string _testCode = string.Empty;
        private string _vaultName = "PhantomVault";
        private Window? _ownerWindow;

        public TotpSettingsViewModel(TotpService? totpService = null)
        {
            _totpService = totpService;

            GenerateTotpSecretCommand = ReactiveCommand.CreateFromTask(GenerateTotpSecret);
            RemoveTotpSecretCommand = ReactiveCommand.CreateFromTask(RemoveTotpSecret);
            VerifyTotpCodeCommand = ReactiveCommand.CreateFromTask(VerifyTotpCode);
            CopySecretCommand = ReactiveCommand.Create(CopySecret);
        }

        public bool IsTotpEnabled
        {
            get => _isTotpEnabled;
            set => this.RaiseAndSetIfChanged(ref _isTotpEnabled, value);
        }

        public bool HasTotpSecret
        {
            get => _hasTotpSecret;
            private set => this.RaiseAndSetIfChanged(ref _hasTotpSecret, value);
        }

        public string TotpSecret
        {
            get => _totpSecret;
            private set => this.RaiseAndSetIfChanged(ref _totpSecret, value);
        }

        public string QrCodeData
        {
            get => _qrCodeData;
            private set => this.RaiseAndSetIfChanged(ref _qrCodeData, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string TestCode
        {
            get => _testCode;
            set => this.RaiseAndSetIfChanged(ref _testCode, value);
        }

        public string VaultName
        {
            get => _vaultName;
            set => this.RaiseAndSetIfChanged(ref _vaultName, value);
        }

        public ReactiveCommand<Unit, Unit> GenerateTotpSecretCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveTotpSecretCommand { get; }
        public ReactiveCommand<Unit, Unit> VerifyTotpCodeCommand { get; }
        public ReactiveCommand<Unit, Unit> CopySecretCommand { get; }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        private async Task GenerateTotpSecret()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Generating TOTP secret...";

                await Task.Delay(500);

                TotpSecret = TotpService.GenerateSecret(20);

                QrCodeData = $"otpauth://totp/PhantomVault:{VaultName}?secret={TotpSecret}&issuer=PhantomVault";

                HasTotpSecret = true;
                // Generating a seed is not enrollment. It becomes enabled only after a
                // code from the authenticator has been verified below.
                IsTotpEnabled = false;
                StatusMessage = "TOTP secret generated. Scan it, then verify a 6-digit code.";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[TotpSettings] Failed to generate a TOTP secret.");
                StatusMessage = "A TOTP secret could not be generated. Try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RemoveTotpSecret()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Removing TOTP configuration...";

                await Task.Delay(500);

                TotpSecret = string.Empty;
                QrCodeData = string.Empty;
                HasTotpSecret = false;
                IsTotpEnabled = false;
                StatusMessage = "TOTP configuration removed";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[TotpSettings] Failed to remove TOTP.");
                StatusMessage = "TOTP could not be removed. Try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task VerifyTotpCode()
        {
            System.Diagnostics.Debug.WriteLine($"[TOTP-VERIFY] VerifyTotpCode called. TotpSecret='{TotpSecret?.Length}chars', TestCode='{TestCode}', _totpService={(_totpService != null ? "OK" : "NULL")}");
            try
            {
                IsBusy = true;
                StatusMessage = "Verifying TOTP code...";

                await Task.Delay(500);

                if (string.IsNullOrEmpty(TotpSecret))
                {
                    StatusMessage = "✗ No TOTP secret configured. Generate a secret first.";
                    return;
                }

                if (string.IsNullOrEmpty(TestCode))
                {
                    StatusMessage = "✗ Please enter a valid 6-digit code.";
                    return;
                }

                var service = _totpService ?? new PhantomVault.Core.Services.TotpService();
                string expectedCode = service.GenerateCode(TotpSecret);
                bool isValid = expectedCode == TestCode.Trim();

                if (isValid)
                {
                    StatusMessage = "✓ TOTP code verified successfully!";
                    IsTotpEnabled = true;
                }
                else
                {
                    StatusMessage = "✗ Invalid TOTP code. Please try again.";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[TotpSettings] TOTP verification failed unexpectedly.");
                StatusMessage = "The authentication code could not be verified. Check the code and device time, then try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void CopySecret()
        {
            try
            {
                if (string.IsNullOrEmpty(TotpSecret))
                {
                    StatusMessage = "No secret to copy";
                    return;
                }

                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                var clipboard = topLevel?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(TotpSecret);
                    StatusMessage = "Secret copied to clipboard (will auto-clear in 30 seconds)";

                    _ = Task.Delay(30000).ContinueWith(async _ =>
                    {
                        try
                        {
                            var currentText = await clipboard.TryGetTextAsync();

                            if (currentText == TotpSecret)
                            {
                                await clipboard.ClearAsync();
                            }
                        }
                        catch
                        {

                        }
                    });
                }
                else
                {
                    StatusMessage = "Clipboard not available";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[TotpSettings] Failed to copy a TOTP value.");
                StatusMessage = "The authentication value could not be copied. Confirm clipboard access is allowed and try again.";
                System.Diagnostics.Debug.WriteLine($"Clipboard copy failed: {ex}");
            }
        }

        private string GenerateRandomBase32Secret()
        {
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            Span<byte> randomBytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(randomBytes);

            var secret = new char[32];
            for (int i = 0; i < 32; i++)
            {

                secret[i] = base32Chars[randomBytes[i] % base32Chars.Length];
            }
            return new string(secret);
        }
    }
}

