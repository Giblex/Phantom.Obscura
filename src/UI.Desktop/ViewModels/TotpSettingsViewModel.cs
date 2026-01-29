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
    /// <summary>
    /// View model for TOTP (Time-based One-Time Password) authenticator settings.
    /// Manages TOTP secret generation, QR code display, and backup codes.
    /// </summary>
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

                // Generate a random base32 secret (160 bits / 32 characters)
                TotpSecret = TotpService.GenerateSecret(20); // 20 bytes = 160 bits
                
                // Generate QR code data in otpauth:// format
                // Format: otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}
                QrCodeData = $"otpauth://totp/PhantomVault:{VaultName}?secret={TotpSecret}&issuer=PhantomVault";

                HasTotpSecret = true;
                StatusMessage = "TOTP secret generated successfully! Scan the QR code with your authenticator app.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to generate secret: {ex.Message}";
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
                StatusMessage = $"Failed to remove TOTP: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task VerifyTotpCode()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Verifying TOTP code...";

                await Task.Delay(500);

                // Verify TOTP code using the service
                if (_totpService != null && !string.IsNullOrEmpty(TotpSecret) && !string.IsNullOrEmpty(TestCode))
                {
                    string expectedCode = _totpService.GenerateCode(TotpSecret);
                    bool isValid = expectedCode == TestCode;

                    if (isValid)
                    {
                        StatusMessage = "TOTP code verified successfully!";
                        IsTotpEnabled = true;
                    }
                    else
                    {
                        StatusMessage = "✗ Invalid TOTP code. Please try again.";
                    }
                }
                else
                {
                    StatusMessage = "✗ Please enter a valid 6-digit code.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Verification failed: {ex.Message}";
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

                    // Auto-clear clipboard after 30 seconds for security
                    _ = Task.Delay(30000).ContinueWith(async _ =>
                    {
                        try
                        {
                            var currentText = await clipboard.TryGetTextAsync();
                            // Only clear if clipboard still contains our secret
                            if (currentText == TotpSecret)
                            {
                                await clipboard.ClearAsync();
                            }
                        }
                        catch
                        {
                            // Best effort - ignore errors in auto-clear
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
                StatusMessage = $"Failed to copy: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Clipboard copy failed: {ex}");
            }
        }

        private string GenerateRandomBase32Secret()
        {
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            
            // Use cryptographically secure random number generator
            // Generate 20 bytes (160 bits) of entropy for TOTP secret
            Span<byte> randomBytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(randomBytes);
            
            var secret = new char[32];
            for (int i = 0; i < 32; i++)
            {
                // Map random byte to base32 character index
                secret[i] = base32Chars[randomBytes[i] % base32Chars.Length];
            }
            return new string(secret);
        }
    }
}
