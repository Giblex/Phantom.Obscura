using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using Serilog;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run setup wizard that guides new users through
    /// initial configuration of Phantom Obscura.
    /// </summary>
    public partial class SetupWizardViewModel : ObservableObject
    {
        private Window? _ownerWindow;
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly UsbBindingService _usbBindingService;
        private readonly PhantomContainerService _containerService;
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        [ObservableProperty]
        private int _currentStep = 1;

        [ObservableProperty]
        private int _totalSteps = 6;

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
        private string _selectedSecurityLevel = "Stealth Secure";

        [ObservableProperty]
        private string _securityDescription = "Recommended for most users. Packs the vault into a single concealed master volume while preserving reversible migration.";

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

        // GUUID Binding (hardware UUID)
        [ObservableProperty]
        private bool _enableGuuidBinding;

        [ObservableProperty]
        private string? _guuidValue;

        // Phantom Key Integration (from Security step)
        [ObservableProperty]
        private bool _enablePhantomKey;

        // Encrypted Container Option
        [ObservableProperty]
        private bool _enableEncryptedContainer;

        [ObservableProperty]
        private string _encryptedContainerSize = "256 MB";

        // USB Remnant Detection
        [ObservableProperty]
        private ObservableCollection<DetectedRemnant> _detectedRemnants = new();

        [ObservableProperty]
        private bool _hasRemnants;

        [ObservableProperty]
        private string _selectedRemnantAction = "WipeRemnants";

        [ObservableProperty]
        private string _remainingUsbSpaceDisplay = string.Empty;

        /// <summary>True when CurrentStep is the dynamically-positioned remnant step (step 6, only when HasRemnants).</summary>
        public bool IsRemnantStep => HasRemnants && CurrentStep == 6;

        /// <summary>True when CurrentStep is the final review/summary step (always TotalSteps).</summary>
        public bool IsReviewStep => CurrentStep == TotalSteps;

        /// <summary>True when the wizard inserts a dedicated remnant review step before completion.</summary>
        public bool UsesExtendedProgress => HasRemnants;

        /// <summary>Two-way helper for the "Wipe remnants" radio button.</summary>
        public bool WipeRemnantsSelected
        {
            get => SelectedRemnantAction == "WipeRemnants";
            set { if (value) SelectedRemnantAction = "WipeRemnants"; }
        }

        /// <summary>Two-way helper for the "Ignore remnants" radio button.</summary>
        public bool IgnoreRemnantsSelected
        {
            get => SelectedRemnantAction == "IgnoreRemnants";
            set { if (value) SelectedRemnantAction = "IgnoreRemnants"; }
        }

        partial void OnSelectedRemnantActionChanged(string value)
        {
            OnPropertyChanged(nameof(WipeRemnantsSelected));
            OnPropertyChanged(nameof(IgnoreRemnantsSelected));
        }

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

        [ObservableProperty]
        private bool _enablePin;

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
                Name = "Standard Secure",
                Description = "Filesystem-backed vault layout with direct encrypted containers under the hidden Phantom workspace.",
                Features = new[]
                {
                    "Direct encrypted root, vault, object, and recovery containers",
                    "Lowest operational risk and easiest support/recovery path",
                    "Best for broad compatibility and reversible migration into higher tiers"
                },
                RecommendedFor = "Default supportable baseline"
            },
            new SecurityLevelOption
            {
                Name = "Stealth Secure",
                Description = "Recommended for most users. Packs the canonical container layout into a concealed master volume while preserving reversibility.",
                Features = new[]
                {
                    "Single packed master volume built from the same canonical inner layout",
                    "Reduced visible structure and better artifact minimisation",
                    "Clean migration path back to Standard or forward to Black Secure"
                },
                RecommendedFor = "Recommended",
                IsSelected = true
            },
            new SecurityLevelOption
            {
                Name = "Black Secure",
                Description = "Expert profile that writes the canonical vault layout directly to the USB device as a raw Black Secure transport.",
                Features = new[]
                {
                    "USB is bound and reopened through the physical device path instead of a browsable filesystem",
                    "Requires a master password and device-bound factors without an external keyfile path",
                    "Provisioned with reversible migration metadata and rollback-ready storage records"
                },
                RecommendedFor = "Expert / adversarial"
            }
        };

        public bool IsBlackSecureSelected => GetSelectedProtectionTier() == VaultProtectionTier.BlackSecure;
        public bool SupportsExternalKeyfile => !IsBlackSecureSelected;
        public bool ShowGeneratedKeyfileInfo => SupportsExternalKeyfile && !UseExistingKeyfile;
        public bool ShowPasswordToggle => !IsBlackSecureSelected;
        public string KeyMaterialSectionTitle => IsBlackSecureSelected ? "Device-Bound Authentication" : "Keyfile Configuration";
        public string KeyMaterialSectionSubtitle => IsBlackSecureSelected
            ? "Black Secure disables the external keyfile path and binds the vault directly to the selected USB device."
            : "Required - Your vault will be secured with a unique keyfile";
        public string KeyMaterialDescription => IsBlackSecureSelected
            ? "Black Secure uses a raw-device transport with USB binding, provisioning metadata, and a required master password. There is no external keyfile file to browse or store separately."
            : "A keyfile is a cryptographic file that acts as your primary authentication method. Store it securely on your USB drive or in a safe location. Without this file, your vault cannot be accessed.";
        public string PasswordSectionTitle => IsBlackSecureSelected ? "Required Master Password" : "Optional Master Password";
        public string PasswordSectionDescription => IsBlackSecureSelected
            ? "Black Secure requires a master password because the USB does not expose a browsable filesystem or external keyfile path."
            : "Add an extra layer of protection with a password";
        public string PasswordToggleText => IsBlackSecureSelected
            ? "Black Secure requires a master password"
            : "Enable master password (recommended for extra security)";

        public SetupWizardViewModel()
        {
            // Initialize services
            _encryptionService = new EncryptionService();
            _containerService = new PhantomContainerService(_encryptionService);
            _manifestService = new ManifestService(_encryptionService, _containerService);
            _usbBindingService = new UsbBindingService();
            _usbArtifactProtectionService = new UsbArtifactProtectionService(_encryptionService);
            _blackSecureRawVolumeService = new BlackSecureRawVolumeService();

            var settings = PhantomVault.UI.Services.SettingsService.Load();
            SelectedSecurityLevel = settings.DefaultVaultProtectionTier switch
            {
                nameof(VaultProtectionTier.StandardSecure) => "Standard Secure",
                nameof(VaultProtectionTier.BlackSecure) => "Black Secure",
                _ => "Stealth Secure"
            };
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
            Log.Information("NextStepAsync called: CurrentStep={CurrentStep}, TotalSteps={TotalSteps}", CurrentStep, TotalSteps);

            if (!await ValidateCurrentStepAsync())
            {
                Log.Warning("Validation failed for step {CurrentStep}", CurrentStep);
                return;
            }

            if (CurrentStep < TotalSteps)
            {
                CurrentStep++;
                UpdateStepInfo();
                await LoadStepDataAsync();
            }
            else
            {
                Log.Information("On final step — calling CompleteSetupAsync");
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
                case 4: // Storage Location
                    await DetectUsbDrivesAsync();
                    // Detect USB serial when USB is selected
                    await DetectUsbSerialAsync();
                    break;

                default:
                    // Summary is always the last step (6 or 7 depending on remnants)
                    if (CurrentStep == TotalSteps)
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

                case 4: // Storage Location (USB-only)
                    if (string.IsNullOrEmpty(SelectedUsbPath))
                    {
                        StatusMessage = "Please select a USB drive for your vault.";
                        return false;
                    }
                    break;

                case 5: // Keyfile & Password
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
                    else if (GetSelectedProtectionTier() == VaultProtectionTier.BlackSecure)
                    {
                        StatusMessage = "Black Secure requires a master password because the USB will not expose a browsable filesystem or external keyfile path.";
                        return false;
                    }
                    break;
            }

            return true;
        }

        private void UpdateStepInfo()
        {
            CurrentStepTitle = CurrentStep switch
            {
                1 => "Welcome to Phantom Obscura",
                2 => "Choose Security Level",
                3 => "PhantomKey Setup",
                4 => "Select Storage Location",
                5 => "Keyfile & Password",
                _ when IsRemnantStep => "Remnant Actions",
                _ when IsReviewStep => "Review and Complete",
                _ => "Setup"
            };

            // Notify computed step-visibility properties
            OnPropertyChanged(nameof(IsRemnantStep));
            OnPropertyChanged(nameof(IsReviewStep));

            CanGoBack = CurrentStep > 1;
            CanGoNext = true;
            NextButtonText = CurrentStep == TotalSteps ? "Create Vault" : "Next";
        }

        private async Task DetectUsbDrivesAsync()
        {
            var detectedDrives = await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives();
                var results = new List<string>();

                foreach (var drive in drives)
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        var displayName = $"{drive.Name} - {drive.VolumeLabel} ({drive.TotalSize / 1024 / 1024 / 1024} GB)";
                        results.Add(displayName);
                    }
                }

                return results;
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableUsbDrives.Clear();
                foreach (var drive in detectedDrives)
                    AvailableUsbDrives.Add(drive);

                UsbDetected = detectedDrives.Count > 0;
                if (UsbDetected)
                {
                    if (string.IsNullOrWhiteSpace(SelectedUsbPath) || !detectedDrives.Contains(SelectedUsbPath))
                        SelectedUsbPath = detectedDrives[0];

                    StatusMessage = $"Found {detectedDrives.Count} USB drive(s).";
                }
                else
                {
                    SelectedUsbPath = null;
                    StatusMessage = "No USB drives detected. Please insert a USB drive to continue.";
                }
            });

            // Scan for remnants on detected USB drives
            if (UsbDetected && !string.IsNullOrEmpty(SelectedUsbPath))
            {
                await ScanForRemnantsAsync();
            }
        }

        private async Task ScanForRemnantsAsync()
        {
            var driveRoot = SelectedUsbPath;
            if (driveRoot != null && driveRoot.Contains(" - "))
            {
                driveRoot = driveRoot.Split(" - ")[0].Trim();
                if (!driveRoot.EndsWith("\\")) driveRoot += "\\";
            }
            if (string.IsNullOrEmpty(driveRoot))
                return;

            var scanResults = await Task.Run(() =>
            {
                var remnants = new List<DetectedRemnant>();
                var searchPatterns = new[]
                {
                    "*.pvault",
                    "*.key",
                    "*.keyfile",
                    "*.manifest",
                    "*.manifest.encrypted",
                    "*.encrypted",
                    "*.pmeta",
                    "system.bin",
                    "obscura.vol"
                };

                foreach (var pattern in searchPatterns)
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(driveRoot, pattern, SearchOption.AllDirectories))
                        {
                            var info = new FileInfo(file);
                            var fileType = Path.GetFileName(file).ToLowerInvariant() switch
                            {
                                "system.bin" => "Packed Vault",
                                "obscura.vol" => "Packed Vault",
                                _ => Path.GetExtension(file).ToLowerInvariant() switch
                                {
                                    ".pvault" => "Vault",
                                    ".key" or ".keyfile" => "Keyfile",
                                    ".pmeta" => "Metadata",
                                    ".encrypted" => "Encrypted Container",
                                    ".manifest" or ".manifest.encrypted" => "Manifest",
                                    _ when file.Contains("manifest", StringComparison.OrdinalIgnoreCase) => "Manifest",
                                    _ => "Encrypted Container"
                                }
                            };
                            remnants.Add(new DetectedRemnant
                            {
                                FileName = info.Name,
                                FilePath = info.FullName,
                                FileType = fileType,
                                FileSize = FormatFileSize(info.Length)
                            });
                        }
                    }
                    catch
                    {
                        // Skip inaccessible directories during the scan.
                    }
                }

                string remainingUsbSpace;
                try
                {
                    var drive = new DriveInfo(driveRoot[..1]);
                    remainingUsbSpace = $"{FormatFileSize(drive.AvailableFreeSpace)} free of {FormatFileSize(drive.TotalSize)}";
                }
                catch
                {
                    remainingUsbSpace = "Unknown";
                }

                return (remnants, remainingUsbSpace);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DetectedRemnants.Clear();
                foreach (var remnant in scanResults.remnants)
                    DetectedRemnants.Add(remnant);

                HasRemnants = DetectedRemnants.Count > 0;
                TotalSteps = HasRemnants ? 7 : 6;
                RemainingUsbSpaceDisplay = scanResults.remainingUsbSpace;

                if (CurrentStep > TotalSteps)
                    CurrentStep = TotalSteps;

                OnPropertyChanged(nameof(UsesExtendedProgress));
                UpdateStepInfo();
            });
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
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

            // Also scan for remnants on the newly-selected drive
            if (!string.IsNullOrEmpty(value))
            {
                _ = ScanForRemnantsAsync();
            }
        }

        [RelayCommand]
        private void GenerateStrongPassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=";
            var password = new char[20];

            for (int i = 0; i < password.Length; i++)
            {
                password[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
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
                PasskeysStatus = "Local Windows Hello-backed authentication is available.";

                    // Generate TOTP secret if TOTP is enabled
                    if (EnableTotp && string.IsNullOrEmpty(TotpSecretKey))
                    {
                        // Generate a random TOTP secret (base32 encoded)
                        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                        var secret = new char[32];
                        for (int i = 0; i < secret.Length; i++)
                        {
                            secret[i] = base32Chars[RandomNumberGenerator.GetInt32(base32Chars.Length)];
                        }
                        TotpSecretKey = new string(secret);
                        TotpQrCodeUri = $"otpauth://totp/PhantomObscura:User?secret={TotpSecretKey}&issuer=PhantomObscura";
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
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var secret = new char[32];
            for (int i = 0; i < secret.Length; i++)
            {
                secret[i] = base32Chars[RandomNumberGenerator.GetInt32(base32Chars.Length)];
            }
            TotpSecretKey = new string(secret);
            TotpQrCodeUri = $"otpauth://totp/PhantomObscura:User?secret={TotpSecretKey}&issuer=PhantomObscura";
            StatusMessage = "TOTP secret generated. Scan the QR code with your authenticator app.";
        }

        private void GenerateSummary()
        {
            var selectedTier = GetSelectedProtectionTier();
            var effectiveTransport = GetEffectiveStorageTransport(selectedTier);
            var requestedTransport = GetRequestedStorageTransport(selectedTier);

            var summary = "Your Phantom Obscura vault will be created with the following settings:\n\n";
            summary += $"• Protection Tier: {SelectedSecurityLevel}\n";
            summary += $"• Effective Transport: {DescribeTransport(effectiveTransport)}\n";
            if (requestedTransport.HasValue && requestedTransport.Value != effectiveTransport)
                summary += $"• Requested Future Transport: {DescribeTransport(requestedTransport.Value)}\n";
            summary += $"• Storage Location: USB Drive ({SelectedUsbPath})\n";
            summary += $"• USB Device Binding: {(EnableUsbBinding ? "Enabled" : "Disabled")}\n";
            summary += $"• GUUID Binding: {(EnableGuuidBinding ? "Enabled" : "Disabled")}\n";
            summary += $"• Phantom Key: {(EnablePhantomKey ? "Enabled" : "Disabled")}\n";
            summary += $"• Encrypted Container: {(EnableEncryptedContainer ? $"Enabled ({EncryptedContainerSize})" : "Disabled")}\n";
            summary += selectedTier == VaultProtectionTier.BlackSecure
                ? "• Key Material: Device-bound raw transport with no external keyfile path\n"
                : $"• Keyfile: {(UseExistingKeyfile && !string.IsNullOrWhiteSpace(KeyfilePath) ? Path.GetFileName(KeyfilePath) : "Will be generated")}\n";
            summary += $"• Master Password: {(UsePassword ? "Configured" : "Not used")}\n";
            if (HasRemnants)
                summary += $"• Remnant Action: {SelectedRemnantAction}\n";
            summary += "\nReversible Measures: Provisioning metadata and canonical inner-container paths will be preserved for future tier migration.\n";
            summary += $"\nEncryption: AES-256-GCM with {(selectedTier == VaultProtectionTier.BlackSecure ? "raw-device transport wrapper" : "canonical container profile")}\n";

            SetupSummary = summary;
        }

        /// <summary>Raised when vault creation completes so the UI can transition to the loading window flow.</summary>
        public event EventHandler? VaultReadyForCreation;

        private async Task CompleteSetupAsync()
        {
            Log.Information("CompleteSetupAsync called, VaultReadyForCreation subscribers: {HasSubscribers}", VaultReadyForCreation != null);

            if (VaultReadyForCreation == null)
            {
                StatusMessage = "Unable to start vault creation. Please restart the wizard.";
                Log.Warning("VaultReadyForCreation event has no subscribers — cannot proceed");
                return;
            }

            try
            {
                // Signal the UI layer to open the loading window – actual creation
                // is now driven by ExecuteVaultCreationAsync called from the loading window.
                VaultReadyForCreation.Invoke(this, EventArgs.Empty);
                Log.Information("VaultReadyForCreation event fired successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "VaultReadyForCreation handler threw an exception");
                StatusMessage = $"Vault creation failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Performs the actual vault creation. Called by VaultCreationLoadingWindow
        /// so the animated progress can wrap each phase.
        /// </summary>
        public async Task ExecuteVaultCreationAsync()
        {
            string? stagingRoot = null;
            try
            {
                IsCompleting = true;
                StatusMessage = "Creating your vault...";
                Log.Information("ExecuteVaultCreationAsync starting");

                // ─── Determine vault path ───
                string vaultPath;
                string? volumePath = null;
                string? driveRoot = null;
                var selectedTier = GetSelectedProtectionTier();
                var effectiveTransport = GetEffectiveStorageTransport(selectedTier);
                var requestedTransport = GetRequestedStorageTransport(selectedTier);
                bool usePackedMasterVolume = effectiveTransport == VaultStorageTransport.PackedVolume;
                PhantomVault.UI.Services.SettingsService.Update(settings =>
                {
                    settings.DefaultVaultProtectionTier = selectedTier.ToString();
                });
                if (usePackedMasterVolume)
                {
                    stagingRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSetup", Guid.NewGuid().ToString("N"));
                }

                const string rootContainerRelativePath = "root/root.pvault";
                const string vaultContainerRelativePath = "vaults/vault.pvault";
                const string objectContainerRelativePath = "objects/objects.pvault";
                const string recoveryContainerRelativePath = "recovery/recovery.pvault";
                const string recoveryVaultWorkspaceRelativePath = "recovery/vault";
                const string bindingRecordRelativePath = "root/usb.binding.pmeta";
                const string recoveryRecordRelativePath = "recovery/recovery.record.pmeta";
                const string provisioningRecordRelativePath = "root/storage-tier.provisioning.pmeta";
                const string decoyDatabaseRelativePath = "decoy/decoy.database.pmeta";

                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    driveRoot = ExtractDriveRoot(SelectedUsbPath);
                    vaultPath = Path.Combine(driveRoot, ".phantom");
                    if (usePackedMasterVolume)
                    {
                        volumePath = Path.Combine(driveRoot, "system.bin");
                    }
                }
                else
                {
                    vaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        ".phantom");
                    if (usePackedMasterVolume)
                    {
                        volumePath = Path.Combine(vaultPath, "obscura.vol");
                    }
                }

                string workingRoot = usePackedMasterVolume ? stagingRoot! : vaultPath;
                string rootContainerPath = Path.Combine(workingRoot, rootContainerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string vaultContainerPath = Path.Combine(workingRoot, vaultContainerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string objectContainerPath = Path.Combine(workingRoot, objectContainerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string recoveryContainerPath = Path.Combine(workingRoot, recoveryContainerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string bindingRecordPath = Path.Combine(workingRoot, bindingRecordRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string recoveryRecordPath = Path.Combine(workingRoot, recoveryRecordRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string provisioningRecordPath = Path.Combine(workingRoot, provisioningRecordRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string decoyDatabasePath = Path.Combine(workingRoot, decoyDatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar));

                // ─── Wipe remnants FIRST (before creating directories) ───
                // Must happen before directory creation because the cleanup will
                // delete an empty .phantom dir — which is the one we're about to use.
                if (HasRemnants && SelectedRemnantAction == "WipeRemnants" && DetectedRemnants.Count > 0)
                {
                    StatusMessage = "Securely wiping remnant files...";
                    Log.Information("Wiping {Count} remnant file(s)", DetectedRemnants.Count);

                    // Collect parent .phantom directories so we can remove protections and clean up
                    var phantomDirsToClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var remnant in DetectedRemnants.ToList())
                    {
                        try
                        {
                            if (File.Exists(remnant.FilePath))
                            {
                                // Strip hardened attributes so secure-overwrite can open the file
                                VaultFileProtection.StripFileProtection(remnant.FilePath);

                                await SecureDeletionService.SecureDeleteFileAsync(
                                    remnant.FilePath,
                                    SecureDeletionService.DeletionMethod.StandardSecure);
                                Log.Information("Wiped remnant: {FilePath}", remnant.FilePath);

                                // Track the .phantom ancestor for directory cleanup
                                var phantomAncestor = VaultFileProtection.FindPhantomAncestor(remnant.FilePath);
                                if (phantomAncestor != null)
                                    phantomDirsToClean.Add(phantomAncestor);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to wipe remnant file: {FilePath}", remnant.FilePath);
                        }
                    }

                    // Remove the hardened .phantom directories if they are now empty
                    foreach (var cleanDir in phantomDirsToClean)
                    {
                        try
                        {
                            VaultFileProtection.StripDirectoryProtection(cleanDir);
                            // Delete empty subdirectories bottom-up
                            foreach (var sub in Directory.GetDirectories(cleanDir, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
                            {
                                if (!Directory.EnumerateFileSystemEntries(sub).Any())
                                {
                                    var di = new DirectoryInfo(sub);
                                    di.Attributes = FileAttributes.Normal;
                                    Directory.Delete(sub);
                                }
                            }
                            if (!Directory.EnumerateFileSystemEntries(cleanDir).Any())
                                Directory.Delete(cleanDir);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to clean up .phantom directory: {Dir}", cleanDir);
                        }
                    }

                    DetectedRemnants.Clear();
                    HasRemnants = false;
                    StatusMessage = "Remnant files wiped.";
                }

                // ─── Create vault directories (after remnant wipe so they aren't deleted) ───
                Directory.CreateDirectory(vaultPath);
                Directory.CreateDirectory(Path.GetDirectoryName(rootContainerPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(objectContainerPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(recoveryContainerPath)!);
                Directory.CreateDirectory(Path.Combine(workingRoot, recoveryVaultWorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(decoyDatabasePath)!);

                // Hide the .phantom folder so vault contents aren't visible in file explorers
                var phantomDirInfo = new DirectoryInfo(vaultPath);
                if (phantomDirInfo.Exists)
                    phantomDirInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;

                StatusMessage = "Creating vault directory...";
                Log.Information("Vault path: {VaultPath}", vaultPath);

                // ─── Generate vault salt (shared across all crypto operations) ───
                byte[] salt = _encryptionService.GenerateSalt(32);
                string? passphrase = UsePassword ? MasterPassword : null;

                // ─── Handle keyfile ───
                string? keyfilePath = null;
                bool usesExternalKeyfile = selectedTier != VaultProtectionTier.BlackSecure;
                if (usesExternalKeyfile && UseExistingKeyfile && KeyfileSelected)
                {
                    keyfilePath = KeyfilePath;
                    StatusMessage = "Using existing keyfile...";
                    Log.Information("Using existing keyfile: {KeyfilePath}", keyfilePath);
                }
                else if (usesExternalKeyfile)
                {
                    keyfilePath = Path.Combine(vaultPath, "vault.key");
                    await GenerateKeyfileAsync(keyfilePath);

                    // Encrypt the keyfile in-place: read raw → encrypt → overwrite with encrypted blob
                    await EncryptFileInPlaceAsync(keyfilePath, salt, passphrase);

                    KeyfilePath = keyfilePath;
                    StatusMessage = "Generated and encrypted keyfile...";
                    Log.Information("Generated and encrypted keyfile at: {KeyfilePath}", keyfilePath);
                }
                else
                {
                    KeyfilePath = null;
                    StatusMessage = "Black Secure uses password plus device-bound factors without an external keyfile path.";
                }

                // ─── USB device binding ───
                string? deviceId = null;
                string? bindingId = null;
                string? bindingGuid = null;
                string? blackSecurePhysicalDevicePath = null;
                if (selectedTier == VaultProtectionTier.BlackSecure &&
                    SelectedStorageLocation == "USB" &&
                    !string.IsNullOrEmpty(driveRoot) &&
                    !_blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromDriveRoot(driveRoot, out blackSecurePhysicalDevicePath))
                {
                    throw new InvalidOperationException("Unable to resolve the selected USB drive to a physical device for Black Secure provisioning.");
                }

                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(driveRoot) && EnableUsbBinding)
                {
                    try
                    {
                        bindingId = Guid.NewGuid().ToString("N");
                        bindingGuid = Guid.NewGuid().ToString("D");

                        if (EnablePhantomKey)
                        {
                            // PhantomKey: high-assurance binding with hidden device ID file
                            deviceId = _usbBindingService.InitializeHighAssuranceBinding(driveRoot, salt);
                            Log.Information("PhantomKey high-assurance binding established: {DeviceId}", deviceId);
                            StatusMessage = "PhantomKey high-assurance USB binding established...";
                        }
                        else
                        {
                            // Standard binding: compute device ID from drive properties
                            deviceId = _usbBindingService.ComputeDeviceId(
                                selectedTier == VaultProtectionTier.BlackSecure && !string.IsNullOrWhiteSpace(blackSecurePhysicalDevicePath)
                                    ? blackSecurePhysicalDevicePath
                                    : driveRoot);
                            Log.Information("Standard USB binding established: {DeviceId}", deviceId);
                            StatusMessage = "Bound to USB device...";
                        }

                        UsbDeviceId = deviceId;
                        if (string.IsNullOrEmpty(GuuidValue))
                        {
                            GuuidValue = DetectSystemGuuid();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not bind to USB device");
                        StatusMessage = $"Warning: Could not bind to USB device: {ex.Message}";
                    }
                }

                // ─── GUUID binding (motherboard hardware UUID) ───
                string? guuidValue = null;
                if (EnableGuuidBinding || (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(driveRoot)))
                {
                    try
                    {
                        guuidValue = DetectSystemGuuid();
                        if (!string.IsNullOrEmpty(guuidValue))
                        {
                            GuuidValue = guuidValue;

                            // Combine device ID with GUUID for stronger multi-factor binding
                            if (!string.IsNullOrEmpty(deviceId))
                            {
                                string combined = $"{deviceId}|GUUID:{guuidValue}";
                                byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
                                deviceId = Convert.ToHexString(hash);
                                UsbDeviceId = deviceId;
                                Log.Information("GUUID combined with USB device ID for multi-factor binding");
                            }
                            else
                            {
                                // GUUID-only binding (no USB binding)
                                byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"GUUID:{guuidValue}"));
                                deviceId = Convert.ToHexString(hash);
                            }

                            StatusMessage = "Hardware GUUID binding established...";
                            Log.Information("GUUID binding: {Guuid}", guuidValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "GUUID detection failed");
                        StatusMessage = $"Warning: GUUID binding failed: {ex.Message}";
                    }
                }

                // ─── Build manifest ───
                var manifest = new VaultManifest
                {
                    Version = 3,
                    VaultName = "PhantomObscura",
                    ContainerPath = vaultContainerRelativePath,
                    RootContainerPath = rootContainerRelativePath,
                    ObjectContainerPath = objectContainerRelativePath,
                    RecoveryContainerPath = recoveryContainerRelativePath,
                    BindingRecordPath = bindingRecordRelativePath,
                    RecoveryRecordPath = recoveryRecordRelativePath,
                    ProvisioningRecordPath = provisioningRecordRelativePath,
                    MasterVolumePath = volumePath,
                    DecoyDatabasePath = decoyDatabaseRelativePath,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    Description = $"Created with {SelectedSecurityLevel} protection tier",
                    SaltBase64 = Convert.ToBase64String(salt),
                    Algorithm = "AES-256-GCM",
                    KeyfilePath = keyfilePath,
                    DeviceId = deviceId,
                    UsbBindingId = bindingId,
                    UsbBindingGuid = bindingGuid,
                    Guuid = GuuidValue,
                    RequiresHardwareToken = EnablePhantomKey,
                    RequiresTotp = EnableTotp,
                    ProtectionTier = selectedTier,
                    EffectiveStorageTransport = effectiveTransport,
                    RequestedStorageTransport = requestedTransport,
                    SupportsReversibleTierMigration = true
                };

                // TOTP secret generation
                if (EnableTotp)
                {
                    manifest.TotpSecret = GenerateSecureTotpSecret();
                    StatusMessage = "Generated TOTP secret...";
                    Log.Information("TOTP secret generated");
                }

                var containerSpecs = new List<(string Path, long SizeBytes, VaultManifest? EmbeddedManifest)>
                {
                    (rootContainerPath, 32L * 1024 * 1024, manifest),
                    (vaultContainerPath, ParseContainerSize(EncryptedContainerSize), null),
                    (objectContainerPath, 128L * 1024 * 1024, null),
                    (recoveryContainerPath, 64L * 1024 * 1024, null)
                };

                // ─── Create mandatory three-container layout ───
                if (EnableEncryptedContainer)
                {
                    manifest.ContainerSizeBytes = containerSpecs[1].SizeBytes;

                    foreach (var containerSpec in containerSpecs)
                    {
                        StatusMessage = $"Creating {Path.GetFileNameWithoutExtension(containerSpec.Path)} container...";
                        Log.Information("Creating encrypted container: {Size} bytes at {Path}", containerSpec.SizeBytes, containerSpec.Path);

                        await _containerService.CreateContainerAsync(
                            containerSpec.Path,
                            containerSpec.SizeBytes,
                            passphrase,
                            keyfilePath,
                            manifest: containerSpec.EmbeddedManifest,
                            progress: null,
                            cancellationToken: CancellationToken.None);
                    }

                    StatusMessage = "Three-container layout created...";
                    Log.Information("Encrypted three-container layout created successfully");
                }
                else
                {
                    manifest.ContainerSizeBytes = containerSpecs[1].SizeBytes;

                    foreach (var containerSpec in containerSpecs)
                    {
                        StatusMessage = $"Creating {Path.GetFileNameWithoutExtension(containerSpec.Path)} container...";
                        await _containerService.CreateContainerAsync(
                            containerSpec.Path,
                            containerSpec.SizeBytes,
                            passphrase,
                            keyfilePath,
                            manifest: containerSpec.EmbeddedManifest,
                            progress: null,
                            cancellationToken: CancellationToken.None);
                    }
                    Log.Information("Baseline three-container layout created");
                }

                if (!string.IsNullOrEmpty(bindingRecordPath))
                {
                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        bindingRecordPath,
                        new UsbBindingRecord
                        {
                            BindingId = bindingId ?? Guid.NewGuid().ToString("N"),
                            BindingGuid = bindingGuid ?? Guid.NewGuid().ToString("D"),
                            DeviceId = deviceId ?? string.Empty,
                            Guuid = GuuidValue,
                            DriveRoot = driveRoot ?? string.Empty,
                            RootContainerPath = rootContainerRelativePath,
                            VaultContainerPath = vaultContainerRelativePath,
                            ObjectContainerPath = objectContainerRelativePath,
                            CreatedUtc = DateTimeOffset.UtcNow
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        "usb-binding");
                }

                if (!string.IsNullOrEmpty(recoveryRecordPath))
                {
                    var recoveryRecord = new RecoveryVaultRecord
                    {
                        BindingId = bindingId ?? string.Empty,
                        BindingGuid = bindingGuid ?? string.Empty,
                        RecoveryContainerPath = recoveryContainerRelativePath,
                        RecoveryVaultPath = recoveryVaultWorkspaceRelativePath,
                        ProtectionTier = selectedTier,
                        EffectiveStorageTransport = effectiveTransport,
                        RecoveryContainerSizeBytes = containerSpecs[3].SizeBytes,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Notes = $"Recovery artifacts are bound to the USB recovery container. PhantomRecovery workspace contract: {recoveryVaultWorkspaceRelativePath}"
                    };

                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        recoveryRecordPath,
                        recoveryRecord,
                        manifest,
                        passphrase,
                        keyfilePath,
                        "recovery-record");

                    var bootstrapPendingDirectory = Path.Combine(
                        workingRoot,
                        recoveryVaultWorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar),
                        ".suite-bootstrap",
                        "pending");
                    Directory.CreateDirectory(bootstrapPendingDirectory);

                    var bootstrapOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(
                        Path.Combine(bootstrapPendingDirectory, "obscura-vault-summary.json"),
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            Source = "Phantom.Obscura",
                            manifest.VaultName,
                            manifest.Version,
                            manifest.CreatedUtc,
                            manifest.Description,
                            manifest.ProtectionTier,
                            manifest.EffectiveStorageTransport,
                            manifest.RequestedStorageTransport,
                            manifest.SupportsReversibleTierMigration,
                            RecoveryContainerPath = recoveryContainerRelativePath,
                            RecoveryRecordPath = recoveryRecordRelativePath,
                            RecoveryVaultPath = recoveryVaultWorkspaceRelativePath,
                            manifest.UsbBindingId,
                            manifest.UsbBindingGuid,
                            manifest.DeviceId,
                            manifest.Guuid,
                            manifest.RequiresHardwareToken,
                            manifest.RequiresTotp
                        }, bootstrapOptions));

                    File.WriteAllText(
                        Path.Combine(bootstrapPendingDirectory, "recovery-record-summary.json"),
                        System.Text.Json.JsonSerializer.Serialize(recoveryRecord, bootstrapOptions));

                    File.WriteAllLines(
                        Path.Combine(bootstrapPendingDirectory, "README.txt"),
                        new[]
                        {
                            "Phantom.Obscura created this Recovery workspace for Phantom.Recovery.",
                            "These bootstrap files are suite metadata only and can be safely imported into the encrypted Recovery vault.",
                            $"Recovery workspace: {recoveryVaultWorkspaceRelativePath}",
                            $"Recovery container: {recoveryContainerRelativePath}"
                        });
                }

                await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                    provisioningRecordPath,
                    new StorageTierProvisioningRecord
                    {
                        ProtectionTier = selectedTier,
                        EffectiveTransport = effectiveTransport,
                        RequestedTransport = requestedTransport,
                        ReversibleMigrationEnabled = true,
                        CurrentRootContainerPath = rootContainerRelativePath,
                        CurrentVaultContainerPath = vaultContainerRelativePath,
                        CurrentObjectContainerPath = objectContainerRelativePath,
                        CurrentRecoveryContainerPath = recoveryContainerRelativePath,
                        CurrentMasterVolumePath = volumePath,
                        RecommendedRollbackTransport = VaultStorageTransport.FileSystem,
                        RecoveryWorkspacePath = recoveryVaultWorkspaceRelativePath,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Notes = usePackedMasterVolume
                            ? "Provisioned with the canonical inner container layout packed into a master volume. Reversible migration to a direct-layout profile can be performed by unpacking the same inner paths."
                            : "Provisioned with the canonical direct container layout. Reversible migration to a packed-volume profile can be performed without changing the inner container paths."
                    },
                    manifest,
                    passphrase,
                    keyfilePath,
                    "storage-tier-provisioning");

                await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                    decoyDatabasePath,
                    BuildDecoyDatabase(),
                    manifest,
                    passphrase,
                    keyfilePath,
                    "decoy-database");

                if (selectedTier == VaultProtectionTier.BlackSecure)
                {
                    StatusMessage = "Writing Black Secure raw-device volume...";
                    await _blackSecureRawVolumeService.CreateVolumeFromDirectoryAsync(
                        blackSecurePhysicalDevicePath!,
                        stagingRoot!,
                        CancellationToken.None);
                    Directory.Delete(stagingRoot!, true);
                    stagingRoot = null;
                }
                else if (usePackedMasterVolume)
                {
                    StatusMessage = "Packing master Obscura volume...";
                    var obscuraVolumeService = new ObscuraVolumeService();
                    await obscuraVolumeService.CreateVolumeFromDirectoryAsync(volumePath!, stagingRoot!, CancellationToken.None);
                    Directory.Delete(stagingRoot!, true);
                    stagingRoot = null;
                }
                else
                {
                    StatusMessage = "Writing direct canonical container layout...";
                }

                // Manifest is now embedded inside the container (v3 format) —
                // no separate vault.manifest.encrypted file on disk.

                // ─── Harden vault files against casual deletion ───
                // Files can only be removed via the in-app remnant wipe which
                // strips these protections first, then securely overwrites.
                VaultFileProtection.HardenVaultFiles(vaultPath);

                StatusMessage = "Vault created successfully!";
                Log.Information("ExecuteVaultCreationAsync completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Vault creation failed");
                StatusMessage = $"Error creating vault: {ex.Message}";
                if (!string.IsNullOrEmpty(stagingRoot) && Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, true);
                    }
                    catch
                    {
                        // Best effort cleanup only.
                    }
                }
                throw; // Re-throw so the loading window can show the error
            }
            finally
            {
                IsCompleting = false;
            }
        }

        /// <summary>
        /// Extracts the drive root path (e.g. "E:\") from a display string like "E:\ - My USB (16 GB)".
        /// </summary>
        private static VaultDatabase BuildDecoyDatabase()
        {
            var generator = new DecoyCredentialGenerator();
            var decoyCredentials = generator.GenerateDecoyCredentials(24);
            var groups = decoyCredentials
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "General" : c.Group)
                .Select(group => new VaultGroup
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = group.Key,
                    Icon = "folder",
                    Entries = group.ToList()
                })
                .ToList();

            return new VaultDatabase
            {
                VaultName = "Personal Vault",
                Description = "Decoy vault for tamper response",
                Created = DateTime.UtcNow.AddDays(-365),
                Groups = groups
            };
        }

        private static string ExtractDriveRoot(string usbPathDisplay)
        {
            string driveRoot = usbPathDisplay.Length >= 2 ? usbPathDisplay.Substring(0, 3) : usbPathDisplay;
            if (usbPathDisplay.Contains(" - "))
            {
                driveRoot = usbPathDisplay.Split(" - ")[0].Trim();
                if (!driveRoot.EndsWith("\\")) driveRoot += "\\";
            }
            return driveRoot;
        }

        /// <summary>
        /// Parses a human-readable container size string (e.g. "256 MB", "1 GB") into bytes.
        /// </summary>
        private static long ParseContainerSize(string sizeString)
        {
            if (string.IsNullOrWhiteSpace(sizeString))
                return 256L * 1024 * 1024; // Default 256 MB

            var trimmed = sizeString.Trim().ToUpperInvariant();

            // Try to split into number and unit
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && double.TryParse(parts[0], out double value))
            {
                return parts[1] switch
                {
                    "GB" => (long)(value * 1024 * 1024 * 1024),
                    "MB" => (long)(value * 1024 * 1024),
                    "KB" => (long)(value * 1024),
                    _ => (long)(value * 1024 * 1024) // Default to MB
                };
            }

            // Try pure numeric (assume MB)
            if (double.TryParse(trimmed, out double numericValue))
                return (long)(numericValue * 1024 * 1024);

            return 256L * 1024 * 1024; // Fallback 256 MB
        }

        /// <summary>
        /// Detects the system's motherboard hardware UUID via WMI (Win32_ComputerSystemProduct).
        /// Returns null on non-Windows or if detection fails.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static string? DetectSystemGuuid()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var uuid = mo["UUID"]?.ToString();
                    // Filter out dummy/empty UUIDs that some BIOSes report
                    if (!string.IsNullOrEmpty(uuid) &&
                        uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" &&
                        uuid != "00000000-0000-0000-0000-000000000000")
                    {
                        return uuid;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to detect system GUUID via WMI");
            }

            return null;
        }

        /// <summary>
        /// Encrypts a file in-place using AES-256-GCM derived from the vault salt.
        /// The result is written as: [12-byte nonce][16-byte tag][ciphertext].
        /// </summary>
        private async Task EncryptFileInPlaceAsync(string filePath, byte[] salt, string? passphrase)
        {
            byte[] plainBytes = await File.ReadAllBytesAsync(filePath);
            try
            {
                // Derive an encryption key specifically for file wrapping
                byte[] wrappingKey = HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    salt,
                    32,
                    System.Text.Encoding.UTF8.GetBytes("PhantomVault.FileEncrypt.v1"),
                    System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(filePath)));

                try
                {
                    byte[] nonce = new byte[12];
                    RandomNumberGenerator.Fill(nonce);
                    byte[] ciphertext = new byte[plainBytes.Length];
                    byte[] tag = new byte[16];

                    using (var aes = new AesGcm(wrappingKey, 16))
                    {
                        aes.Encrypt(nonce, plainBytes, ciphertext, tag);
                    }

                    // Write encrypted envelope: nonce + tag + ciphertext
                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await fs.WriteAsync(nonce);
                    await fs.WriteAsync(tag);
                    await fs.WriteAsync(ciphertext);
                    await fs.FlushAsync();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(wrappingKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        /// <summary>
        /// Creates an encrypted empty vault file (for when no container is requested).
        /// Uses AES-256-GCM so that even the empty vault file is encrypted on disk.
        /// </summary>
        private async Task CreateEncryptedEmptyVaultAsync(string containerPath, byte[] salt, string? passphrase)
        {
            // Create a small encrypted placeholder with vault metadata
            byte[] metadata = System.Text.Encoding.UTF8.GetBytes(
                $"{{\"type\":\"phantom-vault\",\"created\":\"{DateTimeOffset.UtcNow:O}\",\"empty\":true}}");

            byte[] wrappingKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                salt,
                32,
                System.Text.Encoding.UTF8.GetBytes("PhantomVault.EmptyVault.v1"),
                System.Text.Encoding.UTF8.GetBytes("vault.pvault"));

            try
            {
                byte[] nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);
                byte[] ciphertext = new byte[metadata.Length];
                byte[] tag = new byte[16];

                using (var aes = new AesGcm(wrappingKey, 16))
                {
                    aes.Encrypt(nonce, metadata, ciphertext, tag);
                }

                using var fs = new FileStream(containerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await fs.WriteAsync(nonce);
                await fs.WriteAsync(tag);
                await fs.WriteAsync(ciphertext);
                await fs.FlushAsync();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
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

        [RelayCommand]
        private void SelectSecurityLevel(string level)
        {
            SelectedSecurityLevel = level;
        }

        partial void OnSelectedSecurityLevelChanged(string value)
        {
            var selected = SecurityLevels.FirstOrDefault(s => s.Name == value);
            if (selected != null)
            {
                SecurityDescription = selected.Description;
            }

            foreach (var level in SecurityLevels)
            {
                level.IsSelected = level.Name == value;
            }

            var selectedTier = GetSelectedProtectionTier();
            if (selectedTier != VaultProtectionTier.StandardSecure)
            {
                EnableUsbBinding = true;
            }

            if (selectedTier == VaultProtectionTier.BlackSecure)
            {
                EnableGuuidBinding = true;
                UsePassword = true;
                UseExistingKeyfile = false;
                KeyfilePath = null;
                KeyfileSelected = false;
                KeyfileStatus = "Black Secure uses password plus device-bound factors; no external keyfile path is provisioned.";
            }
            else if (!UseExistingKeyfile)
            {
                KeyfileStatus = "A new keyfile will be generated";
            }

            OnPropertyChanged(nameof(IsBlackSecureSelected));
            OnPropertyChanged(nameof(SupportsExternalKeyfile));
            OnPropertyChanged(nameof(ShowGeneratedKeyfileInfo));
            OnPropertyChanged(nameof(ShowPasswordToggle));
            OnPropertyChanged(nameof(KeyMaterialSectionTitle));
            OnPropertyChanged(nameof(KeyMaterialSectionSubtitle));
            OnPropertyChanged(nameof(KeyMaterialDescription));
            OnPropertyChanged(nameof(PasswordSectionTitle));
            OnPropertyChanged(nameof(PasswordSectionDescription));
            OnPropertyChanged(nameof(PasswordToggleText));
        }

        partial void OnUseExistingKeyfileChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowGeneratedKeyfileInfo));
        }

        private VaultProtectionTier GetSelectedProtectionTier()
            => SelectedSecurityLevel switch
            {
                "Standard Secure" => VaultProtectionTier.StandardSecure,
                "Black Secure" => VaultProtectionTier.BlackSecure,
                _ => VaultProtectionTier.StealthSecure
            };

        private static VaultStorageTransport GetEffectiveStorageTransport(VaultProtectionTier protectionTier)
            => protectionTier switch
            {
                VaultProtectionTier.StandardSecure => VaultStorageTransport.FileSystem,
                VaultProtectionTier.BlackSecure => VaultStorageTransport.RawDevice,
                _ => VaultStorageTransport.PackedVolume
            };

        private static VaultStorageTransport? GetRequestedStorageTransport(VaultProtectionTier protectionTier)
            => protectionTier == VaultProtectionTier.BlackSecure
                ? VaultStorageTransport.RawDevice
                : null;

        private static string DescribeTransport(VaultStorageTransport transport)
            => transport switch
            {
                VaultStorageTransport.FileSystem => "Direct filesystem-backed containers",
                VaultStorageTransport.PackedVolume => "Packed master volume",
                VaultStorageTransport.RawDevice => "Raw-device layout",
                _ => transport.ToString()
            };

        partial void OnMasterPasswordChanged(string value)
        {
            AnalyzePasswordStrength();
        }

        partial void OnConfirmPasswordChanged(string value)
        {
            PasswordsMatch = MasterPassword == ConfirmPassword;
        }
    }

    public partial class SecurityLevelOption : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] Features { get; set; } = Array.Empty<string>();
        public string RecommendedFor { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isSelected;
    }

    public class DetectedRemnant
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
    }

    // ───────────────────────────────────────────────────────
    //  Vault‑file hardening / stripping helpers
    // ───────────────────────────────────────────────────────

    public static partial class VaultFileProtection
    {
        /// <summary>
        /// Hardens every file and subdirectory beneath <paramref name="vaultPath"/>
        /// and its <c>.phantom</c> ancestor with Hidden+System+ReadOnly attributes
        /// and a DENY‑DELETE ACL for BUILTIN\Users on Windows.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void HardenVaultFiles(string vaultPath)
        {
            // Harden individual files
            foreach (var file in Directory.EnumerateFiles(vaultPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file,
                        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to harden file: {File}", file);
                }
            }

            // Harden subdirectories (vaults, manifests, etc.)
            foreach (var dir in Directory.EnumerateDirectories(vaultPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    di.Attributes = FileAttributes.ReadOnly
                                    | FileAttributes.Hidden | FileAttributes.System;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to harden directory: {Dir}", dir);
                }
            }

            // Harden the vaults directory itself
            try
            {
                var vaultsInfo = new DirectoryInfo(vaultPath);
                vaultsInfo.Attributes = FileAttributes.ReadOnly
                                        | FileAttributes.Hidden | FileAttributes.System;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to harden vaults directory");
            }

            // Apply DENY‑DELETE ACL on the .phantom folder to block casual deletion
            var phantomDirInfo = new DirectoryInfo(vaultPath);
            if (phantomDirInfo.Exists)
            {
                try
                {
                    phantomDirInfo.Attributes = FileAttributes.ReadOnly
                                               | FileAttributes.Hidden | FileAttributes.System;

                    if (OperatingSystem.IsWindows())
                    {
                        var acl = phantomDirInfo.GetAccessControl();
                        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                        // Deny delete of this folder and its children
                        acl.AddAccessRule(new FileSystemAccessRule(
                            users,
                            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None,
                            AccessControlType.Deny));
                        phantomDirInfo.SetAccessControl(acl);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to set DENY‑DELETE ACL on .phantom directory");
                }
            }
        }

        /// <summary>
        /// Strips hardened attributes from a single file so it can be overwritten and deleted.
        /// </summary>
        public static void StripFileProtection(string filePath)
        {
            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to strip file protection: {File}", filePath);
            }
        }

        /// <summary>
        /// Strips the DENY‑DELETE ACL and hardened attributes from a <c>.phantom</c> directory
        /// tree so that the remnant wipe can remove it.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void StripDirectoryProtection(string phantomDirPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(phantomDirPath);
                dirInfo.Attributes = FileAttributes.Normal;

                if (OperatingSystem.IsWindows())
                {
                    // Remove any DENY rules for BUILTIN\Users
                    var acl = dirInfo.GetAccessControl();
                    var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                    acl.RemoveAccessRuleAll(new FileSystemAccessRule(
                        users,
                        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                        AccessControlType.Deny));
                    dirInfo.SetAccessControl(acl);
                }

                // Also strip attributes on all subdirectories
                foreach (var sub in Directory.EnumerateDirectories(phantomDirPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        new DirectoryInfo(sub).Attributes = FileAttributes.Normal;
                    }
                    catch { }
                }

                // Strip attributes on remaining files
                foreach (var file in Directory.EnumerateFiles(phantomDirPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to strip directory protection: {Dir}", phantomDirPath);
            }
        }

        /// <summary>
        /// Walks up from a file path looking for a <c>.phantom</c> ancestor directory.
        /// </summary>
        public static string? FindPhantomAncestor(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (Path.GetFileName(dir).Equals(".phantom", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
