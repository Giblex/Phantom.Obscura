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
using System.Text;
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
using PhantomVault.Core.Utils;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run setup wizard that guides new users through
    /// initial configuration of Phantom Obscura.
    /// </summary>
    public partial class SetupWizardViewModel : ObservableObject
    {
        private const int GeneratedKeyfileSizeBytes = 256 * 1024;
        private Window? _ownerWindow;
        private readonly DialogService _dialogService = new();
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly UsbBindingService _usbBindingService;
        private readonly PhantomContainerService _containerService;
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private EntropyKeyfileGenerator? _entropyKeyfileGenerator;
        private byte[]? _stagedGeneratedKeyfileBytes;
        private bool _revertingCriticalToggle;
        private bool _generatedPasswordWasAutoCreated;
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
        private string _vaultName = "My Vault";

        [ObservableProperty]
        private bool _acceptedTerms;

        // Step 2: Security Level
        [ObservableProperty]
        private string _selectedSecurityLevel = "Ghost Secured";

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

        /// <summary>
        /// Maps display string to (driveLetter, volumeLabel, deviceId, sizeGb).
        /// Used to validate USB device immutability and prevent device swapping.
        /// </summary>
        private Dictionary<string, (string driveLetter, string volumeLabel, string deviceId, long sizeGb)> _usbDeviceMap = new();

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

        [ObservableProperty]
        private string _keyfileGenerationStatus = "Move your pointer across the entropy field until the keyfile can be sealed.";

        [ObservableProperty]
        private int _entropyCollectedBits;

        [ObservableProperty]
        private int _entropyRequiredBits = 256;

        [ObservableProperty]
        private int _entropySampleCount;

        [ObservableProperty]
        private bool _entropyKeyfileSealed;

        [ObservableProperty]
        private bool _revealMasterPassword;

        [ObservableProperty]
        private bool _revealConfirmPassword;

        // USB Binding
        [ObservableProperty]
        private string? _usbSerialNumber;

        [ObservableProperty]
        private string? _usbDeviceId;

        [ObservableProperty]
        private bool _revealUsbDeviceId;

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
        private string _encryptedContainerSize = "1 GB";

        // USB Remnant Detection
        [ObservableProperty]
        private ObservableCollection<DetectedRemnant> _detectedRemnants = new();

        [ObservableProperty]
        private bool _hasRemnants;

        [ObservableProperty]
        private string _selectedRemnantAction = "WipeRemnants";

        [ObservableProperty]
        private string _remainingUsbSpaceDisplay = string.Empty;

        /// <summary>True when CurrentStep is the additional authentication step.</summary>
        public bool IsAuthenticationStep => CurrentStep == 6;

        /// <summary>True when CurrentStep is the dynamically-positioned remnant step (step 7, only when HasRemnants).</summary>
        public bool IsRemnantStep => HasRemnants && CurrentStep == 7;

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

        public string MasterPasswordRevealGlyph => RevealMasterPassword ? "Hide" : "Reveal";
        public string ConfirmPasswordRevealGlyph => RevealConfirmPassword ? "Hide" : "Reveal";
        public string DisplayUsbDeviceId => string.IsNullOrWhiteSpace(UsbDeviceId)
            ? "Unavailable"
            : RevealUsbDeviceId
                ? UsbDeviceId
                : MaskSensitiveIdentifier(UsbDeviceId);
        public string UsbDeviceIdToggleText => RevealUsbDeviceId ? "Hide" : "Reveal";

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
                RecommendedFor = "Default supportable baseline",
                FriendlySummary = "Like keeping valuables in a sturdy locked drawer. Simple, solid, and easy to live with.",
                SecurityIncreasePercent = 35,
                SecurityHelpText = "Standard Secure is the straightforward option. Think of it like a strong locked drawer: your stuff is protected, it is easy to understand, and support and recovery are simpler."
            },
            new SecurityLevelOption
            {
                Name = "Ghost Secured",
                Description = "Recommended for most users. Packs the canonical container layout into a concealed master volume while preserving reversibility.",
                Features = new[]
                {
                    "Single packed master volume built from the same canonical inner layout",
                    "Reduced visible structure and better artifact minimisation",
                    "Clean migration path back to Standard or forward to Phantom Secured"
                },
                RecommendedFor = "Recommended",
                FriendlySummary = "Like hiding that locked drawer behind a bookcase too. Harder to notice, still practical to use.",
                SecurityIncreasePercent = 68,
                SecurityHelpText = "Ghost Secured adds concealment as well as protection. It is like hiding the locked drawer behind a bookcase, so a snoop has a harder time even noticing where to look.",
                IsSelected = true
            },
            new SecurityLevelOption
            {
                Name = "Phantom Secured",
                Description = "Expert profile that writes the canonical vault layout directly to the USB device as a raw Phantom Secured transport.",
                Features = new[]
                {
                    "USB is bound and reopened through the physical device path instead of a browsable filesystem",
                    "Requires a master password and device-bound factors without an external keyfile path",
                    "Provisioned with reversible migration metadata and rollback-ready storage records"
                },
                RecommendedFor = "Expert / adversarial",
                FriendlySummary = "Like storing everything in a bunker that forgot how to be obvious. Strongest, but definitely stricter.",
                SecurityIncreasePercent = 88,
                SecurityHelpText = "Phantom Secured is the bunker option. It gives the most aggressive hardening and concealment, but it expects you to be comfortable with a stricter, more expert-oriented setup."
            }
        };

        public bool IsBlackSecureSelected => GetSelectedProtectionTier() == VaultProtectionTier.BlackSecure;
        public bool SupportsExternalKeyfile => !IsBlackSecureSelected;
        public bool ShowGeneratedKeyfileInfo => SupportsExternalKeyfile && !UseExistingKeyfile;
        public bool RequiresGeneratedKeyfileEntropy => ShowGeneratedKeyfileInfo;
        public int EntropyProgressPercent => Math.Min(100, (EntropyCollectedBits * 100) / Math.Max(1, EntropyRequiredBits));
        public bool CanSealEntropyKeyfile => RequiresGeneratedKeyfileEntropy && !EntropyKeyfileSealed && (_entropyKeyfileGenerator?.CanFinalize ?? false);
        public bool ShowPasswordToggle => !IsBlackSecureSelected;

        public WindowsHelloSettingsViewModel WindowsHelloOnboarding { get; }
        public PasskeySettingsViewModel PasskeyOnboarding { get; }
        public TotpSettingsViewModel TotpOnboarding { get; }
        public string KeyMaterialSectionTitle => IsBlackSecureSelected ? "Device-Bound Authentication" : "Keyfile Configuration";
        public string KeyMaterialSectionSubtitle => IsBlackSecureSelected
            ? "Phantom Secured disables the external keyfile path and binds the vault directly to the selected USB device."
            : "Required - Your vault will be secured with a unique keyfile";
        public string KeyMaterialDescription => IsBlackSecureSelected
            ? "Phantom Secured uses a raw-device transport with USB binding, provisioning metadata, and a required master password. There is no external keyfile file to browse or store separately."
            : "A keyfile is a cryptographic file that acts as your primary authentication method. Store it securely on your USB drive or in a safe location. Without this file, your vault cannot be accessed.";
        public string PasswordSectionTitle => IsBlackSecureSelected ? "Required Master Password" : "Optional Master Password";
        public string PasswordSectionDescription => IsBlackSecureSelected
            ? "Phantom Secured requires a master password because the USB does not expose a browsable filesystem or external keyfile path."
            : "Add an extra layer of protection with a password";
        public string PasswordToggleText => IsBlackSecureSelected
            ? "Phantom Secured requires a master password"
            : "Enable master password (recommended for extra security)";
        public string PhantomKeyBridgeLocationDescription
        {
            get
            {
                var selectedTier = GetSelectedProtectionTier();
                return selectedTier switch
                {
                    VaultProtectionTier.BlackSecure => $"Phantom Key stays in its own sibling bridge workspace at {PhantomKeyBridgeContract.WorkspaceRelativePath} inside the raw-device transport.",
                    VaultProtectionTier.StealthSecure => $"Phantom Key stays in its own sibling bridge workspace at {PhantomKeyBridgeContract.WorkspaceRelativePath} inside the concealed master transport.",
                    _ => string.IsNullOrWhiteSpace(SelectedUsbPath)
                        ? $"Phantom Key stays in its own encrypted bridge workspace at .phantom/{PhantomKeyBridgeContract.WorkspaceRelativePath}."
                        : $"Phantom Key stays in its own encrypted bridge workspace at {Path.Combine(ExtractDriveRoot(SelectedUsbPath), ".phantom", "vaults", "phantomkey")}."
                };
            }
        }

        public string PhantomKeyTrustBoundarySummary =>
            "Obscura consumes Phantom Key through sealed bridge records only. No passkeys, private keys, recovery secrets, or raw credential material are copied into the vault setup flow.";

        public SetupWizardViewModel()
        {
            // Initialize services
            _encryptionService = new EncryptionService();
            _containerService = new PhantomContainerService(_encryptionService);
            _manifestService = new ManifestService(_encryptionService, _containerService);
            _usbBindingService = new UsbBindingService();
            _usbArtifactProtectionService = new UsbArtifactProtectionService(_encryptionService);
            _blackSecureRawVolumeService = new BlackSecureRawVolumeService();
            WindowsHelloOnboarding = new WindowsHelloSettingsViewModel();
            PasskeyOnboarding = new PasskeySettingsViewModel();
            TotpOnboarding = new TotpSettingsViewModel
            {
                VaultName = EffectiveVaultName
            };

            var settings = PhantomVault.UI.Services.SettingsService.Load();
            SelectedSecurityLevel = settings.DefaultVaultProtectionTier switch
            {
                nameof(VaultProtectionTier.StandardSecure) => "Standard Secure",
                nameof(VaultProtectionTier.BlackSecure) => "Phantom Secured",
                _ => "Ghost Secured"
            };

            if (SupportsExternalKeyfile && !UseExistingKeyfile)
            {
                InitializeEntropyKeyfileGenerator();
            }

            EnableGuuidBinding = true;
            EnableEncryptedContainer = true;
        }

        /// <summary>
        /// Sets the owner window for file dialogs.
        /// </summary>
        public void SetOwnerWindow(Window owner)
        {
            _ownerWindow = owner;
            WindowsHelloOnboarding.SetOwnerWindow(owner);
            PasskeyOnboarding.SetOwnerWindow(owner);
            TotpOnboarding.SetOwnerWindow(owner);
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
            InitializeEntropyKeyfileGenerator();
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
                case 3: // PhantomKey Setup
                    OnPropertyChanged(nameof(IsBlackSecureSelected));
                    break;

                case 4: // Storage Location
                    await DetectUsbDrivesAsync();
                    // Detect USB serial when USB is selected
                    await DetectUsbSerialAsync();
                    break;

                case 5: // Keyfile & Password
                    PrepareGeneratedKeyfileFlow();
                    break;

                case 6: // Additional authentication
                    TotpOnboarding.VaultName = EffectiveVaultName;
                    await DetectWindowsHelloAsync();
                    await DetectPasskeysAsync();
                    break;

                default:
                    // Summary is always the last step (7 or 8 depending on remnants)
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
                    if (string.IsNullOrWhiteSpace(VaultName))
                    {
                        StatusMessage = "Please enter a vault name to continue.";
                        return false;
                    }

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
                    if (SupportsExternalKeyfile && UseExistingKeyfile && !KeyfileSelected)
                    {
                        StatusMessage = "Please select an existing keyfile before continuing.";
                        return false;
                    }

                    if (RequiresGeneratedKeyfileEntropy && !EntropyKeyfileSealed)
                    {
                        StatusMessage = "Collect and seal keyfile entropy before continuing.";
                        return false;
                    }

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
                        StatusMessage = "Phantom Secured requires a master password because the USB will not expose a browsable filesystem or external keyfile path.";
                        return false;
                    }
                    break;

                case 6: // Additional authentication
                    if (EnableWindowsHello && !WindowsHelloOnboarding.IsBiometricEnrolled)
                    {
                        StatusMessage = "Complete Windows Hello enrollment or switch it off before continuing.";
                        return false;
                    }

                    if (EnablePasskeys && !PasskeyOnboarding.HasRegisteredPasskey)
                    {
                        StatusMessage = "Register a device passkey or switch the passkey requirement off before continuing.";
                        return false;
                    }

                    if (EnableTotp && (!TotpOnboarding.HasTotpSecret || !TotpOnboarding.IsTotpEnabled))
                    {
                        StatusMessage = "Generate and verify your TOTP authenticator before continuing.";
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
                3 => "PhantomKey Bridge Setup",
                4 => "Select Storage Location",
                5 => "Keyfile & Password",
                6 => "Additional Authentication",
                _ when IsRemnantStep => "Remnant Actions",
                _ when IsReviewStep => "Review and Complete",
                _ => "Setup"
            };

            // Notify computed step-visibility properties
            OnPropertyChanged(nameof(IsAuthenticationStep));
            OnPropertyChanged(nameof(IsRemnantStep));
            OnPropertyChanged(nameof(IsReviewStep));

            CanGoBack = CurrentStep > 1;
            CanGoNext = true;
            NextButtonText = CurrentStep == TotalSteps ? "Create Vault" : "Next";
        }

        private async Task DetectUsbDrivesAsync()
        {
            var (detectedDrives, usbMap) = await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives();
                var results = new List<string>();
                var map = new Dictionary<string, (string driveLetter, string volumeLabel, string deviceId, long sizeGb)>();

                foreach (var drive in drives)
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        var driveLetter = drive.Name.TrimEnd('\\');
                        var volumeLabel = drive.VolumeLabel;
                        var sizeGb = drive.TotalSize / 1024 / 1024 / 1024;

                        // Get physical device ID using WMI for device binding
                        var deviceId = GetPhysicalDeviceId(driveLetter);

                        // Display format: "ID: xxxxxxxx (C:) VolumeLabel [123 GB]"
                        var displayName = $"ID: {deviceId[..8]} ({driveLetter}) {volumeLabel} [{sizeGb} GB]";
                        results.Add(displayName);

                        map[displayName] = (driveLetter, volumeLabel, deviceId, sizeGb);
                    }
                }

                return (results, map);
            });

            _usbDeviceMap = usbMap;

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

        /// <summary>
        /// Gets the physical device ID for a USB drive using WMI.
        /// Uses volume serial number as the primary identifier.
        /// Falls back to a hash of drive properties if WMI fails.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static string GetPhysicalDeviceId(string driveLetter)
        {
            try
            {
                // Validate that driveLetter is a single alpha character before embedding in WQL
                var letter = driveLetter.TrimEnd('\\', '/').TrimEnd(':');
                if (letter.Length != 1 || !char.IsLetter(letter[0]))
                    throw new ArgumentException($"Unexpected drive letter value: {driveLetter}");

                // Try using WMI to get volume serial number
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                var query = new ObjectQuery($"SELECT SerialNumber FROM Win32_LogicalDisk WHERE Name = '{letter}:'");
                var searcher = new ManagementObjectSearcher(scope, query);

                foreach (var disk in searcher.Get())
                {
                    var serialNumber = disk["SerialNumber"]?.ToString();
                    if (!string.IsNullOrEmpty(serialNumber))
                    {
                        return serialNumber;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "WMI query failed for drive {Drive}, falling back to property hash", driveLetter);
            }

            // Fallback: generate hash from drive properties
            try
            {
                var drive = new DriveInfo(driveLetter);
                var props = $"{drive.Name}{drive.VolumeLabel}{drive.TotalSize}{drive.DriveFormat}";
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(props));
                return Convert.ToHexString(hash);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not compute device ID from drive properties, using stable letter hash");
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(driveLetter.ToUpperInvariant()));
                return Convert.ToHexString(hash);
            }
        }

        private async Task ScanForRemnantsAsync()
        {
            var driveRoot = ExtractDriveRoot(SelectedUsbPath);
            if (string.IsNullOrEmpty(driveRoot))
                return;

            var scanResults = await Task.Run(() =>
            {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            if (!seenPaths.Add(file))
                                continue;

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
                    catch (UnauthorizedAccessException)
                    {
                        Log.Debug("Skipped inaccessible directory during vault artifact scan");
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Log.Debug("Directory was deleted during vault artifact scan");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error scanning for vault artifacts with pattern {Pattern}", pattern);
                    }
                }

                string remainingUsbSpace;
                try
                {
                    var drive = new DriveInfo(driveRoot[..1]);
                    remainingUsbSpace = $"{FormatFileSize(drive.AvailableFreeSpace)} free of {FormatFileSize(drive.TotalSize)}";
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not determine USB space for drive {Drive}", driveRoot);
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
                TotalSteps = HasRemnants ? 8 : 7;
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
            string? deviceId = null;
            string? errorMessage = null;
            bool isUsbSelected = SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath);

            if (isUsbSelected)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var driveLetter = ExtractDriveRoot(SelectedUsbPath);
                        if (string.IsNullOrEmpty(driveLetter))
                        {
                            errorMessage = "Invalid USB device selection.";
                            return;
                        }

                        deviceId = _usbBindingService.ComputeDeviceId(driveLetter);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Could not detect USB serial: {ex.Message}";
                    }
                });
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UsbDeviceId = deviceId;
                UsbSerialNumber = deviceId;
                StatusMessage = errorMessage ?? (deviceId != null ? "USB device binding ready." : null);
            });
        }

        partial void OnSelectedUsbPathChanged(string? value)
        {
            // Validate USB device ID hasn't changed (prevents device swapping)
            if (!string.IsNullOrEmpty(value) && _usbDeviceMap.ContainsKey(value))
            {
                var (_, _, deviceId, _) = _usbDeviceMap[value];
                Log.Debug("USB device selected with physical ID: {DeviceId}", deviceId[..8]);
            }

            // Re-detect USB serial when selection changes
            _ = DetectUsbSerialAsync();

            // Also scan for remnants on the newly-selected drive
            if (!string.IsNullOrEmpty(value))
            {
                _ = ScanForRemnantsAsync().ContinueWith(
                    t => Log.Error(t.Exception, "Remnant scan failed for selected USB path"),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            OnPropertyChanged(nameof(PhantomKeyBridgeLocationDescription));
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
            RevealMasterPassword = false;
            RevealConfirmPassword = false;
            _generatedPasswordWasAutoCreated = true;
            AnalyzePasswordStrength();
            StatusMessage = "Strong password generated. It will be staged as a one-time encrypted recovery item on the selected USB.";
        }

        [RelayCommand]
        private void AnalyzePasswordStrength()
        {
            if (string.IsNullOrEmpty(MasterPassword))
            {
                PasswordStrength = "None";
                PasswordsMatch = MasterPassword == ConfirmPassword;
                return;
            }

            int score = 0;

            // Length scoring (more stringent)
            if (MasterPassword.Length >= 8) score += 1;
            if (MasterPassword.Length >= 12) score += 2;
            if (MasterPassword.Length >= 16) score += 2;
            if (MasterPassword.Length >= 20) score += 2;

            // Character type diversity (increased weight)
            bool hasUpper = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[A-Z]");
            bool hasLower = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[a-z]");
            bool hasDigit = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[0-9]");
            bool hasSpecial = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[^A-Za-z0-9]");

            int diversityScore = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            score += diversityScore * 2;

            // Penalty for sequential or repeated characters
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "(.)\\1{2,}"))
                score -= 2;

            PasswordStrength = score switch
            {
                <= 4 => "Weak",
                <= 8 => "Fair",
                <= 12 => "Good",
                _ => "Excellent"
            };

            PasswordsMatch = MasterPassword == ConfirmPassword;
        }

        private async Task DetectWindowsHelloAsync()
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        WindowsHelloAvailable = false;
                        WindowsHelloStatus = "Windows Hello requires Windows 10 or later.";
                        return;
                    }

                    // Fallback to OS version check for Windows Hello availability
                    var osVersion = Environment.OSVersion;
                    WindowsHelloAvailable = osVersion.Platform == PlatformID.Win32NT &&
                                           osVersion.Version.Major >= 10;

                    WindowsHelloStatus = WindowsHelloAvailable
                        ? "Windows Hello may be available (check device settings)."
                            : "Windows Hello is not available on this device.";
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Windows Hello detection failed");
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
                        TotpQrCodeUri = $"otpauth://totp/{Uri.EscapeDataString(EffectiveVaultName)}:User?secret={TotpSecretKey}&issuer={Uri.EscapeDataString(EffectiveVaultName)}";
                    }

                    StatusMessage = "Configure additional authentication methods.";
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Passkey detection failed");
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
            TotpQrCodeUri = $"otpauth://totp/{Uri.EscapeDataString(EffectiveVaultName)}:User?secret={TotpSecretKey}&issuer={Uri.EscapeDataString(EffectiveVaultName)}";
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
            if (EnablePhantomKey)
            {
                summary += $"• Phantom Key Bridge: {PhantomKeyBridgeLocationDescription}\n";
                summary += $"• Trust Boundary: {PhantomKeyTrustBoundarySummary}\n";
            }
            summary += $"• Encrypted Container: {(EnableEncryptedContainer ? $"Enabled ({EncryptedContainerSize})" : "Disabled")}\n";
            summary += selectedTier == VaultProtectionTier.BlackSecure
                ? "• Key Material: Device-bound raw transport with no external keyfile path\n"
                : $"• Keyfile: {(UseExistingKeyfile && !string.IsNullOrWhiteSpace(KeyfilePath) ? Path.GetFileName(KeyfilePath) : EntropyKeyfileSealed ? "Entropy-blended staged keyfile" : "Pending entropy seal")}\n";
            summary += $"• Master Password: {(UsePassword ? "Configured" : "Not used")}\n";
            summary += $"• Windows Hello: {(EnableWindowsHello ? (WindowsHelloOnboarding.IsBiometricEnrolled ? "Enrolled" : "Pending") : "Not enabled")}\n";
            summary += $"• Passkey: {(EnablePasskeys ? (PasskeyOnboarding.HasRegisteredPasskey ? "Registered" : "Pending") : "Not enabled")}\n";
            summary += $"• TOTP: {(EnableTotp ? (TotpOnboarding.IsTotpEnabled ? "Verified" : "Pending") : "Not enabled")}\n";
            if (HasRemnants)
                summary += $"• Remnant Action: {SelectedRemnantAction}\n";
            summary += "\nReversible Measures: Provisioning metadata and canonical inner-container paths will be preserved for future tier migration.\n";
            summary += $"\nEncryption: AES-256-GCM with {(selectedTier == VaultProtectionTier.BlackSecure ? "raw-device transport wrapper" : "canonical container profile")}\n";

            SetupSummary = summary;
        }

        // ── Quick-Setup helpers (called from WelcomePage / QuickSetupWindow) ──

        /// <summary>The display-ready vault name used by quick-setup callers.</summary>
        public string EffectiveVaultName => string.IsNullOrWhiteSpace(VaultName)
            ? "PhantomObscura"
            : VaultName.Trim();

        /// <summary>Pre-configures the view-model for the quick-setup flow so the full wizard can be bypassed.</summary>
        public void ConfigureForQuickSetup()
        {
            AcceptedTerms = true;
            SelectedSecurityLevel = "Ghost Secured";
            // Vault data always lives on the USB drive.
            SelectedStorageLocation = "USB";
            EnableUsbBinding = true;
            EnableGuuidBinding = true;
            EnablePhantomKey = false;
            EnableEncryptedContainer = false;
            UsePassword = false;
            UseExistingKeyfile = false;
        }

        /// <summary>Detects available USB drives and applies sensible defaults for the quick-setup path.</summary>
        public async Task InitializeQuickSetupAsync()
        {
            await DetectUsbDrivesAsync();

            if (AvailableUsbDrives.Count > 0 && string.IsNullOrEmpty(SelectedUsbPath))
                SelectedUsbPath = AvailableUsbDrives[0];
        }

        /// <summary>Validates that all required quick-setup inputs are present before provisioning.</summary>
        public Task<bool> ValidateQuickSetupAsync()
        {
            if (string.IsNullOrWhiteSpace(VaultName))
            {
                StatusMessage = "Please enter a name for your vault.";
                return Task.FromResult(false);
            }

            if (UsePassword && string.IsNullOrWhiteSpace(MasterPassword))
            {
                StatusMessage = "Please enter a master password.";
                return Task.FromResult(false);
            }

            if (UsePassword && MasterPassword != ConfirmPassword)
            {
                StatusMessage = "Passwords do not match. Please re-enter your password.";
                return Task.FromResult(false);
            }

            if (SelectedStorageLocation == "USB" && string.IsNullOrWhiteSpace(SelectedUsbPath))
            {
                StatusMessage = "A USB drive is required. Please insert your USB drive and try again.";
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>Kicks off provisioning for the quick-setup flow, delegating to the existing creation pipeline.</summary>
        public async Task BeginProvisioningAsync()
        {
            // Fire the VaultReadyForCreation event so the UI layer can open the
            // VaultCreationLoadingWindow, which in turn calls ExecuteVaultCreationAsync
            // through its RunCreationAsync. Calling ExecuteVaultCreationAsync directly
            // here skips the loading window and leaves the QuickSetupWindow stranded
            // (causing the app to appear to close after Create Vault is clicked).
            await CompleteSetupAsync();
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
            if (IsCompleting)
            {
                Log.Warning("ExecuteVaultCreationAsync called while already in progress — ignoring re-entrant call");
                return;
            }

            string? stagingRoot = null;
            string? volumePath = null;
            string? vaultPath = null;
            string? blackSecurePhysicalDevicePath = null;
            string? hostCompanionKeyfilePath = null;
            string? hostCompanionLocatorPath = null;
            var cleanupFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleanupDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IsCompleting = true;
                StatusMessage = "Creating your vault...";
                Log.Information("ExecuteVaultCreationAsync starting");
                ReportProvisioningStage(0, 5, "Initializing secure provisioning...", "Validating selected protection tier and storage targets.");

                // ─── Determine vault path ───
                string? driveRoot = null;
                var selectedTier = GetSelectedProtectionTier();
                var effectiveTransport = GetEffectiveStorageTransport(selectedTier);
                var requestedTransport = GetRequestedStorageTransport(selectedTier);
                bool usePackedMasterVolume = effectiveTransport == VaultStorageTransport.PackedVolume;
                bool needsOnboarding = (EnableWindowsHello && !WindowsHelloOnboarding.IsBiometricEnrolled)
                                   || (EnablePasskeys && !PasskeyOnboarding.HasRegisteredPasskey)
                                   || (EnableTotp && !TotpOnboarding.IsTotpEnabled);
                if (usePackedMasterVolume)
                {
                    stagingRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSetup", Guid.NewGuid().ToString("N"));
                    cleanupDirectories.Add(stagingRoot);
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
                const string generatedPasswordRecordRelativePath = "bootstrap/generated-password.pmeta";
                const string phantomKeyBridgeNotes = "Phantom Key operates from its own sealed bridge workspace. Obscura consumes policy and binding records without importing private credential material.";

                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    driveRoot = ExtractDriveRoot(SelectedUsbPath);
                    vaultPath = Path.Combine(driveRoot, ".phantom");
                    if (usePackedMasterVolume)
                    {
                        volumePath = Path.Combine(driveRoot, "system.bin");
                        cleanupFiles.Add(volumePath);
                    }
                }
                else
                {
                    // Use %AppData%\PhantomVault\vault as the local vault root.
                    // Documents (%MyDocuments%) is commonly redirected to OneDrive on Windows 11
                    // which blocks creation of dot-folders like ".phantom" and causes a
                    // FileNotFoundException at provisioning time.
                    vaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PhantomVault",
                        "vault");
                    if (usePackedMasterVolume)
                    {
                        volumePath = Path.Combine(vaultPath, "obscura.vol");
                        cleanupFiles.Add(volumePath);
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
                string generatedPasswordRecordPath = Path.Combine(vaultPath, generatedPasswordRecordRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyBridgeRootPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyBridgeManifestPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.BridgeManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyContinuityPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.ContinuityRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyPolicyPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.PolicyRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyConsumerMapPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.ConsumerMapRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyAuditLogPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.AuditLogRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string phantomKeyBridgeReceiptPath = Path.Combine(workingRoot, PhantomKeyBridgeContract.BridgeReceiptRelativePath.Replace('/', Path.DirectorySeparatorChar));

                // ─── Wipe remnants FIRST (before creating directories) ───
                // Must happen before directory creation because the cleanup will
                // delete an empty .phantom dir — which is the one we're about to use.
                if (HasRemnants && SelectedRemnantAction == "WipeRemnants" && DetectedRemnants.Count > 0)
                {
                    ReportProvisioningStage(1, 14, "Securely wiping remnant files...", "Removing prior protected artifacts before provisioning the new vault.");
                    Log.Information("Wiping {Count} remnant file(s)", DetectedRemnants.Count);

                    // Lift any OS-level write-protection left by the prior provisioning
                    // before we attempt to delete files.
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        try
                        {
                            var wpLifter = new UsbWriteProtectionService();
                            bool lifted = wpLifter.EnableWriteAccess(driveRoot);
                            Log.Information("Write-protect lifted before remnant wipe: {Lifted}", lifted);
                        }
                        catch (Exception wpEx)
                        {
                            Log.Warning(wpEx, "Could not lift write-protection on {DriveRoot} before remnant wipe — will attempt anyway", driveRoot);
                        }
                    }

                    // Collect parent .phantom directories so we can remove protections and clean up
                    var phantomDirsToClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var remnantFailures = new List<string>();

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
                            Log.Error(ex, "Failed to wipe remnant file: {FilePath}", remnant.FilePath);
                            remnantFailures.Add(remnant.FilePath);
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
                            Log.Error(ex, "Failed to clean up .phantom directory: {Dir}", cleanDir);
                            remnantFailures.Add(cleanDir);
                        }
                    }

                    if (remnantFailures.Count > 0)
                    {
                        throw new InvalidOperationException("Provisioning aborted because one or more prior Phantom artifacts could not be securely removed.");
                    }

                    DetectedRemnants.Clear();
                    HasRemnants = false;
                    StatusMessage = "Remnant files wiped.";
                }

                // ─── Create vault directories (after remnant wipe so they aren't deleted) ───
                ReportProvisioningStage(1, 22, "Creating canonical vault structure...", "Preparing staged root, vault, object, and recovery paths.");

                // If the vault path already exists from a failed prior run, reset all file
                // attributes to Normal so we can overwrite/delete locked files freely.
                if (Directory.Exists(vaultPath))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(vaultPath, "*", SearchOption.AllDirectories))
                        {
                            var fi2 = new FileInfo(f);
                            if (fi2.Attributes != FileAttributes.Normal)
                                fi2.Attributes = FileAttributes.Normal;
                        }
                        foreach (var d in Directory.EnumerateDirectories(vaultPath, "*", SearchOption.AllDirectories))
                        {
                            var di2 = new DirectoryInfo(d);
                            if (di2.Attributes != FileAttributes.Normal)
                                di2.Attributes = FileAttributes.Normal;
                        }
                        var vaultDirInfo2 = new DirectoryInfo(vaultPath);
                        if (vaultDirInfo2.Attributes != FileAttributes.Normal)
                            vaultDirInfo2.Attributes = FileAttributes.Normal;
                        Log.Information("Reset attributes on existing vault path: {VaultPath}", vaultPath);
                    }
                    catch (Exception attrEx)
                    {
                        Log.Warning(attrEx, "Could not reset attributes on existing vault path — continuing anyway");
                    }
                }

                Directory.CreateDirectory(vaultPath);
                Directory.CreateDirectory(Path.GetDirectoryName(rootContainerPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(objectContainerPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(recoveryContainerPath)!);
                Directory.CreateDirectory(Path.Combine(workingRoot, recoveryVaultWorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(decoyDatabasePath)!);
                if (EnablePhantomKey)
                {
                    Directory.CreateDirectory(phantomKeyBridgeRootPath);
                }
                cleanupDirectories.Add(vaultPath);

                // Hide the .phantom folder so vault contents aren't visible in file explorers
                var phantomDirInfo = new DirectoryInfo(vaultPath);
                if (phantomDirInfo.Exists)
                    phantomDirInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;

                StatusMessage = "Creating vault directory...";
                Log.Information("Vault path: {VaultPath}", vaultPath);

                // ─── Generate vault salt (shared across all crypto operations) ───
                byte[] salt = _encryptionService.GenerateSalt(32);
                string? passphrase = UsePassword ? MasterPassword : null;
                string bindingId = Guid.NewGuid().ToString("N");
                string bindingGuid = Guid.NewGuid().ToString("D");

                // ─── Handle keyfile ───
                string? keyfilePath = null;
                bool usesExternalKeyfile = selectedTier != VaultProtectionTier.BlackSecure;
                if (usesExternalKeyfile && UseExistingKeyfile && KeyfileSelected)
                {
                    keyfilePath = KeyfilePath;
                    ReportProvisioningStage(2, 34, "Using existing keyfile...", "Reusing the operator-supplied keyfile for container provisioning.");
                    Log.Information("Using existing keyfile: {KeyfilePath}", keyfilePath);
                }
                else if (usesExternalKeyfile)
                {
                    string primaryKeyfilePath = Path.Combine(vaultPath, "vault.key");
                    string companionLocatorPath = Path.Combine(vaultPath, "host-key", "companion.locator");
                    ReportProvisioningStage(2, 38, "Writing entropy-blended keyfile...", "Persisting the staged pointer-derived keyfile and wrapping it in place.");
                    await GenerateKeyfileAsync(primaryKeyfilePath);
                    cleanupFiles.Add(primaryKeyfilePath);

                    hostCompanionKeyfilePath = await GenerateSecondaryKeyfileAsync(primaryKeyfilePath, companionLocatorPath, bindingId);
                    hostCompanionLocatorPath = companionLocatorPath;
                    if (!string.IsNullOrWhiteSpace(hostCompanionKeyfilePath))
                    {
                        cleanupFiles.Add(hostCompanionKeyfilePath);
                        cleanupDirectories.Add(Path.GetDirectoryName(hostCompanionKeyfilePath)!);
                    }

                    if (!string.IsNullOrWhiteSpace(hostCompanionLocatorPath))
                    {
                        cleanupFiles.Add(hostCompanionLocatorPath);
                        cleanupDirectories.Add(Path.GetDirectoryName(hostCompanionLocatorPath)!);
                    }

                    // Encrypt the USB-visible primary keyfile in-place while keeping the host companion raw.
                    await EncryptFileInPlaceAsync(primaryKeyfilePath, salt, passphrase);

                    keyfilePath = CompositeKeyfilePath.Compose(primaryKeyfilePath, hostCompanionKeyfilePath);
                    KeyfilePath = keyfilePath;
                    StatusMessage = "Generated and encrypted keyfile...";
                    Log.Information("Generated key material with USB keyfile {PrimaryKeyfilePath} and companion keyfile {HostCompanionKeyfilePath}", primaryKeyfilePath, hostCompanionKeyfilePath);
                }
                else
                {
                    ReportProvisioningStage(2, 34, "Skipping external keyfile path...", "Phantom Secured uses password and device-bound factors instead of a browsable keyfile.");
                    KeyfilePath = null;
                    StatusMessage = "Phantom Secured uses password plus device-bound factors without an external keyfile path.";
                }

                // ─── USB device binding ───
                string? deviceId = null;
                if (selectedTier == VaultProtectionTier.BlackSecure &&
                    SelectedStorageLocation == "USB" &&
                    !string.IsNullOrEmpty(driveRoot) &&
                    !_blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromDriveRoot(driveRoot, out blackSecurePhysicalDevicePath))
                {
                    throw new InvalidOperationException("Unable to resolve the selected USB drive to a physical device for Phantom Secured provisioning.");
                }

                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(driveRoot) && EnableUsbBinding)
                {
                    // Pre-flight: verify the selected drive is actually writable before attempting binding.
                    // If access is denied, attempt to lift any write-protection left by a prior provisioning.
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        string probeFile = Path.Combine(driveRoot, $".phantom_probe_{Guid.NewGuid():N}");
                        try
                        {
                            File.WriteAllText(probeFile, "probe");
                            File.Delete(probeFile);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Drive may be write-protected from a prior Phantom provisioning.
                            // Attempt to lift it before giving up.
                            Log.Warning("USB drive {DriveRoot} appears write-protected — attempting to lift protection before binding", driveRoot);
                            try
                            {
                                var wpLifter = new UsbWriteProtectionService();
                                wpLifter.EnableWriteAccess(driveRoot);
                                File.WriteAllText(probeFile, "probe");
                                File.Delete(probeFile);
                                Log.Information("Write-protect lifted on {DriveRoot} — proceeding with binding", driveRoot);
                            }
                            catch
                            {
                                throw new InvalidOperationException(
                                    $"The selected USB drive ({driveRoot.TrimEnd('\\')}) is write-protected and the protection could not be removed automatically. " +
                                    "Remove write protection manually and try again, or choose a different drive.");
                            }
                        }
                        catch (IOException ioEx)
                        {
                            throw new InvalidOperationException(
                                $"The selected USB drive ({driveRoot.TrimEnd('\\')}) could not be written to: {ioEx.Message} " +
                                "Ensure the drive is connected and not full.");
                        }
                    }

                    try
                    {
                        bindingId = Guid.NewGuid().ToString("N");
                        bindingGuid = Guid.NewGuid().ToString("D");

                        if (EnablePhantomKey)
                        {
                            ReportProvisioningStage(3, 48, "Binding PhantomKey hardware context...", "Computing the high-assurance device binding record.");
                            // PhantomKey: high-assurance binding with hidden device ID file
                            deviceId = _usbBindingService.InitializeHighAssuranceBinding(driveRoot, salt);
                            Log.Information("PhantomKey high-assurance binding established: {DeviceId}", deviceId);
                            StatusMessage = "PhantomKey high-assurance USB binding established...";
                        }
                        else
                        {
                            ReportProvisioningStage(3, 48, "Binding USB device identity...", "Computing the device-bound identifier for this transport.");
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

                        // ── Seed OS-suppression sentinels on the freshly-bound drive ──
                        // Writes .metadata_never_index, .nomedia, .Trashes (file), and
                        // .fseventsd/no_log at the root so OS indexers skip the volume.
                        // RO is NOT set here — the manifest and vault container still
                        // need to be written further down. The GPT ReadOnly bit (and
                        // optional Layer B partition re-tag) is applied later by the
                        // VaultUnlock close handler so RW stays available throughout
                        // provisioning. Settings-gated.
                        if (!string.IsNullOrEmpty(driveRoot))
                        {
                            try
                            {
                                var setupUsbSettings = PhantomVault.UI.Services.SettingsService.Load();
                                if (setupUsbSettings.UsbWriteProtectionEnabled)
                                {
                                    var wpService = new UsbWriteProtectionService();
                                    var seedState = new PhantomVault.Core.Models.UsbWriteProtectionState
                                    {
                                        ReadOnly = false,
                                        Hidden = false,
                                        CompatibilityMode = setupUsbSettings.UsbCompatibilityMode,
                                    };
                                    wpService.EnsureSentinelFiles(driveRoot, seedState);
                                    Log.Information("[Setup] USB OS-indexer sentinels seeded on {Drive} ({N} files)",
                                        driveRoot, seedState.ExpectedSentinelFiles.Count);
                                }
                            }
                            catch (Exception wpEx)
                            {
                                Log.Warning(wpEx, "[Setup] failed to seed OS-indexer sentinels on {Drive}; continuing.", driveRoot);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Setup] USB binding failed for drive {DriveRoot}", driveRoot);
                        string bindMsg = ex.InnerException is UnauthorizedAccessException || ex is UnauthorizedAccessException
                            ? $"The USB drive ({driveRoot?.TrimEnd('\\')}) is write-protected. Remove write protection and try again."
                            : $"Provisioning aborted because the selected USB device could not be bound securely. {ex.Message}";
                        throw new InvalidOperationException(bindMsg, ex);
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
                        throw new InvalidOperationException("Provisioning aborted because the required hardware GUUID binding could not be established.", ex);
                    }
                }

                // ─── Build manifest ───
                var manifest = new VaultManifest
                {
                    Version = 3,
                    VaultName = EffectiveVaultName,
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
                    PhantomKeyBridgeEnabled = EnablePhantomKey,
                    PhantomKeyBridgeWorkspacePath = EnablePhantomKey ? PhantomKeyBridgeContract.WorkspaceRelativePath : null,
                    PhantomKeyBridgeManifestPath = EnablePhantomKey ? PhantomKeyBridgeContract.BridgeManifestRelativePath : null,
                    PhantomKeyBridgeContinuityPath = EnablePhantomKey ? PhantomKeyBridgeContract.ContinuityRelativePath : null,
                    PhantomKeyBridgePolicyPath = EnablePhantomKey ? PhantomKeyBridgeContract.PolicyRelativePath : null,
                    PhantomKeyBridgeConsumerMapPath = EnablePhantomKey ? PhantomKeyBridgeContract.ConsumerMapRelativePath : null,
                    PhantomKeyBridgeAuditLogPath = EnablePhantomKey ? PhantomKeyBridgeContract.AuditLogRelativePath : null,
                    PhantomKeyBridgeReceiptPath = EnablePhantomKey ? PhantomKeyBridgeContract.BridgeReceiptRelativePath : null,
                    RequiresTotp = EnableTotp,
                    ProtectionTier = selectedTier,
                    EffectiveStorageTransport = effectiveTransport,
                    RequestedStorageTransport = requestedTransport,
                    SupportsReversibleTierMigration = true
                };

                // TOTP secret generation
                if (EnableTotp)
                {
                    ReportProvisioningStage(4, 58, "Generating manifest authentication material...", "Adding TOTP metadata and sealing the root manifest.");
                    manifest.TotpSecret = !string.IsNullOrWhiteSpace(TotpOnboarding.TotpSecret)
                        ? TotpOnboarding.TotpSecret
                        : throw new InvalidOperationException("TOTP was enabled, but no verified TOTP secret is staged.");
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

                // ─── Create mandatory container layout ───
                manifest.ContainerSizeBytes = containerSpecs[1].SizeBytes;
                ReportProvisioningStage(4, 68, "Creating encrypted container set...", "Provisioning root, vault, object, and recovery containers.");

                await using var initialVaultPayload = await BuildInitialVaultPayloadAsync(manifest, passphrase, keyfilePath)
                    .ConfigureAwait(false);

                foreach (var containerSpec in containerSpecs)
                {
                    StatusMessage = $"Creating {Path.GetFileNameWithoutExtension(containerSpec.Path)} container...";
                    Log.Information("Creating encrypted container: {Size} bytes at {Path}", containerSpec.SizeBytes, containerSpec.Path);

                    if (string.Equals(containerSpec.Path, vaultContainerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        initialVaultPayload.Position = 0;
                        await _containerService.CreateContainerFromStreamAsync(
                            containerSpec.Path,
                            initialVaultPayload,
                            containerSpec.SizeBytes,
                            passphrase,
                            keyfilePath,
                            manifest: null,
                            progress: null,
                            cancellationToken: CancellationToken.None);
                    }
                    else
                    {
                        await _containerService.CreateContainerAsync(
                            containerSpec.Path,
                            containerSpec.SizeBytes,
                            passphrase,
                            keyfilePath,
                            manifest: containerSpec.EmbeddedManifest,
                            progress: null,
                            cancellationToken: CancellationToken.None);
                    }
                }

                StatusMessage = "Container layout created...";
                Log.Information("Encrypted container layout created successfully");

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
                    cleanupFiles.Add(Path.Combine(bootstrapPendingDirectory, "obscura-vault-summary.json"));

                    File.WriteAllText(
                        Path.Combine(bootstrapPendingDirectory, "recovery-record-summary.json"),
                        System.Text.Json.JsonSerializer.Serialize(recoveryRecord, bootstrapOptions));
                    cleanupFiles.Add(Path.Combine(bootstrapPendingDirectory, "recovery-record-summary.json"));

                    File.WriteAllLines(
                        Path.Combine(bootstrapPendingDirectory, "README.txt"),
                        new[]
                        {
                            "Phantom.Obscura created this Recovery workspace for Phantom.Recovery.",
                            "These bootstrap files are suite metadata only and can be safely imported into the encrypted Recovery vault.",
                            $"Recovery workspace: {recoveryVaultWorkspaceRelativePath}",
                            $"Recovery container: {recoveryContainerRelativePath}"
                        });
                    cleanupFiles.Add(Path.Combine(bootstrapPendingDirectory, "README.txt"));
                    cleanupDirectories.Add(Path.Combine(
                        workingRoot,
                        recoveryVaultWorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar),
                        ".suite-bootstrap"));
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

                if (EnablePhantomKey)
                {
                    ReportProvisioningStage(5, 78, "Sealing Phantom Key bridge workspace...", "Writing isolated bridge policy, continuity, and consumer mapping records.");

                    var bridgeManifest = new PhantomKeyBridgeManifestDocument
                    {
                        CreatedUtc = DateTimeOffset.UtcNow,
                        BridgeModel = PhantomKeyBridgeContract.BridgeManifestModel,
                        OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                        Consumers = PhantomKeyBridgeContract.DefaultConsumers,
                        WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                        Notes = phantomKeyBridgeNotes
                    };

                    File.WriteAllText(
                        phantomKeyBridgeManifestPath,
                        JsonSerializer.Serialize(
                            bridgeManifest,
                            new JsonSerializerOptions { WriteIndented = true }));

                    if (!File.Exists(phantomKeyAuditLogPath))
                    {
                        File.WriteAllText(phantomKeyAuditLogPath, string.Empty);
                    }

                    var bindingDigest = string.IsNullOrWhiteSpace(bindingId)
                        ? string.Empty
                        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bindingId)));

                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        phantomKeyContinuityPath,
                        new PhantomKeyContinuityDocument
                        {
                            CreatedUtc = DateTimeOffset.UtcNow,
                            VaultName = manifest.VaultName,
                            ProtectionTier = selectedTier.ToString(),
                            EffectiveTransport = effectiveTransport.ToString(),
                            BridgeWorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                            Consumers = PhantomKeyBridgeContract.DefaultConsumers,
                            BindingDigest = bindingDigest,
                            RequiresPasskeyBridge = EnablePasskeys,
                            Notes = "Continuity state is sanitized for bridge consumption. No raw secrets or credential payloads are persisted here."
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        PhantomKeyBridgeContract.ContinuityPurpose);

                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        phantomKeyPolicyPath,
                        new PhantomKeyPolicyWorkspaceDocument
                        {
                            CreatedUtc = DateTimeOffset.UtcNow,
                            OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                            StorageBoundary = selectedTier == VaultProtectionTier.BlackSecure
                                ? "raw-device-sibling-workspace"
                                : selectedTier == VaultProtectionTier.StealthSecure
                                    ? "packed-transport-sibling-workspace"
                                    : "filesystem-sibling-workspace",
                            PrivateMaterialExportAllowed = false,
                            RequiresBridgeMediation = true,
                            AllowedConsumers = PhantomKeyBridgeContract.DefaultConsumers,
                            AllowedRecordClasses = PhantomKeyBridgeContract.DefaultRecordClasses,
                            Notes = phantomKeyBridgeNotes
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        PhantomKeyBridgeContract.PolicyPurpose);

                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        phantomKeyConsumerMapPath,
                        new PhantomKeyConsumerMapDocument
                        {
                            CreatedUtc = DateTimeOffset.UtcNow,
                            OwnerApp = PhantomKeyBridgeContract.ObscuraOwnerApp,
                            WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                            ObscuraBindingRecordPath = bindingRecordRelativePath,
                            ObscuraProvisioningRecordPath = provisioningRecordRelativePath,
                            RecoveryWorkspacePath = recoveryVaultWorkspaceRelativePath,
                            ConsumerApps = PhantomKeyBridgeContract.DefaultConsumers,
                            Notes = "Consumer map exposes only relative paths and policy metadata. Secrets remain in their original containers."
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        PhantomKeyBridgeContract.ConsumerMapPurpose);

                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        phantomKeyBridgeReceiptPath,
                        new PhantomKeyBridgeReceiptDocument
                        {
                            CreatedUtc = DateTimeOffset.UtcNow,
                            WorkspacePath = PhantomKeyBridgeContract.WorkspaceRelativePath,
                            ManifestPath = PhantomKeyBridgeContract.BridgeManifestRelativePath,
                            ContinuityPath = PhantomKeyBridgeContract.ContinuityRelativePath,
                            PolicyPath = PhantomKeyBridgeContract.PolicyRelativePath,
                            ConsumerMapPath = PhantomKeyBridgeContract.ConsumerMapRelativePath,
                            AuditLogPath = PhantomKeyBridgeContract.AuditLogRelativePath,
                            StorageBoundary = bridgeManifest.BridgeModel,
                            PrivateMaterialExportAllowed = false,
                            Notes = phantomKeyBridgeNotes
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        PhantomKeyBridgeContract.BridgeReceiptPurpose);
                }

                await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                    decoyDatabasePath,
                    BuildDecoyDatabase(),
                    manifest,
                    passphrase,
                    keyfilePath,
                    "decoy-database");

                if (_generatedPasswordWasAutoCreated && UsePassword && !string.IsNullOrWhiteSpace(MasterPassword))
                {
                    await _usbArtifactProtectionService.WriteEncryptedJsonAsync(
                        generatedPasswordRecordPath,
                        new GeneratedPasswordBootstrapRecord
                        {
                            Password = MasterPassword,
                            Prompt = "Please reveal password and save somewhere safe, this will be deleted.",
                            CreatedUtc = DateTimeOffset.UtcNow
                        },
                        manifest,
                        passphrase,
                        keyfilePath,
                        "generated-password-bootstrap");

                    StatusMessage = "Generated password staged as a one-time encrypted recovery item on the USB.";
                }

                if (selectedTier == VaultProtectionTier.BlackSecure)
                {
                    ReportProvisioningStage(5, 84, "Writing Phantom Secured raw-device volume...", "Projecting the staged canonical layout directly to the physical device.");
                    await _blackSecureRawVolumeService.CreateVolumeFromDirectoryAsync(
                        blackSecurePhysicalDevicePath!,
                        stagingRoot!,
                        CancellationToken.None);
                    Directory.Delete(stagingRoot!, true);
                    stagingRoot = null;
                }
                else if (usePackedMasterVolume)
                {
                    ReportProvisioningStage(5, 84, "Packing master Obscura volume...", "Packing the canonical staged layout into the concealed transport volume.");
                    var obscuraVolumeService = new ObscuraVolumeService();
                    await obscuraVolumeService.CreateVolumeFromDirectoryAsync(volumePath!, stagingRoot!, CancellationToken.None);
                    Directory.Delete(stagingRoot!, true);
                    stagingRoot = null;
                }
                else
                {
                    ReportProvisioningStage(5, 84, "Writing direct canonical container layout...", "Leaving the canonical container set directly on the selected transport.");
                }

                // Manifest is now embedded inside the container (v3 format) —
                // no separate vault.manifest.encrypted file on disk.

                // ─── Harden vault files against casual deletion ───
                // Files can only be removed via the in-app remnant wipe which
                // strips these protections first, then securely overwrites.
                VaultFileProtection.HardenVaultFiles(vaultPath);

                PhantomVault.UI.Services.SettingsService.Update(settings =>
                {
                    settings.DefaultVaultProtectionTier = selectedTier.ToString();
                    settings.PendingPostCreateAuthOnboarding = needsOnboarding;
                    settings.PendingSetupWindowsHello = EnableWindowsHello && !WindowsHelloOnboarding.IsBiometricEnrolled;
                    settings.PendingSetupPasskey = EnablePasskeys && !PasskeyOnboarding.HasRegisteredPasskey;
                    settings.PendingSetupTotp = EnableTotp && !TotpOnboarding.IsTotpEnabled;

                    var treatAsLocal = SelectedStorageLocation != "USB" || string.IsNullOrEmpty(SelectedUsbPath);
                    if (treatAsLocal &&
                        !settings.KnownLocalVaultPaths.Contains(vaultPath, StringComparer.OrdinalIgnoreCase))
                    {
                        settings.KnownLocalVaultPaths.Add(vaultPath);
                    }
                });

                if (SelectedStorageLocation != "USB" || string.IsNullOrEmpty(SelectedUsbPath))
                {
                    Log.Information("Registered local vault path: {VaultPath}", vaultPath);
                }

                ReportProvisioningStage(6, 100, "Vault created successfully!", "Protection metadata, transport records, and hardened vault paths are all in place.");
                Log.Information("ExecuteVaultCreationAsync completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Vault creation failed");
                StatusMessage = $"Error creating vault: {ex.Message}";
                await CleanupFailedProvisioningAsync(
                    cleanupFiles,
                    cleanupDirectories,
                    stagingRoot,
                    vaultPath,
                    volumePath,
                    hostCompanionKeyfilePath,
                    hostCompanionLocatorPath).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(blackSecurePhysicalDevicePath))
                {
                    await _blackSecureRawVolumeService.InvalidateVolumeHeaderAsync(blackSecurePhysicalDevicePath).ConfigureAwait(false);
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

        private async Task<MemoryStream> BuildInitialVaultPayloadAsync(VaultManifest manifest, string? passphrase, string? keyfilePath)
        {
            var database = new VaultDatabase
            {
                Version = "2.0",
                EncryptionType = "ZeroKnowledge-VaultFileZk",
                Created = DateTime.UtcNow,
                VaultName = manifest.VaultName,
                Description = "Initial Phantom Obscura vault database",
                Groups = new List<VaultGroup>
                {
                    new()
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = "Logins",
                        Icon = "key",
                        Entries = new List<Credential>()
                    }
                }
            };

            byte[] vaultKey = new VaultDatabaseKeyService(_encryptionService)
                .DeriveKey(manifest, passphrase, keyfilePath);
            var zkVaultService = new PhantomVault.Core.Services.ZeroKnowledge.ZkVaultService();
            try
            {
                if (!await zkVaultService.UnlockWithHybridKeyAsync(vaultKey).ConfigureAwait(false))
                    throw new InvalidOperationException("Unable to initialize the vault database encryption key.");

                byte[] databaseBytes = JsonSerializer.SerializeToUtf8Bytes(database, new JsonSerializerOptions { WriteIndented = true });
                try
                {
                    await using var plaintextStream = new MemoryStream(databaseBytes, writable: false);
                    var encryptedPayload = new MemoryStream();
                    await zkVaultService.EncryptStreamToStreamAsync(plaintextStream, encryptedPayload).ConfigureAwait(false);
                    encryptedPayload.Position = 0;
                    return encryptedPayload;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(databaseBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vaultKey);
                await zkVaultService.LockAndWipeKeysAsync().ConfigureAwait(false);
                zkVaultService.Dispose();
            }
        }

        private string ExtractDriveRoot(string? usbPathDisplay)
        {
            if (string.IsNullOrWhiteSpace(usbPathDisplay))
                return string.Empty;

            // Parse format: "ID: xxxxxxxx (C:) VolumeLabel [123 GB]"
            var match = System.Text.RegularExpressions.Regex.Match(usbPathDisplay, @"\(([A-Za-z]:)\)");
            if (match.Success)
            {
                var driveLetter = match.Groups[1].Value;
                if (!driveLetter.EndsWith("\\")) driveLetter += "\\";
                return driveLetter;
            }

            // Fallback for legacy format (should not occur with new detection logic)
            string driveRoot = usbPathDisplay.Length >= 2 ? usbPathDisplay.Substring(0, 3) : usbPathDisplay;
            if (!driveRoot.EndsWith("\\")) driveRoot += "\\";
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
                // Mix passphrase bytes into the IKM so the wrapping key is bound to both
                // the vault salt and the master password (if set).
                byte[] saltIkm = string.IsNullOrEmpty(passphrase)
                    ? salt
                    : SHA256.HashData(
                        salt.Concat(System.Text.Encoding.UTF8.GetBytes(passphrase)).ToArray());

                // Derive an encryption key specifically for file wrapping
                byte[] wrappingKey = HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    saltIkm,
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

        private async Task<string> GenerateSecondaryKeyfileAsync(string primaryKeyfilePath, string locatorPath, string bindingId)
        {
            if (!File.Exists(primaryKeyfilePath))
            {
                throw new FileNotFoundException("The primary generated keyfile is missing before host companion provisioning.", primaryKeyfilePath);
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("Local application data storage is unavailable for the host companion keyfile.");
            }

            string hiddenFolder = Path.Combine(localAppData, "PhantomObscura", "HostKey");
            Directory.CreateDirectory(hiddenFolder);
            if (OperatingSystem.IsWindows())
            {
                var di = new DirectoryInfo(hiddenFolder);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }

            string secondaryPath = Path.Combine(hiddenFolder, $"{bindingId}.companion.key");
            File.Copy(primaryKeyfilePath, secondaryPath, overwrite: true);
            if (OperatingSystem.IsWindows())
            {
                var fi = new FileInfo(secondaryPath);
                fi.Attributes |= FileAttributes.Hidden | FileAttributes.ReadOnly;
            }

            string locatorDirectory = Path.GetDirectoryName(locatorPath)
                ?? throw new InvalidOperationException("Companion locator path must include a parent directory.");
            Directory.CreateDirectory(locatorDirectory);
            if (OperatingSystem.IsWindows())
            {
                var di = new DirectoryInfo(locatorDirectory);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }

            await File.WriteAllTextAsync(
                locatorPath,
                JsonSerializer.Serialize(new HostCompanionLocator
                {
                    HostCompanionKeyfilePath = secondaryPath
                }));

            if (OperatingSystem.IsWindows())
            {
                var fi = new FileInfo(locatorPath);
                fi.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }

            ReportProvisioningStage(2, 40, "Provisioned host companion keyfile...",
                "Host companion keyfile and USB locator were sealed successfully.");
            Log.Information("Host companion keyfile provisioned at: {SecondaryKeyfilePath}", secondaryPath);
            StatusMessage = "Host companion keyfile sealed successfully.";
            return secondaryPath;
        }

        private async Task GenerateKeyfileAsync(string keyfilePath)
        {
            // If no entropy-staged bytes exist (e.g. quick setup skipped the ritual),
            // fall back to a CSPRNG-generated key. This is cryptographically sound —
            // the mouse-entropy ritual only adds extra mixing on top of CSPRNG for the
            // full wizard UX; the underlying security is identical.
            if (_stagedGeneratedKeyfileBytes == null || _stagedGeneratedKeyfileBytes.Length == 0)
            {
                _stagedGeneratedKeyfileBytes = new byte[GeneratedKeyfileSizeBytes > 0 ? GeneratedKeyfileSizeBytes : 64];
                System.Security.Cryptography.RandomNumberGenerator.Fill(_stagedGeneratedKeyfileBytes);
                Log.Information("GenerateKeyfileAsync: no staged entropy bytes — auto-generated {Bytes} bytes via CSPRNG", _stagedGeneratedKeyfileBytes.Length);
            }

            try
            {
                await File.WriteAllBytesAsync(keyfilePath, _stagedGeneratedKeyfileBytes);
                KeyfileStatus = $"Entropy-blended keyfile staged for {Path.GetFileName(keyfilePath)}.";
                StatusMessage = "Entropy-blended keyfile generated successfully.";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(_stagedGeneratedKeyfileBytes);
                _stagedGeneratedKeyfileBytes = null;
                EntropyKeyfileSealed = false;
                PrepareGeneratedKeyfileFlow();
            }
        }

        private async Task CleanupFailedProvisioningAsync(
            IEnumerable<string> cleanupFiles,
            IEnumerable<string> cleanupDirectories,
            string? stagingRoot,
            string? vaultPath,
            string? volumePath,
            string? hostCompanionKeyfilePath,
            string? hostCompanionLocatorPath)
        {
            var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in cleanupFiles)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    filePaths.Add(path);
            }

            if (!string.IsNullOrWhiteSpace(volumePath))
                filePaths.Add(volumePath);
            if (!string.IsNullOrWhiteSpace(hostCompanionKeyfilePath))
                filePaths.Add(hostCompanionKeyfilePath);
            if (!string.IsNullOrWhiteSpace(hostCompanionLocatorPath))
                filePaths.Add(hostCompanionLocatorPath);

            foreach (var filePath in filePaths)
            {
                await TryDeleteProvisioningFileAsync(filePath).ConfigureAwait(false);
            }

            var directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in cleanupDirectories)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    directoryPaths.Add(path);
            }

            if (!string.IsNullOrWhiteSpace(stagingRoot))
                directoryPaths.Add(stagingRoot);
            if (!string.IsNullOrWhiteSpace(vaultPath))
                directoryPaths.Add(vaultPath);

            foreach (var directoryPath in directoryPaths.OrderByDescending(path => path.Length))
            {
                TryDeleteProvisioningDirectory(directoryPath);
            }
        }

        private static async Task TryDeleteProvisioningFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                VaultFileProtection.StripFileProtection(filePath);
                await SecureDeletionService.SecureDeleteFileAsync(filePath, SecureDeletionService.DeletionMethod.StandardSecure).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }

        private static void TryDeleteProvisioningDirectory(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return;

                VaultFileProtection.StripDirectoryProtection(directoryPath);
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
                {
                    try
                    {
                        new DirectoryInfo(dir).Attributes = FileAttributes.Normal;
                    }
                    catch
                    {
                    }
                }

                new DirectoryInfo(directoryPath).Attributes = FileAttributes.Normal;
                Directory.Delete(directoryPath, true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
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
                KeyfileStatus = "Phantom Secured uses password plus device-bound factors; no external keyfile path is provisioned.";
                ResetEntropyKeyfileState();
            }
            else if (!UseExistingKeyfile)
            {
                KeyfileStatus = "A new keyfile will be generated";
                InitializeEntropyKeyfileGenerator();
            }

            OnPropertyChanged(nameof(IsBlackSecureSelected));
            OnPropertyChanged(nameof(SupportsExternalKeyfile));
            OnPropertyChanged(nameof(ShowGeneratedKeyfileInfo));
            OnPropertyChanged(nameof(RequiresGeneratedKeyfileEntropy));
            OnPropertyChanged(nameof(EntropyProgressPercent));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));
            OnPropertyChanged(nameof(ShowPasswordToggle));
            OnPropertyChanged(nameof(KeyMaterialSectionTitle));
            OnPropertyChanged(nameof(KeyMaterialSectionSubtitle));
            OnPropertyChanged(nameof(KeyMaterialDescription));
            OnPropertyChanged(nameof(PasswordSectionTitle));
            OnPropertyChanged(nameof(PasswordSectionDescription));
            OnPropertyChanged(nameof(PasswordToggleText));
            OnPropertyChanged(nameof(PhantomKeyBridgeLocationDescription));
        }

        partial void OnEnablePhantomKeyChanged(bool value)
        {
            OnPropertyChanged(nameof(PhantomKeyBridgeLocationDescription));
            OnPropertyChanged(nameof(PhantomKeyTrustBoundarySummary));
        }

        partial void OnRevealMasterPasswordChanged(bool value)
        {
            OnPropertyChanged(nameof(MasterPasswordRevealGlyph));
        }

        partial void OnRevealConfirmPasswordChanged(bool value)
        {
            OnPropertyChanged(nameof(ConfirmPasswordRevealGlyph));
        }

        partial void OnUseExistingKeyfileChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowGeneratedKeyfileInfo));
            OnPropertyChanged(nameof(RequiresGeneratedKeyfileEntropy));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));

            if (value)
            {
                ResetEntropyKeyfileState();
                KeyfileGenerationStatus = "Existing keyfile mode selected.";
            }
            else if (SupportsExternalKeyfile)
            {
                InitializeEntropyKeyfileGenerator();
            }
        }

        private VaultProtectionTier GetSelectedProtectionTier()
            => SelectedSecurityLevel switch
            {
                "Standard Secure" => VaultProtectionTier.StandardSecure,
                "Ghost Secured" => VaultProtectionTier.StealthSecure,
                "Phantom Secured" => VaultProtectionTier.BlackSecure,
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
            if (_generatedPasswordWasAutoCreated && !string.Equals(value, ConfirmPassword, StringComparison.Ordinal))
            {
                _generatedPasswordWasAutoCreated = false;
            }
            AnalyzePasswordStrength();
        }

        partial void OnConfirmPasswordChanged(string value)
        {
            if (_generatedPasswordWasAutoCreated && !string.Equals(value, MasterPassword, StringComparison.Ordinal))
            {
                _generatedPasswordWasAutoCreated = false;
            }
            PasswordsMatch = MasterPassword == ConfirmPassword;
        }

        partial void OnEnableUsbBindingChanged(bool value)
        {
            _ = ConfirmSecurityReductionAsync(value, nameof(EnableUsbBinding), () => EnableUsbBinding = true);
        }

        partial void OnEnableGuuidBindingChanged(bool value)
        {
            _ = ConfirmSecurityReductionAsync(value, nameof(EnableGuuidBinding), () => EnableGuuidBinding = true);
        }

        partial void OnEnableEncryptedContainerChanged(bool value)
        {
            _ = ConfirmSecurityReductionAsync(value, nameof(EnableEncryptedContainer), () => EnableEncryptedContainer = true);
        }

        private async Task ConfirmSecurityReductionAsync(bool newValue, string toggleName, Action restoreAction)
        {
            if (newValue || _revertingCriticalToggle || _ownerWindow == null)
                return;

            var approved = await _dialogService.ShowConfirmationAsync(
                "Reduce Vault Protection?",
                "Are you sure? This will significantly weaken the vault.",
                "Weaken Vault",
                "Keep Protection",
                _ownerWindow);

            if (approved)
            {
                StatusMessage = toggleName switch
                {
                    nameof(EnableEncryptedContainer) => "Encrypted container disabled. Vault hardening reduced.",
                    nameof(EnableGuuidBinding) => "GUUID binding disabled. Hardware binding reduced.",
                    nameof(EnableUsbBinding) => "USB binding disabled. Device-bound protection reduced.",
                    _ => "Vault protection reduced."
                };
                return;
            }

            _revertingCriticalToggle = true;
            try
            {
                restoreAction();
            }
            finally
            {
                _revertingCriticalToggle = false;
            }

            StatusMessage = "Protection setting restored.";
        }

        [RelayCommand]
        private void ToggleMasterPasswordReveal()
        {
            RevealMasterPassword = !RevealMasterPassword;
        }

        [RelayCommand]
        private void ToggleConfirmPasswordReveal()
        {
            RevealConfirmPassword = !RevealConfirmPassword;
        }

        [RelayCommand]
        private void ToggleRevealUsbDeviceId()
        {
            RevealUsbDeviceId = !RevealUsbDeviceId;
        }

        public event EventHandler<ProvisioningProgressEventArgs>? ProvisioningProgressChanged;

        partial void OnVaultNameChanged(string value)
        {
            TotpOnboarding.VaultName = EffectiveVaultName;
            OnPropertyChanged(nameof(EffectiveVaultName));
        }

        partial void OnUsbDeviceIdChanged(string? value)
        {
            OnPropertyChanged(nameof(DisplayUsbDeviceId));
        }

        partial void OnRevealUsbDeviceIdChanged(bool value)
        {
            OnPropertyChanged(nameof(DisplayUsbDeviceId));
            OnPropertyChanged(nameof(UsbDeviceIdToggleText));
        }

        public void RecordEntropyPointerSample(double x, double y, bool leftPressed, bool rightPressed)
        {
            if (!RequiresGeneratedKeyfileEntropy || EntropyKeyfileSealed)
                return;

            PrepareGeneratedKeyfileFlow();
            _entropyKeyfileGenerator?.AddMouseSample(x, y, leftPressed, rightPressed);

            if (_entropyKeyfileGenerator == null)
                return;

            EntropyCollectedBits = _entropyKeyfileGenerator.CollectedEntropyBits;
            EntropySampleCount = _entropyKeyfileGenerator.SampleCount;
            KeyfileGenerationStatus = _entropyKeyfileGenerator.CanFinalize
                ? "Entropy threshold reached. Seal the staged keyfile to continue."
                : $"Entropy collected: {EntropyCollectedBits}/{EntropyRequiredBits} bits.";

            OnPropertyChanged(nameof(EntropyProgressPercent));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));
        }

        private static string MaskSensitiveIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return "Unavailable";

            if (identifier.Length <= 8)
                return new string('•', identifier.Length);

            return $"{new string('•', Math.Max(0, identifier.Length - 8))}{identifier[^8..]}";
        }

        [RelayCommand]
        private void ResetEntropyKeyfile()
        {
            InitializeEntropyKeyfileGenerator();
        }

        [RelayCommand]
        private void SealEntropyKeyfile()
        {
            if (!RequiresGeneratedKeyfileEntropy)
                return;

            PrepareGeneratedKeyfileFlow();
            if (_entropyKeyfileGenerator == null || !_entropyKeyfileGenerator.CanFinalize)
            {
                StatusMessage = "Keep moving the pointer until enough entropy has been collected.";
                return;
            }

            var result = _entropyKeyfileGenerator.FinalizeKeyMaterial(GeneratedKeyfileSizeBytes);
            _stagedGeneratedKeyfileBytes = result.KeyMaterial;
            EntropyCollectedBits = result.CollectedEntropyBits;
            EntropySampleCount = result.SampleCount;
            EntropyKeyfileSealed = true;
            KeyfileStatus = $"Entropy-blended keyfile is sealed and ready ({result.CollectedEntropyBits} bits from {result.SampleCount} samples).";
            KeyfileGenerationStatus = "Entropy sealed. The keyfile will be written and wrapped during provisioning.";
            StatusMessage = "Entropy keyfile staged successfully.";

            _entropyKeyfileGenerator.Dispose();
            _entropyKeyfileGenerator = null;

            OnPropertyChanged(nameof(EntropyProgressPercent));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));
        }

        private void PrepareGeneratedKeyfileFlow()
        {
            if (!RequiresGeneratedKeyfileEntropy)
                return;

            if (_entropyKeyfileGenerator == null && !EntropyKeyfileSealed)
                InitializeEntropyKeyfileGenerator();
        }

        private void InitializeEntropyKeyfileGenerator()
        {
            ResetEntropyKeyfileState();

            _entropyKeyfileGenerator = new EntropyKeyfileGenerator();
            EntropyRequiredBits = _entropyKeyfileGenerator.MinimumRequiredBits;
            KeyfileGenerationStatus = "Move your pointer across the entropy field until the keyfile can be sealed.";
            KeyfileStatus = "Entropy-blended keyfile not sealed yet.";

            OnPropertyChanged(nameof(EntropyProgressPercent));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));
        }

        private void ResetEntropyKeyfileState()
        {
            _entropyKeyfileGenerator?.Dispose();
            _entropyKeyfileGenerator = null;

            if (_stagedGeneratedKeyfileBytes != null)
            {
                CryptographicOperations.ZeroMemory(_stagedGeneratedKeyfileBytes);
                _stagedGeneratedKeyfileBytes = null;
            }

            EntropyCollectedBits = 0;
            EntropySampleCount = 0;
            EntropyKeyfileSealed = false;
            EntropyRequiredBits = 256;

            OnPropertyChanged(nameof(EntropyProgressPercent));
            OnPropertyChanged(nameof(CanSealEntropyKeyfile));
        }

        private void ReportProvisioningStage(int phaseIndex, double percent, string status, string detail)
        {
            StatusMessage = status;
            ProvisioningProgressChanged?.Invoke(this, new ProvisioningProgressEventArgs(phaseIndex, percent, status, detail));
        }
    }

    public sealed class ProvisioningProgressEventArgs : EventArgs
    {
        public ProvisioningProgressEventArgs(int phaseIndex, double percent, string status, string detail)
        {
            PhaseIndex = phaseIndex;
            Percent = percent;
            Status = status;
            Detail = detail;
        }

        public int PhaseIndex { get; }
        public double Percent { get; }
        public string Status { get; }
        public string Detail { get; }
    }

    public partial class SecurityLevelOption : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] Features { get; set; } = Array.Empty<string>();
        public string RecommendedFor { get; set; } = string.Empty;
        public string SecurityHelpText { get; set; } = string.Empty;
        public string FriendlySummary { get; set; } = string.Empty;
        public int SecurityIncreasePercent { get; set; }

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

    internal sealed class GeneratedPasswordBootstrapRecord
    {
        public string Password { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; }
    }

    internal sealed class HostCompanionLocator
    {
        public string HostCompanionKeyfilePath { get; init; } = string.Empty;
    }

}
