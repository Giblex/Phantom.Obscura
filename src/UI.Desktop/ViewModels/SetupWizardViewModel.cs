using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
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
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.DomainKeys;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Utils;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.Licensing;
using PhantomVault.UI.Services.Mount;
using PhantomVault.UI.Views.Dialogs;
using Serilog;

namespace PhantomVault.UI.ViewModels
{

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
        private readonly KeyfileRecoveryBundleService _keyfileRecoveryBundleService;

        /// <summary>
        /// Recovery codes produced during the most recent provisioning run. Surfaced once to the user
        /// during the forced-export step, then cleared. Each code can reopen the exported recovery file.
        /// </summary>
        private string[]? _stagedRecoveryCodes;

        /// <summary>
        /// The exportable recovery file (a self-describing <see cref="RecoveryStoreEnvelope"/>) produced
        /// during the most recent provisioning run. The user MUST save this OFF the USB; any recovery
        /// code reopens it to rebuild the keyfile onto a new USB if the original is lost.
        /// </summary>
        private byte[]? _stagedRecoveryFileBytes;
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

        [ObservableProperty]
        private string _vaultName = "My Vault";

        [ObservableProperty]
        private bool _acceptedTerms;

        [ObservableProperty]
        private string _selectedSecurityLevel = "Ghost Secured";

        [ObservableProperty]
        private string _securityDescription = "Recommended for most users. Packs the vault into a single concealed master volume while preserving reversible migration.";

        [ObservableProperty]
        private string _selectedStorageLocation = "USB";

        [ObservableProperty]
        private bool _usbDetected;

        [ObservableProperty]
        private string? _selectedUsbPath;

        [ObservableProperty]
        private ObservableCollection<string> _availableUsbDrives = new();

        private Dictionary<string, (string driveLetter, string volumeLabel, string deviceId, long sizeGb)> _usbDeviceMap = new();

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

        [ObservableProperty]
        private string? _usbSerialNumber;

        [ObservableProperty]
        private string? _usbDeviceId;

        [ObservableProperty]
        private bool _revealUsbDeviceId;

        [ObservableProperty]
        private bool _enableUsbBinding = true;

        [ObservableProperty]
        private bool _enableGuuidBinding;

        [ObservableProperty]
        private string? _guuidValue;

        [ObservableProperty]
        private bool _enablePhantomKey;

        [ObservableProperty]
        private bool _enableEncryptedContainer;

        [ObservableProperty]
        private string _encryptedContainerSize = "1 GB";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WinFspStatusText))]
        [NotifyPropertyChangedFor(nameof(WinFspStatusDetail))]
        [NotifyPropertyChangedFor(nameof(VirtualDriveReadinessText))]
        private bool _isWinFspInstalled;

        [ObservableProperty]
        private ObservableCollection<DetectedRemnant> _detectedRemnants = new();

        [ObservableProperty]
        private bool _hasRemnants;

        [ObservableProperty]
        private string _selectedRemnantAction = "WipeRemnants";

        [ObservableProperty]
        private string _remainingUsbSpaceDisplay = string.Empty;

        /// <summary>
        /// Named step indices for the setup wizard.
        ///
        /// CurrentStep stays an <see cref="int"/> because the progress indicator in
        /// SetupWizardWindow.axaml binds it through converters with numeric
        /// ConverterParameters — changing the type would break every one of those.
        /// These constants remove the magic numbers from the logic instead, so the
        /// step map has a single definition that the switches, predicates, titles and
        /// validators all refer to.
        ///
        /// Note the tail of the wizard is conditional: when USB remnants are detected an
        /// extra step is inserted before Review, so Review is always <see cref="TotalSteps"/>
        /// rather than a fixed index.
        /// </summary>
        internal static class Step
        {
            public const int Welcome = 1;
            public const int SecurityLevel = 2;
            public const int PhantomKeyBridge = 3;
            public const int StorageLocation = 4;
            public const int KeyfileAndPassword = 5;
            public const int Authentication = 6;
            public const int Remnants = 7;   // only present when HasRemnants

            /// <summary>Step count without the conditional remnants step.</summary>
            public const int BaseTotal = 7;

            /// <summary>Step count including the conditional remnants step.</summary>
            public const int TotalWithRemnants = 8;
        }

        public bool IsAuthenticationStep => CurrentStep == Step.Authentication;

        public bool IsRemnantStep => HasRemnants && CurrentStep == Step.Remnants;

        public bool IsReviewStep => CurrentStep == TotalSteps;

        public bool UsesExtendedProgress => HasRemnants;

        public bool WipeRemnantsSelected
        {
            get => SelectedRemnantAction == "WipeRemnants";
            set { if (value) SelectedRemnantAction = "WipeRemnants"; }
        }

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

        [ObservableProperty]
        private bool _enableWindowsHello;

        [ObservableProperty]
        private bool _windowsHelloAvailable;

        [ObservableProperty]
        private string? _windowsHelloStatus;

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
                    "Clean, reversible migration path to and from Standard Secure"
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
                Description = "Maximum concealment. Binds the vault to a raw-device USB transport with a mandatory keyfile, device binding, and provisioning metadata.",
                Features = new[]
                {
                    "Raw-device transport bound to the selected USB hardware",
                    "Mandatory keyfile (keyfile-first) with an optional master password",
                    "Strongest artifact minimisation and device-bound access"
                },
                RecommendedFor = "Maximum security",
                FriendlySummary = "Like a hidden safe welded to one specific key. The strongest option, bound to your USB device.",
                SecurityIncreasePercent = 92,
                SecurityHelpText = "Phantom Secured ties the vault to a single USB device using a raw-device transport. It still uses a mandatory keyfile like the other tiers, and a master password remains optional for an extra layer."
            }
            // NOTE: every tier — including Phantom Secured (BlackSecure) — now provisions a
            // mandatory keyfile and keeps the master password optional, preserving the
            // keyfile-first / password-optional rule. Phantom Secured additionally binds the
            // vault to a raw-device USB transport.
        };

        public bool IsBlackSecureSelected => GetSelectedProtectionTier() == VaultProtectionTier.BlackSecure;
        public bool SupportsExternalKeyfile => true;
        public bool ShowGeneratedKeyfileInfo => SupportsExternalKeyfile && !UseExistingKeyfile;
        public bool RequiresGeneratedKeyfileEntropy => ShowGeneratedKeyfileInfo;
        public int EntropyProgressPercent => Math.Min(100, (EntropyCollectedBits * 100) / Math.Max(1, EntropyRequiredBits));
        public bool CanSealEntropyKeyfile => RequiresGeneratedKeyfileEntropy && !EntropyKeyfileSealed && (_entropyKeyfileGenerator?.CanFinalize ?? false);
        public bool ShowPasswordToggle => true;

        public WindowsHelloSettingsViewModel WindowsHelloOnboarding { get; }
        public PasskeySettingsViewModel PasskeyOnboarding { get; }
        public TotpSettingsViewModel TotpOnboarding { get; }
        public string KeyMaterialSectionTitle => "Keyfile Configuration";
        public string KeyMaterialSectionSubtitle => IsBlackSecureSelected
            ? "Required - Your vault is secured with a unique keyfile and bound to the selected USB device."
            : "Required - Your vault will be secured with a unique keyfile";
        public string KeyMaterialDescription => IsBlackSecureSelected
            ? "Phantom Secured uses a mandatory keyfile (keyfile-first) and additionally binds the vault to a raw-device USB transport. Store the keyfile securely; without it your vault cannot be accessed."
            : "A keyfile is a cryptographic file that acts as your primary authentication method. Store it securely on your USB drive or in a safe location. Without this file, your vault cannot be accessed.";
        public string PasswordSectionTitle => "Optional Master Password";
        public string PasswordSectionDescription => "Add an extra layer of protection with a password";
        public string PasswordToggleText => "Enable master password (recommended for extra security)";
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

            _encryptionService = new EncryptionService();
            _containerService = new PhantomContainerService(_encryptionService);
            _manifestService = new ManifestService(_encryptionService, _containerService);
            _usbBindingService = new UsbBindingService();
            _usbArtifactProtectionService = new UsbArtifactProtectionService(_encryptionService);
            _keyfileRecoveryBundleService = new KeyfileRecoveryBundleService(
                new RecoveryStoreFactory(new ScopedRecoveryService(_encryptionService)),
                new RecoveryCodeService());
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
            IsWinFspInstalled = PhantomMountService.IsWinFspAvailable;
        }

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
            if (CurrentStep > Step.Welcome)
            {
                CurrentStep--;
                UpdateStepInfo();
            }
        }

        [RelayCommand]
        private void GoToStep(object? stepParameter)
        {

            if (stepParameter is string stepStr && int.TryParse(stepStr, out int targetStep))
            {

                if (targetStep >= Step.Welcome && targetStep < CurrentStep)
                {
                    CurrentStep = targetStep;
                    UpdateStepInfo();
                }
            }
            else if (stepParameter is int targetStepInt)
            {

                if (targetStepInt >= Step.Welcome && targetStepInt < CurrentStep)
                {
                    CurrentStep = targetStepInt;
                    UpdateStepInfo();
                }
            }
        }

        public bool CanGoToStep(int targetStep) => targetStep >= Step.Welcome && targetStep < CurrentStep;

        [RelayCommand]
        private async Task LoadStepDataAsync()
        {
            switch (CurrentStep)
            {
                case Step.PhantomKeyBridge:
                    OnPropertyChanged(nameof(IsBlackSecureSelected));
                    break;

                case Step.StorageLocation:
                    await DetectUsbDrivesAsync();

                    await DetectUsbSerialAsync();
                    break;

                case Step.KeyfileAndPassword:
                    PrepareGeneratedKeyfileFlow();
                    break;

                case Step.Authentication:
                    TotpOnboarding.VaultName = EffectiveVaultName;
                    await DetectWindowsHelloAsync();
                    await DetectPasskeysAsync();
                    break;

                default:

                    if (CurrentStep == TotalSteps)
                        GenerateSummary();
                    break;
            }
        }

        private async Task<bool> ValidateCurrentStepAsync()
        {
            switch (CurrentStep)
            {
                case Step.Welcome:
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

                case Step.SecurityLevel:
                    if (string.IsNullOrEmpty(SelectedSecurityLevel))
                    {
                        StatusMessage = "Please select a security level.";
                        return false;
                    }
                    break;

                case Step.StorageLocation:
                    if (string.IsNullOrEmpty(SelectedUsbPath))
                    {
                        StatusMessage = "Please select a USB drive for your vault.";
                        return false;
                    }
                    break;

                case Step.KeyfileAndPassword:
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
                    break;

                case Step.Authentication:
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
                Step.Welcome => "Welcome to Phantom Obscura",
                Step.SecurityLevel => "Choose Security Level",
                Step.PhantomKeyBridge => "PhantomKey Bridge Setup",
                Step.StorageLocation => "Select Storage Location",
                Step.KeyfileAndPassword => "Keyfile & Password",
                Step.Authentication => "Additional Authentication",
                _ when IsRemnantStep => "Remnant Actions",
                _ when IsReviewStep => "Review and Complete",
                _ => "Setup"
            };

            OnPropertyChanged(nameof(IsAuthenticationStep));
            OnPropertyChanged(nameof(IsRemnantStep));
            OnPropertyChanged(nameof(IsReviewStep));

            CanGoBack = CurrentStep > Step.Welcome;
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

                        var deviceId = GetPhysicalDeviceId(driveLetter);

                        var displayName = $"ID: {deviceId[..8]} ({driveLetter}) {volumeLabel}";
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

            if (UsbDetected && !string.IsNullOrEmpty(SelectedUsbPath))
            {
                await ScanForRemnantsAsync();
            }
        }

        [SupportedOSPlatform("windows")]
        private static string GetPhysicalDeviceId(string driveLetter)
        {
            try
            {

                var letter = driveLetter.TrimEnd('\\', '/').TrimEnd(':');
                if (letter.Length != 1 || !char.IsLetter(letter[0]))
                    throw new ArgumentException($"Unexpected drive letter value: {driveLetter}");

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

                // SAFETY: only ever scan inside Obscura's own canonical container
                // ({drive}\.phantom). Everything under it is definitively ours; nothing
                // outside it is touched. This guarantees the remnant wipe can never
                // delete unrelated user files that merely happen to share an extension
                // (e.g. a personal ".key", ".encrypted", or "system.bin" elsewhere).
                var phantomRoot = PhantomDeviceLayout.GetPhantomRoot(driveRoot);

                if (Directory.Exists(phantomRoot))
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(phantomRoot, "*", SearchOption.AllDirectories))
                        {
                            AddRemnant(file);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Log.Debug("Skipped inaccessible path inside the .phantom container during remnant scan");
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Log.Debug("The .phantom container was removed during the remnant scan");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error scanning the .phantom container for Obscura remnants");
                    }
                }

                // Some Obscura vault layouts place the packed master volume (and the
                // legacy audit log) OUTSIDE the .phantom folder — at the drive root or
                // one directory down — which is exactly where WelcomePageViewModel
                // detects an existing vault. Mirror that here so a vault the welcome
                // screen can see is never invisible to the remnant scanner. Only the
                // well-known Obscura artifact filenames are considered, so unrelated
                // user files are never picked up.
                try
                {
                    // Scannable (not Known) so interrupted-commit leftovers — system.bin.tmp,
                    // system.bin.bak, the journal — are surfaced too. A stranded .bak is a
                    // whole previous vault, so a drive reported "clean" while one sat there
                    // was reporting something false.
                    foreach (var name in VaultFileProtection.ScannableObscuraArtifactNames)
                        AddRemnantIfExists(Path.Combine(driveRoot, name));

                    foreach (var dir in SafeEnumerateImmediateDirectories(driveRoot, phantomRoot))
                    {
                        foreach (var name in VaultFileProtection.ScannableObscuraArtifactNames)
                            AddRemnantIfExists(Path.Combine(dir, name));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error scanning the drive root for Obscura vault artifacts");
                }

                void AddRemnantIfExists(string path)
                {
                    if (File.Exists(path))
                        AddRemnant(path);
                }

                void AddRemnant(string file)
                {
                    if (!seenPaths.Add(file))
                        return;

                    FileInfo info;
                    try { info = new FileInfo(file); }
                    catch { return; }

                    var lowerName = info.Name.ToLowerInvariant();
                    var fileType = lowerName switch
                    {
                        PhantomDeviceLayout.SystemVolumeFileName => "Packed Vault",
                        "obscura.vol" => "Packed Vault",
                        PhantomDeviceLayout.DeviceIdFileName => "Device Binding",
                        PhantomDeviceLayout.AuditLogFileName => "Audit Log",
                        _ => Path.GetExtension(file).ToLowerInvariant() switch
                        {
                            ".pvault" => "Vault",
                            ".key" or ".keyfile" => "Keyfile",
                            ".pmeta" => "Metadata",
                            ".encrypted" => "Encrypted Container",
                            ".manifest" => "Manifest",
                            _ when lowerName.Contains("manifest") => "Manifest",
                            _ => "Vault Artifact"
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
                TotalSteps = HasRemnants ? Step.TotalWithRemnants : Step.BaseTotal;
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

            if (!string.IsNullOrEmpty(value) && _usbDeviceMap.ContainsKey(value))
            {
                var (_, _, deviceId, _) = _usbDeviceMap[value];
                Log.Debug("USB device selected with physical ID: {DeviceId}", deviceId[..8]);
            }

            _ = DetectUsbSerialAsync();

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

            if (MasterPassword.Length >= 8) score += 1;
            if (MasterPassword.Length >= 12) score += 2;
            if (MasterPassword.Length >= 16) score += 2;
            if (MasterPassword.Length >= 20) score += 2;

            bool hasUpper = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[A-Z]");
            bool hasLower = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[a-z]");
            bool hasDigit = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[0-9]");
            bool hasSpecial = System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[^A-Za-z0-9]");

            int diversityScore = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            score += diversityScore * 2;

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

                    PasskeysAvailable = true;
                PasskeysStatus = "Local Windows Hello-backed authentication is available.";

                    if (EnableTotp && string.IsNullOrEmpty(TotpSecretKey))
                    {

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

        public string EffectiveVaultName => string.IsNullOrWhiteSpace(VaultName)
            ? "PhantomObscura"
            : VaultName.Trim();

        public void ConfigureForQuickSetup()
        {
            AcceptedTerms = true;
            SelectedSecurityLevel = "Ghost Secured";

            SelectedStorageLocation = "USB";
            EnableUsbBinding = true;
            EnableGuuidBinding = true;
            EnablePhantomKey = false;
            EnableEncryptedContainer = false;
            UsePassword = false;
            UseExistingKeyfile = false;
        }

        public async Task InitializeQuickSetupAsync()
        {
            await DetectUsbDrivesAsync();

            if (AvailableUsbDrives.Count > 0 && string.IsNullOrEmpty(SelectedUsbPath))
                SelectedUsbPath = AvailableUsbDrives[0];
        }

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

        public async Task BeginProvisioningAsync()
        {

            await CompleteSetupAsync();
        }

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

                VaultReadyForCreation.Invoke(this, EventArgs.Empty);
                Log.Information("VaultReadyForCreation event fired successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "VaultReadyForCreation handler threw an exception");
                StatusMessage = $"Vault creation failed: {ex.Message}";
            }
        }

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

                string? driveRoot = null;
                var selectedTier = GetSelectedProtectionTier();
                var effectiveTransport = GetEffectiveStorageTransport(selectedTier);
                var requestedTransport = GetRequestedStorageTransport(selectedTier);
                bool usePackedMasterVolume = effectiveTransport == VaultStorageTransport.PackedVolume;
                // Both the packed-volume and raw-device (BlackSecure) tiers stage the
                // canonical layout into a temp directory first, then project it into
                // their concealed transport (a packed .bin volume, or the raw
                // \\.\PhysicalDrive). Only the plain FileSystem tier writes containers
                // directly into the on-disk vault path.
                bool useStagingRoot = effectiveTransport != VaultStorageTransport.FileSystem;

                // Packed-volume tiers mount as a virtual drive via WinFsp. Auto-install
                // the bundled driver now (elevated, silent) so the vault is mountable the
                // moment provisioning finishes. Non-blocking: provisioning continues even
                // if the driver isn't installed (e.g. no bundled MSI / declined UAC).
                if (usePackedMasterVolume)
                    await EnsureWinFspForProvisioningAsync();

                bool needsOnboarding = (EnableWindowsHello && !WindowsHelloOnboarding.IsBiometricEnrolled)
                                   || (EnablePasskeys && !PasskeyOnboarding.HasRegisteredPasskey)
                                   || (EnableTotp && !TotpOnboarding.IsTotpEnabled);
                if (useStagingRoot)
                {
                    stagingRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSetup", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stagingRoot);
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
                        PhantomDeviceLayout.EnsurePhantomRoot(driveRoot);
                        volumePath = PhantomDeviceLayout.GetSystemVolumePath(driveRoot);
                        cleanupFiles.Add(volumePath);
                    }
                }
                else
                {

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

                string workingRoot = useStagingRoot ? stagingRoot! : vaultPath;
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

                if (HasRemnants && SelectedRemnantAction == "WipeRemnants" && DetectedRemnants.Count > 0)
                {
                    ReportProvisioningStage(1, 14, "Securely wiping remnant files...", "Removing prior protected artifacts before provisioning the new vault.");
                    Log.Information("Wiping {Count} remnant file(s)", DetectedRemnants.Count);

                    var remnantsSnapshot = DetectedRemnants.ToList();

                    // WMI / diskpart write-protect lifting and multi-pass secure deletion are
                    // blocking operations. Run them off the UI thread so the provisioning
                    // animation keeps rendering instead of appearing stuck at this stage.
                    var remnantFailures = await Task.Run(async () =>
                    {
                        var failures = new List<string>();

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

                        var phantomDirsToClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var remnant in remnantsSnapshot)
                        {
                            try
                            {
                                // Defense-in-depth: never wipe anything that is not a
                                // definitively Obscura-owned artifact — i.e. it must live
                                // inside a ".phantom" container, or be one of the well-known
                                // Obscura artifact filenames (system.bin / obscura.vol /
                                // vault.audit). Anything else is skipped outright so
                                // unrelated user files are never deleted.
                                if (!VaultFileProtection.IsObscuraOwnedArtifact(remnant.FilePath))
                                {
                                    Log.Warning("Refusing to wipe non-Obscura path: {FilePath}", remnant.FilePath);
                                    continue;
                                }

                                var phantomAncestor = VaultFileProtection.FindPhantomAncestor(remnant.FilePath);

                                if (File.Exists(remnant.FilePath))
                                {

                                    VaultFileProtection.StripFileProtection(remnant.FilePath);

                                    await SecureDeletionService.BestEffortDeleteAsync(
                                        remnant.FilePath,
                                        SecureDeletionService.DeletionMethod.StandardSecure);
                                    Log.Information("Wiped remnant: {FilePath}", remnant.FilePath);

                                    if (phantomAncestor != null)
                                        phantomDirsToClean.Add(phantomAncestor);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Failed to wipe remnant file: {FilePath}", remnant.FilePath);
                                failures.Add(remnant.FilePath);
                            }
                        }

                        foreach (var cleanDir in phantomDirsToClean)
                        {
                            try
                            {
                                VaultFileProtection.StripDirectoryProtection(cleanDir);

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
                                failures.Add(cleanDir);
                            }
                        }

                        return failures;
                    });

                    if (remnantFailures.Count > 0)
                    {
                        throw new InvalidOperationException("Provisioning aborted because one or more prior Phantom artifacts could not be securely removed.");
                    }

                    DetectedRemnants.Clear();
                    HasRemnants = false;
                    StatusMessage = "Remnant files wiped.";
                }

                ReportProvisioningStage(1, 22, "Creating canonical vault structure...", "Preparing staged root, vault, object, and recovery paths.");

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

                var phantomDirInfo = new DirectoryInfo(vaultPath);
                if (phantomDirInfo.Exists)
                    phantomDirInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;

                StatusMessage = "Creating vault directory...";
                Log.Information("Vault path: {VaultPath}", vaultPath);

                byte[] salt = _encryptionService.GenerateSalt(32);
                string? passphrase = UsePassword ? MasterPassword : null;
                string bindingId = Guid.NewGuid().ToString("N");
                string bindingGuid = Guid.NewGuid().ToString("D");

                string? keyfilePath = null;
                // Keyfile-first for every tier — including Phantom Secured (BlackSecure),
                // which now provisions a mandatory keyfile in addition to its raw-device
                // USB binding.
                bool usesExternalKeyfile = true;
                if (usesExternalKeyfile && UseExistingKeyfile && KeyfileSelected)
                {
                    keyfilePath = KeyfilePath;
                    ReportProvisioningStage(2, 34, "Using existing keyfile...", "Reusing the operator-supplied keyfile for container provisioning.");
                    Log.Debug("Using existing keyfile: {KeyfileName}", System.IO.Path.GetFileName(keyfilePath));
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

                    await EncryptFileInPlaceAsync(primaryKeyfilePath, salt, passphrase);

                    keyfilePath = CompositeKeyfilePath.Compose(primaryKeyfilePath, hostCompanionKeyfilePath);
                    KeyfilePath = keyfilePath;
                    StatusMessage = "Generated and encrypted keyfile...";
                    Log.Debug("Generated key material with USB keyfile {PrimaryKeyfileName} and companion keyfile {HostCompanionKeyfileName}", System.IO.Path.GetFileName(primaryKeyfilePath), System.IO.Path.GetFileName(hostCompanionKeyfilePath));
                }
                else
                {
                    ReportProvisioningStage(2, 34, "Skipping external keyfile path...", "Phantom Secured uses password and device-bound factors instead of a browsable keyfile.");
                    KeyfilePath = null;
                    StatusMessage = "Phantom Secured uses password plus device-bound factors without an external keyfile path.";
                }

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

                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        // Write-probe: prove the drive is writable before we start binding.
                        //
                        // The name is deliberately anonymous. It used to be
                        // ".phantom_probe_<guid>", which announced the product at the root of
                        // the user's drive — the one thing a vault with a decoy tier must not
                        // do. The contents ("probe") were never sensitive; the *filename* was.
                        //
                        // The delete is in a finally rather than trailing the write. Previously
                        // an exception between the two — or the write-protect retry path, which
                        // rewrites the same file — could leave the probe behind permanently,
                        // and nothing ever swept it: probe files are not in
                        // VaultFileProtection.KnownObscuraArtifactNames, so neither the remnant
                        // scanner nor the wipe guard would ever see them again.
                        string probeFile = Path.Combine(driveRoot, $".~{Guid.NewGuid():N}.tmp");
                        try
                        {
                            try
                            {
                                File.WriteAllText(probeFile, "probe");
                            }
                            catch (UnauthorizedAccessException)
                            {

                                Log.Warning("USB drive {DriveRoot} appears write-protected — attempting to lift protection before binding", driveRoot);
                                try
                                {
                                    var wpLifter = new UsbWriteProtectionService();
                                    wpLifter.EnableWriteAccess(driveRoot);
                                    File.WriteAllText(probeFile, "probe");
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
                        finally
                        {
                            // A probe we cannot delete is a residue we will never find again,
                            // so this is warned about rather than swallowed. Removable media
                            // fails deletes far more often than a fixed disk does.
                            try
                            {
                                if (File.Exists(probeFile)) File.Delete(probeFile);
                            }
                            catch (Exception cleanupEx)
                            {
                                Log.Warning(cleanupEx,
                                    "Could not remove the write-probe file left on {DriveRoot} — it will remain on the drive",
                                    driveRoot);
                            }
                        }
                    }

                    try
                    {
                        bindingId = Guid.NewGuid().ToString("N");
                        bindingGuid = Guid.NewGuid().ToString("D");

                        if (EnablePhantomKey)
                        {
                            ReportProvisioningStage(3, 48, "Binding PhantomKey hardware context...", "Computing the high-assurance device binding record.");

                            deviceId = _usbBindingService.InitializeHighAssuranceBinding(driveRoot, salt);
                            Log.Information("PhantomKey high-assurance binding established: {DeviceId}", deviceId);
                            StatusMessage = "PhantomKey high-assurance USB binding established...";
                        }
                        else
                        {
                            ReportProvisioningStage(3, 48, "Binding USB device identity...", "Computing the device-bound identifier for this transport.");

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

                string? guuidValue = null;
                if (EnableGuuidBinding || (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(driveRoot)))
                {
                    try
                    {
                        guuidValue = DetectSystemGuuid();
                        if (!string.IsNullOrEmpty(guuidValue))
                        {
                            GuuidValue = guuidValue;

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
                    SupportsReversibleTierMigration = true,
                    PremiumLicenseToken = PendingLicenseToken
                };

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

                    // Seal the mandatory keyfile material under a fresh set of recovery codes so a lost
                    // or damaged USB can be reconstructed from an OFF-USB recovery file (see forced
                    // export step). Failure here must not abort provisioning, but is surfaced loudly.
                    if (!string.IsNullOrWhiteSpace(keyfilePath) && CompositeKeyfilePath.Exists(keyfilePath))
                    {
                        byte[]? keyfileMaterial = null;
                        try
                        {
                            keyfileMaterial = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
                            var bundle = _keyfileRecoveryBundleService.Create(
                                keyfileMaterial,
                                deviceId: deviceId,
                                appVersion: manifest.Version.ToString());

                            _stagedRecoveryCodes = bundle.RecoveryCodes;
                            _stagedRecoveryFileBytes = bundle.RecoveryFileBytes;
                            Log.Information(
                                "Keyfile recovery bundle sealed: {CodeCount} codes, {Bytes}-byte recovery file staged for off-USB export",
                                bundle.RecoveryCodes.Length, bundle.RecoveryFileBytes.Length);
                        }
                        catch (Exception recoveryEx)
                        {
                            Log.Error(recoveryEx, "Failed to seal keyfile recovery bundle — vault created but recovery codes are unavailable");
                            _stagedRecoveryCodes = null;
                            _stagedRecoveryFileBytes = null;
                        }
                        finally
                        {
                            if (keyfileMaterial != null)
                                CryptographicOperations.ZeroMemory(keyfileMaterial);
                        }
                    }

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
                    await obscuraVolumeService.CreateVolumeFromDirectoryAsync(
                        volumePath!, stagingRoot!,
                        keyfilePath ?? throw new InvalidOperationException("A keyfile is required to pack the master volume."),
                        CancellationToken.None);
                    Directory.Delete(stagingRoot!, true);
                    stagingRoot = null;
                }
                else
                {
                    ReportProvisioningStage(5, 84, "Writing direct canonical container layout...", "Leaving the canonical container set directly on the selected transport.");
                }

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

                throw;
            }
            finally
            {
                IsCompleting = false;
            }
        }

        /// <summary>True when provisioning produced recovery codes + an exportable recovery file.</summary>
        public bool HasStagedRecovery
            => _stagedRecoveryCodes is { Length: > 0 } && _stagedRecoveryFileBytes is { Length: > 0 };

        /// <summary>The recovery codes produced by the last provisioning run (shown once).</summary>
        public IReadOnlyList<string> StagedRecoveryCodes
            => _stagedRecoveryCodes ?? Array.Empty<string>();

        /// <summary>The default filename suggested when exporting the recovery file.</summary>
        public string SuggestedRecoveryFileName
            => $"{SanitizeFileName(EffectiveVaultName)}.precovery";

        /// <summary>
        /// Write the staged recovery file to <paramref name="destinationPath"/>. The destination MUST
        /// NOT live on the bound USB (defeats the purpose); callers should enforce this with
        /// <see cref="IsOffUsbDestination"/>. Returns true on success.
        /// </summary>
        public async Task<bool> SaveStagedRecoveryFileAsync(string destinationPath)
        {
            if (_stagedRecoveryFileBytes is not { Length: > 0 })
            {
                Log.Warning("SaveStagedRecoveryFileAsync called with no staged recovery file");
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
                return false;

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                await File.WriteAllBytesAsync(destinationPath, _stagedRecoveryFileBytes).ConfigureAwait(false);
                Log.Information("Recovery file exported to {Path}", destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export recovery file to {Path}", destinationPath);
                return false;
            }
        }

        /// <summary>
        /// True when <paramref name="destinationPath"/> is NOT on the bound USB drive root. Used to keep
        /// the recovery file off the device it is meant to recover.
        /// </summary>
        public bool IsOffUsbDestination(string? destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
                return false;

            var usbRoot = ExtractDriveRoot(SelectedUsbPath);
            if (string.IsNullOrWhiteSpace(usbRoot))
                return true;

            try
            {
                var destRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));
                return !string.Equals(
                    destRoot?.TrimEnd('\\', '/'),
                    usbRoot.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Unresolvable path: assume same-volume, the conservative answer.
                Log.Warning(ex, "Could not compare destination {Destination} against USB root {UsbRoot}", destinationPath, usbRoot);
                return false;
            }
        }

        /// <summary>Zeroize and clear staged recovery secrets after the user has saved/recorded them.</summary>
        public void ClearStagedRecovery()
        {
            if (_stagedRecoveryFileBytes != null)
            {
                CryptographicOperations.ZeroMemory(_stagedRecoveryFileBytes);
                _stagedRecoveryFileBytes = null;
            }
            _stagedRecoveryCodes = null;
        }

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "phantom-vault";

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "phantom-vault" : cleaned;
        }

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

            var match = System.Text.RegularExpressions.Regex.Match(usbPathDisplay, @"\(([A-Za-z]:)\)");
            if (match.Success)
            {
                var driveLetter = match.Groups[1].Value;
                if (!driveLetter.EndsWith("\\")) driveLetter += "\\";
                return driveLetter;
            }

            string driveRoot = usbPathDisplay.Length >= 2 ? usbPathDisplay.Substring(0, 3) : usbPathDisplay;
            if (!driveRoot.EndsWith("\\")) driveRoot += "\\";
            return driveRoot;
        }

        /// <summary>
        /// Enumerates the immediate subdirectories of the drive root (skipping the
        /// .phantom container, which is scanned separately) so packed vaults that
        /// were dropped one level down can still be found. Never throws.
        /// </summary>
        private static IEnumerable<string> SafeEnumerateImmediateDirectories(string driveRoot, string phantomRoot)
        {
            List<string> dirs = new();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(driveRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar),
                                       Path.GetFullPath(phantomRoot).TrimEnd(Path.DirectorySeparatorChar),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        dirs.Add(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not enumerate immediate directories of {DriveRoot} during remnant scan", driveRoot);
            }
            return dirs;
        }

        private static long ParseContainerSize(string sizeString)
        {
            if (string.IsNullOrWhiteSpace(sizeString))
                return 256L * 1024 * 1024;

            var trimmed = sizeString.Trim().ToUpperInvariant();

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && double.TryParse(parts[0], out double value))
            {
                return parts[1] switch
                {
                    "GB" => (long)(value * 1024 * 1024 * 1024),
                    "MB" => (long)(value * 1024 * 1024),
                    "KB" => (long)(value * 1024),
                    _ => (long)(value * 1024 * 1024)
                };
            }

            if (double.TryParse(trimmed, out double numericValue))
                return (long)(numericValue * 1024 * 1024);

            return 256L * 1024 * 1024;
        }

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

        private async Task EncryptFileInPlaceAsync(string filePath, byte[] salt, string? passphrase)
        {
            byte[] plainBytes = await File.ReadAllBytesAsync(filePath);
            try
            {

                byte[] saltIkm = string.IsNullOrEmpty(passphrase)
                    ? salt
                    : SHA256.HashData(
                        salt.Concat(System.Text.Encoding.UTF8.GetBytes(passphrase)).ToArray());

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
            Log.Debug("Host companion keyfile provisioned: {SecondaryKeyfileName}", System.IO.Path.GetFileName(secondaryPath));
            StatusMessage = "Host companion keyfile sealed successfully.";
            return secondaryPath;
        }

        private async Task GenerateKeyfileAsync(string keyfilePath)
        {

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
                await SecureDeletionService.BestEffortDeleteAsync(filePath, SecureDeletionService.DeletionMethod.StandardSecure).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Secure delete of provisioning file failed, falling back to a plain delete: {Path}", filePath);
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                    }
                }
                catch (Exception fallbackEx)
                {
                    // Worth surfacing: a provisioning artefact is left on disk.
                    Log.Error(fallbackEx, "Failed to delete provisioning file: {Path}", filePath);
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
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to remove the provisioning directory: {Dir}", directoryPath);
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
                // Phantom Secured binds to a raw-device USB transport AND uses a mandatory
                // keyfile like the other tiers (keyfile-first). The master password remains
                // optional.
                EnableGuuidBinding = true;
            }

            if (!UseExistingKeyfile)
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
            OnPropertyChanged(nameof(VirtualDriveReadinessText));
            _ = ConfirmSecurityReductionAsync(value, nameof(EnableEncryptedContainer), () => EnableEncryptedContainer = true);
        }

        /// <summary>Short driver-status label shown on the provisioning card badge.</summary>
        public string WinFspStatusText => IsWinFspInstalled ? "Driver installed" : "Driver not installed";

        /// <summary>Longer explanation of the WinFsp driver requirement.</summary>
        public string WinFspStatusDetail => IsWinFspInstalled
            ? "WinFsp is present. Vaults can be provisioned and mounted as a virtual encrypted drive."
            : "WinFsp is required to mount the vault as a Windows drive letter. Install it to enable virtual-drive provisioning.";

        /// <summary>Readiness line combining the provisioning toggle with driver availability.</summary>
        public string VirtualDriveReadinessText
        {
            get
            {
                if (!EnableEncryptedContainer)
                    return "Virtual-drive provisioning is off. The vault will store data as an encrypted container file.";
                return IsWinFspInstalled
                    ? "Ready to provision the vault as a virtual encrypted drive."
                    : "Install the WinFsp driver to provision the vault as a virtual drive.";
            }
        }

        [RelayCommand]
        private void RefreshWinFspStatus()
        {
            IsWinFspInstalled = PhantomMountService.IsWinFspAvailable;
            StatusMessage = IsWinFspInstalled ? "WinFsp driver detected." : "WinFsp driver not detected.";
        }

        [RelayCommand]
        private async Task InstallWinFspAsync()
        {
            var bundled = FindBundledWinFspInstaller();
            if (bundled == null)
            {
                StatusMessage = "Opening the WinFsp download page in your browser...";
                try
                {
                    Process.Start(new ProcessStartInfo("https://winfsp.dev/rel/")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to open the WinFsp download page.");
                    StatusMessage = "Could not open the WinFsp download page. Install it manually from winfsp.dev.";
                }

                RefreshWinFspStatus();
                return;
            }

            StatusMessage = "Installing the WinFsp driver...";
            var result = await RunBundledWinFspInstallerAsync(bundled);
            StatusMessage = result switch
            {
                WinFspInstallResult.Installed => "WinFsp driver installed.",
                WinFspInstallResult.Cancelled => "Driver installation was cancelled. The WinFsp driver is required for virtual-drive mounting.",
                _ => "Could not start the WinFsp installer. Install it manually from winfsp.dev."
            };
            RefreshWinFspStatus();
        }

        private enum WinFspInstallResult
        {
            Installed,
            Cancelled,
            Failed
        }

        /// <summary>
        /// Runs a bundled WinFsp MSI elevated and passively (single UAC prompt, no
        /// MSI wizard), then polls until the driver is detectable. Shared by the
        /// manual "Install driver" button and the automatic install during
        /// provisioning. Does NOT touch <see cref="StatusMessage"/> so callers can
        /// frame their own messaging.
        /// </summary>
        private static async Task<WinFspInstallResult> RunBundledWinFspInstallerAsync(string msiPath)
        {
            try
            {
                // msiexec must run elevated to install a driver. Since the app now
                // ships asInvoker, request elevation explicitly (one UAC prompt) and
                // install passively so the user isn't forced through the MSI wizard.
                var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\" /passive /norestart")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process? proc;
                try
                {
                    proc = Process.Start(psi);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // User declined the elevation prompt.
                    return WinFspInstallResult.Cancelled;
                }

                if (proc != null)
                    await proc.WaitForExitAsync();

                // The driver registry entry can lag slightly behind msiexec exiting;
                // poll briefly so detection succeeds without a manual retry.
                for (int i = 0; i < 10 && !PhantomMountService.IsWinFspAvailable; i++)
                    await Task.Delay(300);

                return PhantomMountService.IsWinFspAvailable
                    ? WinFspInstallResult.Installed
                    : WinFspInstallResult.Failed;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to launch the WinFsp installer.");
                return WinFspInstallResult.Failed;
            }
        }

        /// <summary>
        /// Ensures WinFsp is present before a tier that needs virtual-drive mounting
        /// is provisioned. If a bundled MSI is available and the driver is missing,
        /// installs it silently+elevated inline. Never blocks provisioning on
        /// failure (the vault still provisions; mounting just falls back until the
        /// driver is installed from the Storage step). Returns true if WinFsp is
        /// available afterwards.
        /// </summary>
        private async Task<bool> EnsureWinFspForProvisioningAsync()
        {
            if (PhantomMountService.IsWinFspAvailable)
                return true;

            var bundled = FindBundledWinFspInstaller();
            if (bundled == null)
            {
                Log.Information("WinFsp not installed and no bundled MSI found — skipping auto-install during provisioning.");
                return false;
            }

            ReportProvisioningStage(0, 5, "Installing the virtual-drive driver...", "Setting up WinFsp so your vault can mount as a drive.");
            var result = await RunBundledWinFspInstallerAsync(bundled);
            IsWinFspInstalled = PhantomMountService.IsWinFspAvailable;
            Log.Information("WinFsp auto-install during provisioning result: {Result}", result);
            return result == WinFspInstallResult.Installed;
        }

        private static string? FindBundledWinFspInstaller()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                foreach (var sub in new[] { baseDir, Path.Combine(baseDir, "drivers"), Path.Combine(baseDir, "Assets") })
                {
                    if (!Directory.Exists(sub))
                        continue;
                    var hit = Directory.GetFiles(sub, "winfsp*.msi").FirstOrDefault();
                    if (hit != null)
                        return hit;
                }
            }
            catch
            {
                // best-effort discovery only
            }

            return null;
        }

        // ---- Phase 3: optional subscription / plan selection ----------------

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlanStatusText))]
        [NotifyPropertyChangedFor(nameof(UpgradePlanButtonText))]
        private bool _selectedPlanIsPremium;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlanStatusText))]
        [NotifyPropertyChangedFor(nameof(UpgradePlanButtonText))]
        private bool _isPremiumActivated;

        [ObservableProperty]
        private string _subscriptionStatusText = string.Empty;

        /// <summary>
        /// Signed premium license token captured during setup (if the user
        /// subscribed). Persisted into the new vault's manifest at provisioning
        /// so premium unlocks on first open. Null = Free plan.
        /// </summary>
        public string? PendingLicenseToken { get; private set; }

        public string PlanStatusText => IsPremiumActivated
            ? "Premium plan activated — it will be applied to your new vault."
            : SelectedPlanIsPremium
                ? "Premium selected — complete checkout to activate, or keep the Free plan."
                : "Free plan — full core protection, no subscription required.";

        public string UpgradePlanButtonText => IsPremiumActivated ? "Premium Activated" : "Upgrade to Premium";

        [RelayCommand]
        private async Task UpgradeToPremiumAsync()
        {
            if (IsPremiumActivated)
                return;

            var services = (Avalonia.Application.Current as PhantomVault.UI.App)?.Services;
            if (services?.GetService(typeof(ILicensingClient)) is not ILicensingClient client)
            {
                SubscriptionStatusText = "Licensing is unavailable in this build.";
                return;
            }

            // The vault's USB binding id is not finalised until provisioning, so
            // the token is issued unbound here; it still verifies against the new
            // vault (an unbound token matches any binding).
            var window = new PayWindow(client, null);
            try
            {
                if (_ownerWindow != null)
                    await window.ShowDialog(_ownerWindow);
                else
                    window.Show();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Premium checkout window failed.");
                SubscriptionStatusText = "Checkout could not be opened.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(window.ResultToken))
            {
                PendingLicenseToken = window.ResultToken;
                IsPremiumActivated = true;
                SelectedPlanIsPremium = true;
                SubscriptionStatusText = "Premium activated. It will be sealed into your new vault.";
            }
            else
            {
                SubscriptionStatusText = "Checkout was not completed. You can upgrade later from Settings.";
            }
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

    public static partial class VaultFileProtection
    {

        [SupportedOSPlatform("windows")]
        public static void HardenVaultFiles(string vaultPath)
        {

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

        [SupportedOSPlatform("windows")]
        public static void StripDirectoryProtection(string phantomDirPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(phantomDirPath);
                dirInfo.Attributes = FileAttributes.Normal;

                if (OperatingSystem.IsWindows())
                {

                    var acl = dirInfo.GetAccessControl();
                    var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                    acl.RemoveAccessRuleAll(new FileSystemAccessRule(
                        users,
                        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                        AccessControlType.Deny));
                    dirInfo.SetAccessControl(acl);
                }

                foreach (var sub in Directory.EnumerateDirectories(phantomDirPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        new DirectoryInfo(sub).Attributes = FileAttributes.Normal;
                    }
                    catch { }
                }

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

        /// <summary>
        /// Exact filenames of Obscura-owned vault artifacts that can legitimately
        /// live OUTSIDE the <c>.phantom</c> folder (at the drive root or one level
        /// down). These names are specific enough to Obscura that the same set is
        /// what <c>WelcomePageViewModel</c> treats as an existing vault. Anything
        /// not in this list — and not inside a <c>.phantom</c> container — is never
        /// considered a remnant, so unrelated user files are never touched.
        /// </summary>
        public static readonly IReadOnlySet<string> KnownObscuraArtifactNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "system.bin",
                "obscura.vol",
                "vault.audit",
            };

        /// <summary>
        /// Suffixes appended to a volume name by <c>ObscuraVolumeService</c>'s atomic
        /// commit — the staging copy, the rollback copy, and the intent journal.
        ///
        /// These must be recognised as Obscura-owned. A commit that is interrupted (stick
        /// pulled, power lost) or whose cleanup delete fails leaves one of these next to the
        /// vault. The <c>.bak</c> in particular is a complete previous vault, encrypted under
        /// the same keyfile, so it is an openable snapshot containing credentials the user
        /// may have since deleted. Recovery sweeps them on the next open of THAT vault — but
        /// a stranded artifact from an abandoned provisioning attempt has no next open, and
        /// without these suffixes the remnant scanner walked straight past it and offered the
        /// user a "clean" drive that still held an old vault.
        /// </summary>
        public static readonly IReadOnlyList<string> ObscuraCommitArtifactSuffixes =
            new[] { ".tmp", ".bak", ".commit-journal" };

        /// <summary>
        /// Every filename the remnant scanner should look for at the drive root: the vault
        /// artifacts themselves plus each one's interrupted-commit leftovers.
        /// </summary>
        public static readonly IReadOnlySet<string> ScannableObscuraArtifactNames =
            new HashSet<string>(
                KnownObscuraArtifactNames.Concat(
                    KnownObscuraArtifactNames.SelectMany(
                        name => ObscuraCommitArtifactSuffixes.Select(suffix => name + suffix))),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when a path is definitively Obscura-owned: either it lives inside a
        /// <c>.phantom</c> container, or its filename is one of the well-known
        /// Obscura artifact names (including interrupted-commit leftovers). Used by both
        /// remnant detection and the wipe guard so the two stay perfectly consistent.
        /// </summary>
        public static bool IsObscuraOwnedArtifact(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
            if (FindPhantomAncestor(filePath) != null)
                return true;
            return ScannableObscuraArtifactNames.Contains(Path.GetFileName(filePath));
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

