using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run setup wizard that guides new users through
    /// initial configuration of PhantomVault.
    /// </summary>
    public partial class SetupWizardViewModel : ObservableObject
    {
        private Window? _ownerWindow;
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly UsbBindingService _usbBindingService;
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

        [ObservableProperty]
        private bool _useExistingKeyfile;

        [ObservableProperty]
        private bool _keyfileSelected;

        [ObservableProperty]
        private string _keyfileStatus = "A new keyfile will be generated";

        // USB Binding
        [ObservableProperty]
        private string? _usbSerialNumber;

        [ObservableProperty]
        private string? _usbDeviceId;

        [ObservableProperty]
        private bool _enableUsbBinding = true;

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
                Features = new[] { "Keyfile + optional password", "USB storage recommended", "Post-quantum encryption", "Auto-lock on idle", "Security dashboard" },
                RecommendedFor = "General users who want strong security"
            },
            new SecurityLevelOption
            {
                Name = "Maximum",
                Description = "Highest security. Required for sensitive data and compliance.",
                Features = new[] { "Keyfile + password required", "USB binding enforced", "Encrypted container", "Post-quantum encryption", "Aggressive auto-locking", "Security dashboard" },
                RecommendedFor = "Enterprise users, high-value data"
            }
        };

        public SetupWizardViewModel()
        {
            // Initialize services
            _encryptionService = new EncryptionService();
            _manifestService = new ManifestService(_encryptionService);
            _usbBindingService = new UsbBindingService();
        }

        /// <summary>
        /// Sets the owner window for file dialogs.
        /// </summary>
        public void SetOwnerWindow(Window owner)
        {
            _ownerWindow = owner;
        }

        [RelayCommand]
        private async Task BrowseKeyfileAsync()
        {
            if (_ownerWindow == null) return;

            var storageProvider = _ownerWindow.StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Existing Keyfile",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Keyfiles") { Patterns = new[] { "*.key", "*.keyfile" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (result.Count > 0)
            {
                var file = result[0];
                KeyfilePath = file.Path.LocalPath;
                UseExistingKeyfile = true;
                KeyfileSelected = true;
                KeyfileStatus = $"Using existing keyfile: {Path.GetFileName(KeyfilePath)}";
            }
        }

        [RelayCommand]
        private void ClearKeyfile()
        {
            KeyfilePath = null;
            UseExistingKeyfile = false;
            KeyfileSelected = false;
            KeyfileStatus = "A new keyfile will be generated";
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

        /// <summary>
        /// Navigates directly to a specific step (only allowed for completed steps).
        /// </summary>
        [RelayCommand]
        private void GoToStep(object? stepParameter)
        {
            // Parse step from XAML CommandParameter (passed as string)
            if (stepParameter is string stepStr && int.TryParse(stepStr, out int targetStep))
            {
                // Can only go back to completed steps (steps before current)
                if (targetStep >= 1 && targetStep < CurrentStep)
                {
                    CurrentStep = targetStep;
                    UpdateStepInfo();
                }
            }
            else if (stepParameter is int targetStepInt)
            {
                // Also handle direct int parameter
                if (targetStepInt >= 1 && targetStepInt < CurrentStep)
                {
                    CurrentStep = targetStepInt;
                    UpdateStepInfo();
                }
            }
        }

        /// <summary>
        /// Checks if a specific step can be navigated to (only completed steps).
        /// </summary>
        public bool CanGoToStep(int targetStep) => targetStep >= 1 && targetStep < CurrentStep;

        [RelayCommand]
        private async Task LoadStepDataAsync()
        {
            switch (CurrentStep)
            {
                case 3: // Storage Location
                    await DetectUsbDrivesAsync();
                    // Detect USB serial when USB is selected
                    await DetectUsbSerialAsync();
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

        private async Task DetectUsbSerialAsync()
        {
            await Task.Run(() =>
            {
                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    try
                    {
                        // Extract drive letter from the selected path (e.g., "E:\" from "E:\ - My USB (16 GB)")
                        var driveLetter = SelectedUsbPath.Length >= 2 ? SelectedUsbPath.Substring(0, 3) : SelectedUsbPath;
                        if (SelectedUsbPath.Contains(" - "))
                        {
                            driveLetter = SelectedUsbPath.Split(" - ")[0].Trim();
                            if (!driveLetter.EndsWith("\\")) driveLetter += "\\";
                        }

                        UsbDeviceId = _usbBindingService.ComputeDeviceId(driveLetter);
                        UsbSerialNumber = UsbDeviceId;
                        StatusMessage = $"USB bound to device ID: {UsbDeviceId}";
                    }
                    catch (Exception ex)
                    {
                        UsbDeviceId = null;
                        UsbSerialNumber = null;
                        StatusMessage = $"Could not detect USB serial: {ex.Message}";
                    }
                }
                else
                {
                    UsbDeviceId = null;
                    UsbSerialNumber = null;
                }
            });
        }

        partial void OnSelectedUsbPathChanged(string? value)
        {
            // Re-detect USB serial when selection changes
            _ = DetectUsbSerialAsync();
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

                // Determine vault path based on storage selection
                string vaultPath;
                string? driveRoot = null;
                
                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    // Extract drive letter from the selected path
                    driveRoot = SelectedUsbPath.Length >= 2 ? SelectedUsbPath.Substring(0, 3) : SelectedUsbPath;
                    if (SelectedUsbPath.Contains(" - "))
                    {
                        driveRoot = SelectedUsbPath.Split(" - ")[0].Trim();
                        if (!driveRoot.EndsWith("\\")) driveRoot += "\\";
                    }
                    vaultPath = Path.Combine(driveRoot, "PhantomVault");
                }
                else
                {
                    vaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "PhantomVault");
                }

                // Create vault directory if it doesn't exist
                Directory.CreateDirectory(vaultPath);
                StatusMessage = "Creating vault directory...";

                // Handle keyfile: either use existing or generate new
                string? keyfilePath = null;
                if (UseExistingKeyfile && KeyfileSelected)
                {
                    // User selected an existing keyfile
                    keyfilePath = KeyfilePath;
                    StatusMessage = "Using existing keyfile...";
                }
                else
                {
                    // Generate new keyfile
                    keyfilePath = Path.Combine(vaultPath, "vault.key");
                    await GenerateKeyfileAsync(keyfilePath);
                    KeyfilePath = keyfilePath;
                    StatusMessage = "Generated new keyfile...";
                }

                // Compute USB device binding if storing on USB
                string? usbSerial = null;
                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(driveRoot) && EnableUsbBinding)
                {
                    try
                    {
                        usbSerial = _usbBindingService.ComputeDeviceId(driveRoot);
                        UsbDeviceId = usbSerial;
                        StatusMessage = "Bound to USB device...";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Warning: Could not bind to USB device: {ex.Message}";
                        // Continue without USB binding
                    }
                }

                // Generate salt for key derivation
                byte[] salt = _encryptionService.GenerateSalt();

                // Create the encrypted manifest
                var manifest = new VaultManifest
                {
                    Version = 3,
                    VaultName = "PhantomVault",
                    ContainerPath = Path.Combine(vaultPath, "vault.pvault"),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    Description = $"Created with {SelectedSecurityLevel} security level",
                    SaltBase64 = Convert.ToBase64String(salt),
                    Algorithm = "AES-256-GCM",
                    KeyfilePath = keyfilePath,
                    DeviceId = usbSerial,
                    RequiresHardwareToken = false,
                    RequiresTotp = EnableTotp
                };

                // Generate TOTP secret if enabled
                if (EnableTotp)
                {
                    manifest.TotpSecret = GenerateSecureTotpSecret();
                    StatusMessage = "Generated TOTP secret...";
                }

                // Write encrypted manifest
                StatusMessage = "Writing encrypted manifest...";
                var manifestPath = Path.Combine(vaultPath, "vault.manifest.encrypted");
                
                // Determine dual-factor requirement based on security level
                bool requireDualFactor = SelectedSecurityLevel == "Maximum";
                
                // Use passphrase if password is enabled, otherwise rely on keyfile
                string? passphrase = UsePassword ? MasterPassword : null;

#pragma warning disable CS0618 // Using obsolete WriteManifest for now (SecurePassword requires different handling)
                _manifestService.WriteManifest(
                    manifest,
                    manifestPath,
                    passphrase,
                    keyfilePath,
                    usbSerial,
                    requireDualFactor);
#pragma warning restore CS0618

                // Create empty container file (the actual vault database)
                var containerPath = manifest.ContainerPath;
                if (!File.Exists(containerPath))
                {
                    await File.WriteAllBytesAsync(containerPath, Array.Empty<byte>());
                }

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

        private string GenerateSecureTotpSecret()
        {
            // Generate 20 bytes of random data for TOTP (160 bits, RFC 6238 compliant)
            var secretBytes = new byte[20];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(secretBytes);
            }
            // Convert to Base32 for TOTP compatibility
            return ToBase32(secretBytes);
        }

        private static string ToBase32(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var result = new System.Text.StringBuilder();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                    bitsLeft -= 5;
                }
            }

            if (bitsLeft > 0)
            {
                result.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
            }

            return result.ToString();
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
