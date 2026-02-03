using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run setup wizard that guides new users through
    /// initial configuration of PhantomVault.
    /// </summary>
    public partial class SetupWizardViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _currentStep = 1;

        [ObservableProperty]
        private int _totalSteps = 7;

        [ObservableProperty]
        private string _currentStepTitle = "Welcome";

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoNext = true;

        [ObservableProperty]
        private string _nextButtonText = "Next";

        // Step 1: Welcome
        [ObservableProperty]
        private bool _acceptedTerms;

        // Step 2: Security Level
        [ObservableProperty]
        private string _selectedSecurityLevel = "Balanced";

        [ObservableProperty]
        private string _securityDescription = "Recommended for most users. Provides strong security with convenient features.";

        // Step 3: Storage Location
        [ObservableProperty]
        private string _selectedStorageLocation = "USB";

        [ObservableProperty]
        private bool _usbDetected;

        [ObservableProperty]
        private string? _selectedUsbPath;

        [ObservableProperty]
        private ObservableCollection<string> _availableUsbDrives = new();

        // Step 4: Keyfile & Password
        [ObservableProperty]
        private string _masterPassword = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private string _passwordStrength = "None";

        [ObservableProperty]
        private bool _passwordsMatch;

        [ObservableProperty]
        private bool _usePassword;

        [ObservableProperty]
        private string? _keyfilePath;

        // Step 5: Windows Hello
        [ObservableProperty]
        private bool _enableWindowsHello;

        [ObservableProperty]
        private bool _windowsHelloAvailable;

        [ObservableProperty]
        private string? _windowsHelloStatus;

        // Step 6: Passkeys & TOTP
        [ObservableProperty]
        private bool _enablePasskeys;

        [ObservableProperty]
        private bool _passkeysAvailable;

        [ObservableProperty]
        private string? _passkeysStatus;

        [ObservableProperty]
        private bool _enableTotp;

        [ObservableProperty]
        private string? _totpSecretKey;

        [ObservableProperty]
        private string? _totpQrCodeUri;

        // Step 7: Summary
        [ObservableProperty]
        private string _setupSummary = string.Empty;

        [ObservableProperty]
        private bool _isCompleting;

        [ObservableProperty]
        private string? _statusMessage;

        public ObservableCollection<SecurityLevelOption> SecurityLevels { get; } = new()
        {
            new SecurityLevelOption
            {
                Name = "Basic",
                Description = "Simple keyfile protection. Quick setup for personal use.",
                Features = new[] { "Keyfile authentication", "Local storage", "Basic encryption (AES-256)" },
                RecommendedFor = "Personal use on trusted devices"
            },
            new SecurityLevelOption
            {
                Name = "Balanced",
                Description = "Recommended for most users. Strong security with convenient features.",
                Features = new[] { "Keyfile + optional password", "USB storage recommended", "Post-quantum encryption", "Auto-lock on idle" },
                RecommendedFor = "General users who want strong security"
            },
            new SecurityLevelOption
            {
                Name = "Maximum",
                Description = "Highest security. Required for sensitive data and compliance.",
                Features = new[] { "Keyfile + password required", "USB binding enforced", "Encrypted container", "Post-quantum encryption", "Aggressive auto-locking" },
                RecommendedFor = "Enterprise users, high-value data"
            }
        };

        public SetupWizardViewModel()
        {
            // Initialize default state
        }

        [RelayCommand]
        private async Task NextStepAsync()
        {
            if (!await ValidateCurrentStepAsync())
                return;

            if (CurrentStep < TotalSteps)
            {
                CurrentStep++;
                UpdateStepInfo();
                await LoadStepDataAsync();
            }
            else
            {
                await CompleteSetupAsync();
            }
        }

        [RelayCommand]
        private void PreviousStep()
        {
            if (CurrentStep > 1)
            {
                CurrentStep--;
                UpdateStepInfo();
            }
        }

        [RelayCommand]
        private async Task LoadStepDataAsync()
        {
            switch (CurrentStep)
            {
                case 3: // Storage Location
                    await DetectUsbDrivesAsync();
                    break;

                case 5: // Windows Hello
                    await DetectWindowsHelloAsync();
                    break;

                case 6: // Passkeys & TOTP
                    await DetectPasskeysAsync();
                    break;

                case 7: // Summary
                    GenerateSummary();
                    break;
            }
        }

        private async Task<bool> ValidateCurrentStepAsync()
        {
            switch (CurrentStep)
            {
                case 1: // Welcome
                    if (!AcceptedTerms)
                    {
                        StatusMessage = "Please accept the terms and conditions to continue.";
                        return false;
                    }
                    break;

                case 2: // Security Level
                    if (string.IsNullOrEmpty(SelectedSecurityLevel))
                    {
                        StatusMessage = "Please select a security level.";
                        return false;
                    }
                    break;

                case 3: // Storage Location
                    if (SelectedStorageLocation == "USB" && string.IsNullOrEmpty(SelectedUsbPath))
                    {
                        StatusMessage = "Please select a USB drive for your vault.";
                        return false;
                    }
                    break;

                case 4: // Keyfile & Password
                    // Keyfile is always generated - no validation needed
                    // Password is optional, but if provided, must match confirmation
                    if (UsePassword)
                    {
                        if (string.IsNullOrEmpty(MasterPassword))
                        {
                            StatusMessage = "Please enter a master password.";
                            return false;
                        }
                        if (MasterPassword != ConfirmPassword)
                        {
                            StatusMessage = "Passwords do not match.";
                            return false;
                        }
                        if (PasswordStrength == "Weak")
                        {
                            StatusMessage = "Password is too weak. Please use a stronger password.";
                            return false;
                        }
                    }
                    break;
            }

            return true;
        }

        private void UpdateStepInfo()
        {
            CurrentStepTitle = CurrentStep switch
            {
                1 => "Welcome to PhantomVault",
                2 => "Choose Security Level",
                3 => "Select Storage Location",
                4 => "Keyfile & Password",
                5 => "Windows Hello",
                6 => "Passkeys & TOTP",
                7 => "Review and Complete",
                _ => "Setup"
            };

            CanGoBack = CurrentStep > 1;
            CanGoNext = true;
            NextButtonText = CurrentStep == TotalSteps ? "Create Vault" : "Next";
        }

        private async Task DetectUsbDrivesAsync()
        {
            await Task.Run(() =>
            {
                AvailableUsbDrives.Clear();
                var drives = DriveInfo.GetDrives();
                
                foreach (var drive in drives)
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        var displayName = $"{drive.Name} - {drive.VolumeLabel} ({drive.TotalSize / 1024 / 1024 / 1024} GB)";
                        AvailableUsbDrives.Add(displayName);
                    }
                }

                UsbDetected = AvailableUsbDrives.Count > 0;
                if (UsbDetected)
                {
                    StatusMessage = $"Found {AvailableUsbDrives.Count} USB drive(s).";
                }
                else
                {
                    StatusMessage = "No USB drives detected. You can use local storage instead.";
                }
            });
        }

        [RelayCommand]
        private void GenerateStrongPassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=";
            var random = new Random();
            var password = new char[20];
            
            for (int i = 0; i < password.Length; i++)
            {
                password[i] = chars[random.Next(chars.Length)];
            }

            MasterPassword = new string(password);
            ConfirmPassword = MasterPassword;
            AnalyzePasswordStrength();
            StatusMessage = "Strong password generated. Please save it in a secure location.";
        }

        [RelayCommand]
        private void AnalyzePasswordStrength()
        {
            if (string.IsNullOrEmpty(MasterPassword))
            {
                PasswordStrength = "None";
                return;
            }

            int score = 0;
            
            // Length
            if (string.IsNullOrEmpty(MasterPassword))
            {
                PasswordStrength = "None";
                PasswordsMatch = true;
                return;
            }
            
            if (MasterPassword.Length >= 12) score++;
            if (MasterPassword.Length >= 16) score++;
            if (MasterPassword.Length >= 20) score++;

            // Complexity
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[A-Z]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[a-z]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[0-9]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[^A-Za-z0-9]")) score++;

            PasswordStrength = score switch
            {
                <= 2 => "Weak",
                <= 4 => "Fair",
                <= 6 => "Good",
                _ => "Excellent"
            };

            PasswordsMatch = MasterPassword == ConfirmPassword;
        }

        private async Task DetectWindowsHelloAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Check if Windows Hello is available on this system
                    // This is a simplified check - in production, use Windows.Security.Credentials.KeyCredentialManager
                    var osVersion = Environment.OSVersion;
                    WindowsHelloAvailable = osVersion.Platform == PlatformID.Win32NT && 
                                           osVersion.Version.Major >= 10;
                    
                    if (WindowsHelloAvailable)
                    {
                        WindowsHelloStatus = "Windows Hello is available on this device.";
                        StatusMessage = "Windows Hello detected. You can enable biometric authentication.";
                    }
                    else
                    {
                        WindowsHelloStatus = "Windows Hello is not available on this device.";
                        StatusMessage = "Windows Hello not available. You can skip this step.";
                    }
                }
                catch
                {
                    WindowsHelloAvailable = false;
                    WindowsHelloStatus = "Unable to detect Windows Hello status.";
                }
            });
        }

        private async Task DetectPasskeysAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Check if passkeys/FIDO2 is supported
                    // In production, this would check for FIDO2 authenticator availability
                    PasskeysAvailable = true; // Most modern systems support software passkeys
                    PasskeysStatus = "Passkey authentication is available.";
                    
                    // Generate TOTP secret if TOTP is enabled
                    if (EnableTotp && string.IsNullOrEmpty(TotpSecretKey))
                    {
                        // Generate a random TOTP secret (base32 encoded)
                        var random = new Random();
                        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                        var secret = new char[32];
                        for (int i = 0; i < secret.Length; i++)
                        {
                            secret[i] = base32Chars[random.Next(base32Chars.Length)];
                        }
                        TotpSecretKey = new string(secret);
                        TotpQrCodeUri = $"otpauth://totp/PhantomVault:User?secret={TotpSecretKey}&issuer=PhantomVault";
                    }
                    
                    StatusMessage = "Configure additional authentication methods.";
                }
                catch
                {
                    PasskeysAvailable = false;
                    PasskeysStatus = "Unable to detect passkey support.";
                }
            });
        }

        [RelayCommand]
        private void GenerateTotpSecret()
        {
            var random = new Random();
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var secret = new char[32];
            for (int i = 0; i < secret.Length; i++)
            {
                secret[i] = base32Chars[random.Next(base32Chars.Length)];
            }
            TotpSecretKey = new string(secret);
            TotpQrCodeUri = $"otpauth://totp/PhantomVault:User?secret={TotpSecretKey}&issuer=PhantomVault";
            StatusMessage = "TOTP secret generated. Scan the QR code with your authenticator app.";
        }

        private void GenerateSummary()
        {
            var summary = "Your PhantomVault will be created with the following settings:\n\n";
            summary += $"• Security Level: {SelectedSecurityLevel}\n";
            summary += $"• Storage Location: {(SelectedStorageLocation == "USB" ? $"USB Drive ({SelectedUsbPath})" : "Local Storage")}\n";
            summary += $"• Keyfile: Will be generated\n";
            summary += $"• Master Password: {(UsePassword ? "Configured" : "Not used")}\n";
            summary += $"• Windows Hello: {(EnableWindowsHello ? "Enabled" : "Not used")}\n";
            summary += $"• Passkeys: {(EnablePasskeys ? "Enabled" : "Not used")}\n";
            summary += $"• TOTP: {(EnableTotp ? "Enabled" : "Not used")}\n";
            summary += $"\nEncryption: AES-256-GCM with {(SelectedSecurityLevel == "Maximum" ? "Post-Quantum" : "Standard")} key derivation\n";

            SetupSummary = summary;
        }

        private async Task CompleteSetupAsync()
        {
            try
            {
                IsCompleting = true;
                StatusMessage = "Creating your vault...";

                // Determine vault path
                string vaultPath;
                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    vaultPath = Path.Combine(SelectedUsbPath, "PhantomVault");
                }
                else
                {
                    vaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "PhantomVault");
                }

                // Create vault directory if it doesn't exist
                Directory.CreateDirectory(vaultPath);

                // Generate keyfile (always required)
                string keyfilePath = Path.Combine(vaultPath, "vault.key");
                await GenerateKeyfileAsync(keyfilePath);
                KeyfilePath = keyfilePath;

                // Save configuration
                var configPath = Path.Combine(vaultPath, "vault.config.json");
                var config = new
                {
                    SecurityLevel = SelectedSecurityLevel,
                    StorageLocation = SelectedStorageLocation,
                    KeyfilePath = keyfilePath,
                    UsePassword = UsePassword,
                    EnableWindowsHello = EnableWindowsHello,
                    EnablePasskeys = EnablePasskeys,
                    EnableTotp = EnableTotp,
                    CreatedAt = DateTime.UtcNow
                };
                
                var configJson = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configPath, configJson);

                StatusMessage = "Vault created successfully!";
                
                // Close the wizard after a short delay
                await Task.Delay(1500);
                
                // TODO: Navigate to main vault view or close wizard
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating vault: {ex.Message}";
            }
            finally
            {
                IsCompleting = false;
            }
        }

        private async Task GenerateKeyfileAsync(string keyfilePath)
        {
            // Generate a cryptographically secure random keyfile
            var keyData = new byte[256]; // 256 bytes = 2048 bits
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyData);
            }
            
            await File.WriteAllBytesAsync(keyfilePath, keyData);
            StatusMessage = "Keyfile generated successfully.";
        }

        partial void OnSelectedSecurityLevelChanged(string value)
        {
            var selected = SecurityLevels.FirstOrDefault(s => s.Name == value);
            if (selected != null)
            {
                SecurityDescription = selected.Description;
            }
        }

        partial void OnMasterPasswordChanged(string value)
        {
            AnalyzePasswordStrength();
        }

        partial void OnConfirmPasswordChanged(string value)
        {
            PasswordsMatch = MasterPassword == ConfirmPassword;
        }
    }

    public class SecurityLevelOption
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] Features { get; set; } = Array.Empty<string>();
        public string RecommendedFor { get; set; } = string.Empty;
    }
}
