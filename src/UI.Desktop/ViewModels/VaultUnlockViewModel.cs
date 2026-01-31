using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ReactiveUI;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for vault unlock flow. Handles vault discovery,
    /// validation, and navigation to the main vault window.
    /// Includes throttling for failed unlock attempts.
    /// </summary>
    public sealed class VaultUnlockViewModel : ReactiveObject
    {
    private readonly string _usbPath;
    private readonly DialogService _dialogService;
    private readonly VaultLockDurationService _vaultLockDurationService;
    private readonly SecureTrashService _secureTrashService;
    private readonly IVeraCryptService _veraCryptService;
    private readonly IconManager _iconManager;
    private readonly UsbDetector _usbDetector;
    private readonly UnlockThrottleService _throttleService;

    private bool _isBusy;
    private string _status = "Searching for vault...";
    private int _progressPercent;
    private Window? _ownerWindow;

    public VaultUnlockViewModel(
        string usbPath,
        DialogService dialogService,
        VaultLockDurationService vaultLockDurationService,
        SecureTrashService secureTrashService,
        IVeraCryptService veraCryptService,
        IconManager iconManager,
        UsbDetector usbDetector)
    {
        _usbPath = usbPath ?? throw new ArgumentNullException(nameof(usbPath));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _vaultLockDurationService = vaultLockDurationService ?? throw new ArgumentNullException(nameof(vaultLockDurationService));
        _secureTrashService = secureTrashService ?? throw new ArgumentNullException(nameof(secureTrashService));
        _veraCryptService = veraCryptService ?? throw new ArgumentNullException(nameof(veraCryptService));
        _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));
        _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
        _throttleService = new UnlockThrottleService();

        UnlockCommand = ReactiveCommand.CreateFromTask(UnlockVaultAsync);
    }

        public ReactiveCommand<Unit, Unit> UnlockCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        /// <summary>
        /// Unlock progress percentage (0-100) for visual progress bar.
        /// </summary>
        public int ProgressPercent
        {
            get => _progressPercent;
            private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        /// <summary>
        /// Discovers vault on USB path, validates it, and navigates to VaultWindow.
        /// </summary>
        public async Task UnlockVaultAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                ProgressPercent = 5;
                Status = "Validating vault location...";

                // Validate USB path
                if (string.IsNullOrWhiteSpace(_usbPath))
                {
                    await _dialogService.ShowErrorAsync(
                        "Invalid Path",
                        "USB path is empty or invalid.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // Find the vault manifest in the hidden .phantom folder
                var phantomPath = Path.Combine(_usbPath, ".phantom", "manifests");
                
                if (!Directory.Exists(phantomPath))
                {
                    await _dialogService.ShowErrorAsync(
                        "Vault Not Found",
                        "Could not find vault files on this USB drive. The .phantom folder does not exist.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                ProgressPercent = 15;
                Status = "Searching for vault manifest...";

                // Get the first manifest file
                var manifestFiles = Directory.GetFiles(phantomPath, "*.manifest");
                if (manifestFiles.Length == 0)
                {
                    await _dialogService.ShowErrorAsync(
                        "Vault Not Found",
                        "Could not find any vault manifest files in the .phantom folder.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                var manifestPath = manifestFiles[0];

                // Check for unlock throttling before prompting for password
                if (_throttleService.IsThrottled(manifestPath, out var remainingLockout))
                {
                    var minutes = (int)Math.Ceiling(remainingLockout.TotalMinutes);
                    await _dialogService.ShowErrorAsync(
                        "Too Many Failed Attempts",
                        $"This vault is temporarily locked due to multiple failed unlock attempts.\n\n" +
                        $"Please wait {minutes} minute(s) before trying again.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // CRITICAL: Require authentication before opening vault
                ProgressPercent = 25;
                Status = "Authentication required...";
                var password = await PromptForPasswordAsync();
                if (string.IsNullOrEmpty(password))
                {
                    await _dialogService.ShowErrorAsync(
                        "Authentication Required",
                        "A passphrase is required to unlock the vault.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                ProgressPercent = 40;
                Status = "Initializing vault services...";

                // Create services for vault window
                var encryptionService = new EncryptionService();
                var manifestService = new ManifestService(encryptionService);

                ProgressPercent = 55;
                Status = "Validating passphrase (deriving key)...";

                // CRITICAL: Validate passphrase by attempting to decrypt the manifest
                try
                {
                    var testManifest = manifestService.ReadManifest(manifestPath, password, null);
                    if (testManifest == null)
                    {
                        // Register failed attempt for throttling
                        _throttleService.RegisterFailedAttempt(manifestPath);
                        var failedCount = _throttleService.GetFailedAttemptCount(manifestPath);
                        
                        await _dialogService.ShowErrorAsync(
                            "Invalid Passphrase",
                            $"The passphrase is incorrect or the vault is corrupted.\n\n" +
                            $"Failed attempts: {failedCount}",
                            _ownerWindow);
                        CloseAndReturnToWelcome();
                        return;
                    }
                    
                    // Check for additional auth requirements from manifest
                    if (testManifest.RequiresTotp && !string.IsNullOrEmpty(testManifest.TotpSecret))
                    {
                        ProgressPercent = 70;
                        Status = "TOTP verification required...";
                        var totpCode = await PromptForTotpAsync();
                        if (string.IsNullOrEmpty(totpCode) || !ValidateTotpCode(testManifest.TotpSecret, totpCode))
                        {
                            // Register failed TOTP attempt
                            _throttleService.RegisterFailedAttempt(manifestPath);
                            
                            await _dialogService.ShowErrorAsync(
                                "TOTP Verification Failed",
                                "The TOTP code is invalid or expired.",
                                _ownerWindow);
                            CloseAndReturnToWelcome();
                            return;
                        }
                    }
                    
                    // Successful authentication - reset throttle counter
                    _throttleService.ResetAttempts(manifestPath);
                }
                catch (Exception decryptEx)
                {
                    // Register failed attempt for throttling
                    _throttleService.RegisterFailedAttempt(manifestPath);
                    var failedCount = _throttleService.GetFailedAttemptCount(manifestPath);
                    
                    await _dialogService.ShowErrorAsync(
                        "Authentication Failed",
                        $"Failed to decrypt vault: {decryptEx.Message}\n\n" +
                        $"Failed attempts: {failedCount}",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // Get VeraCrypt path from settings
                var settings = SettingsService.Load();
                var effectivePath = string.IsNullOrWhiteSpace(settings.VeraCryptPathOverride) 
                    ? _veraCryptService.VeraCryptPath 
                    : settings.VeraCryptPathOverride!;
                
                // If effectivePath is null or empty, fall back to default "veracrypt"
                var vaultOptions = new Core.Options.VaultOptions 
                { 
                    VeraCryptPath = string.IsNullOrWhiteSpace(effectivePath) ? "veracrypt" : effectivePath 
                };

                var vaultService = new VaultService(vaultOptions);
                var idleLockService = new IdleLockService(TimeSpan.FromMinutes(15));
                var zkVaultService = new Core.Services.ZeroKnowledge.ZkVaultService();

                ProgressPercent = 85;
                Status = "Opening vault...";

            // Create and show vault window - pass the validated password
            var vaultViewModel = new VaultViewModel(
                vaultService,
                manifestService,
                idleLockService,
                zkVaultService,
                _dialogService,
                _vaultLockDurationService,
                _usbDetector,
                _secureTrashService,
                _iconManager);

            // Set the manifest context with the validated password
            vaultViewModel.SetManifestContext(manifestPath, password, null);

                ProgressPercent = 95;
                Status = "Preparing vault interface...";

                var vaultWindow = new VaultWindow
                {
                    DataContext = vaultViewModel
                };

                vaultViewModel.SetOwnerWindow(vaultWindow);

                ProgressPercent = 100;
                Status = "Vault unlocked!";
                vaultWindow.Show();

                // Zero out the password after use
                password = null;

                // Close unlock window
                _ownerWindow?.Close();
            }
            catch (Exception ex)
            {
                Status = "Error during vault unlock";
                await _dialogService.ShowErrorAsync(
                    "Vault Unlock Failed",
                    $"An error occurred while unlocking the vault: {ex.Message}",
                    _ownerWindow);
                CloseAndReturnToWelcome();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CloseAndReturnToWelcome()
        {
            _ownerWindow?.Close();

            // Show WelcomePage again
            if (Avalonia.Application.Current?.ApplicationLifetime 
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    if (window is WelcomePage welcomePage)
                    {
                        welcomePage.Show();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Prompts the user for the vault passphrase using a secure password dialog.
        /// </summary>
        private async Task<string?> PromptForPasswordAsync()
        {
            var dialog = new Window
            {
                Title = "Vault Passphrase Required",
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C3B4B"))
            };

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            
            var label = new TextBlock 
            { 
                Text = "Enter your vault passphrase to unlock:",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };
            
            // Use TextBox with PasswordChar for secure input
            var passwordBox = new TextBox 
            { 
                PasswordChar = '●',
                Watermark = "Passphrase",
                Width = 360,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Classes = { "SecureInput" }
            };

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            
            var okButton = new Button 
            { 
                Content = "Unlock",
                IsDefault = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };
            
            var cancelButton = new Button 
            { 
                Content = "Cancel",
                IsCancel = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(passwordBox);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, _) => { result = passwordBox.Text; dialog.Close(); };
            cancelButton.Click += (_, _) => { dialog.Close(); };

            if (_ownerWindow != null)
            {
                await dialog.ShowDialog(_ownerWindow);
            }
            else
            {
                dialog.Show();
                await Task.Delay(100); // Wait for dialog to show
            }

            // Clear the password box after reading
            passwordBox.Text = string.Empty;
            
            return result;
        }

        /// <summary>
        /// Prompts the user for a TOTP code.
        /// </summary>
        private async Task<string?> PromptForTotpAsync()
        {
            var dialog = new Window
            {
                Title = "Two-Factor Authentication",
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C3B4B"))
            };

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            
            var label = new TextBlock 
            { 
                Text = "Enter the 6-digit code from your authenticator app:",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };
            
            var codeBox = new TextBox 
            { 
                Watermark = "000000",
                Width = 120,
                MaxLength = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83"))
            };

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            
            var okButton = new Button 
            { 
                Content = "Verify",
                IsDefault = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };
            
            var cancelButton = new Button 
            { 
                Content = "Cancel",
                IsCancel = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(codeBox);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, _) => { result = codeBox.Text; dialog.Close(); };
            cancelButton.Click += (_, _) => { dialog.Close(); };

            if (_ownerWindow != null)
            {
                await dialog.ShowDialog(_ownerWindow);
            }

            return result;
        }

        /// <summary>
        /// Validates a TOTP code against the stored secret.
        /// </summary>
        private static bool ValidateTotpCode(string totpSecret, string code)
        {
            if (string.IsNullOrEmpty(totpSecret) || string.IsNullOrEmpty(code))
                return false;

            try
            {
                var totpService = new TotpService();
                var expectedCode = totpService.GenerateCode(totpSecret);
                
                // Use constant-time comparison to prevent timing attacks
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(code.PadLeft(6, '0')),
                    System.Text.Encoding.UTF8.GetBytes(expectedCode.PadLeft(6, '0')));
            }
            catch
            {
                return false;
            }
        }
    }
}
