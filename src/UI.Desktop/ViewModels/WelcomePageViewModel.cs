using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Privileged;
using PhantomVault.UI.Models;
using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public sealed class WelcomePageViewModel : ReactiveObject
    {
        private readonly DialogService _dialogService;
        private readonly UsbDetector _usbDetector;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private readonly UsbBindingService _usbBindingService;
        private readonly IPasskeyService _passkeyService;
        private readonly DispatcherTimer _scanTimer;
        private const int DetectionPresentationDelayMs = 2200;

        private bool _isCheckingForVault;
        private bool _hasExistingVault;
        private bool _hasUsbDevice;
        private bool _hasRecognizedVaults;
        private bool _isDeviceDetectionActive;
        private string _statusMessage = "Please insert a USB device";
        private string _deviceLinkHeadline = "Please insert a USB device";
        private string _deviceLinkDetail = "Insert a removable drive to create a new vault or open an existing one.";
        private string? _detectedUsbPath;
        private string? _detectedUsbDisplayName;
        private bool _scanInProgress;
        private string? _lastAutoOpenedVaultPath;
        private string? _lastPresentedDetectionSignature;
        private DetectedVaultLaunchRequest? _selectedVault;
        private Window? _ownerWindow;
        private bool _isSetupChoiceVisible;

        public event EventHandler<DetectedVaultLaunchRequest>? NavigateToSecurityCheck;
        public event EventHandler? NavigateToSetupWizard;
        public event EventHandler? NavigateToQuickSetup;
#pragma warning disable CS0067
        public event EventHandler? NavigateToUsbSetup;
        public event EventHandler? DeveloperBypassRequested;
#pragma warning restore CS0067

        public WelcomePageViewModel(
            UsbDetector usbDetector,
            BlackSecureRawVolumeService blackSecureRawVolumeService,
            UsbBindingService usbBindingService,
            IPasskeyService passkeyService)
        {
            _dialogService = new DialogService();
            _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
            _blackSecureRawVolumeService = blackSecureRawVolumeService ?? throw new ArgumentNullException(nameof(blackSecureRawVolumeService));
            _usbBindingService = usbBindingService ?? throw new ArgumentNullException(nameof(usbBindingService));
            _passkeyService = passkeyService ?? throw new ArgumentNullException(nameof(passkeyService));

            DetectedVaults = new ObservableCollection<DetectedVaultLaunchRequest>();

#if DEBUG
            IsDeveloperBypassVisible = true;
            DeveloperBypassCommand = ReactiveCommand.Create(ExecuteDeveloperBypass);
#else
            IsDeveloperBypassVisible = false;
            DeveloperBypassCommand = ReactiveCommand.Create(() => { });
#endif

            GetStartedCommand = ReactiveCommand.CreateFromTask(GetStartedAsync);
            ShowAdvancedSetupCommand = ReactiveCommand.CreateFromTask(ShowAdvancedSetupAsync);
            ShowDefaultSetupCommand = ReactiveCommand.CreateFromTask(ShowDefaultSetupAsync);
            BackToWelcomeCommand = ReactiveCommand.Create(HideSetupChoice);
            OpenExistingVaultCommand = ReactiveCommand.CreateFromTask(OpenExistingVaultAsync);

            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _scanTimer.Tick += async (_, _) => await RefreshUsbStateAsync().ConfigureAwait(false);
            _scanTimer.Start();

            _ = RefreshUsbStateAsync();
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        /// <summary>
        /// Stops the background USB/vault scan. Call this once the welcome page is
        /// hidden behind a vault or setup window — hiding alone leaves the 2-second
        /// timer running for the rest of the app's life, which keeps hammering the
        /// privileged broker (and, if it's not installed, kept popping the "Enable
        /// privileged helper" dialog on every tick).
        /// </summary>
        public void StopScanning()
        {
            _scanTimer.Stop();
        }

        public string AppName => "Phantom Obscura";
        public string AppVersion => "5.0.0";
        public string WelcomeMessage => "USB-bound post-quantum vault protection";
        public bool IsSetupChoiceVisible
        {
            get => _isSetupChoiceVisible;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isSetupChoiceVisible, value);
                this.RaisePropertyChanged(nameof(IsLandingVisible));
            }
        }

        public bool IsLandingVisible => !IsSetupChoiceVisible;

        public ObservableCollection<DetectedVaultLaunchRequest> DetectedVaults { get; }

        // Hard cap on how many vaults the user can load at once.
        public const int MaxVaultSlots = 3;

        // Fixed-width slot accessors so the welcome page can render three
        // slots even when there are fewer detected vaults (null → empty slot).
        public DetectedVaultLaunchRequest? Slot1 => DetectedVaults.ElementAtOrDefault(0);
        public DetectedVaultLaunchRequest? Slot2 => DetectedVaults.ElementAtOrDefault(1);
        public DetectedVaultLaunchRequest? Slot3 => DetectedVaults.ElementAtOrDefault(2);
        public bool Slot1HasVault => Slot1 != null;
        public bool Slot2HasVault => Slot2 != null;
        public bool Slot3HasVault => Slot3 != null;

        private void RaiseSlotChanges()
        {
            this.RaisePropertyChanged(nameof(Slot1));
            this.RaisePropertyChanged(nameof(Slot2));
            this.RaisePropertyChanged(nameof(Slot3));
            this.RaisePropertyChanged(nameof(Slot1HasVault));
            this.RaisePropertyChanged(nameof(Slot2HasVault));
            this.RaisePropertyChanged(nameof(Slot3HasVault));
        }

        public bool IsCheckingForVault
        {
            get => _isCheckingForVault;
            private set => this.RaiseAndSetIfChanged(ref _isCheckingForVault, value);
        }

        public bool HasExistingVault
        {
            get => _hasExistingVault;
            private set => this.RaiseAndSetIfChanged(ref _hasExistingVault, value);
        }

        public bool HasUsbDevice
        {
            get => _hasUsbDevice;
            private set => this.RaiseAndSetIfChanged(ref _hasUsbDevice, value);
        }

        public bool HasRecognizedVaults
        {
            get => _hasRecognizedVaults;
            private set => this.RaiseAndSetIfChanged(ref _hasRecognizedVaults, value);
        }

        public bool IsDeviceDetectionActive
        {
            get => _isDeviceDetectionActive;
            private set => this.RaiseAndSetIfChanged(ref _isDeviceDetectionActive, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string DeviceLinkHeadline
        {
            get => _deviceLinkHeadline;
            private set => this.RaiseAndSetIfChanged(ref _deviceLinkHeadline, value);
        }

        public string DeviceLinkDetail
        {
            get => _deviceLinkDetail;
            private set => this.RaiseAndSetIfChanged(ref _deviceLinkDetail, value);
        }

        public string DetectedVaultPathDisplay => string.IsNullOrWhiteSpace(_detectedUsbPath)
            ? "Waiting for removable media"
            : _detectedUsbPath;

        public string UsbDisplayName => string.IsNullOrWhiteSpace(_detectedUsbDisplayName)
            ? "No device connected"
            : _detectedUsbDisplayName;

        public bool ShowVaultPicker => HasRecognizedVaults && DetectedVaults.Count > 0;
        public bool HasSelectedVault => SelectedVault != null;

        public string OpenVaultButtonText => HasSelectedVault
            ? "Open"
            : "Access your vault";

        public string GetStartedButtonText => HasUsbDevice && !HasRecognizedVaults
            ? "Set up this USB"
            : "Get Started";

        public DetectedVaultLaunchRequest? SelectedVault
        {
            get => _selectedVault;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedVault, value);
                if (value != null)
                {
                    _detectedUsbPath = value.UsbPath;
                    _detectedUsbDisplayName = value.UsbDisplayName;
                    DeviceLinkHeadline = $"Detected USB: {value.UsbDisplayName}";
                    DeviceLinkDetail = DetectedVaults.Count > 1
                        ? "Select which vault on this USB device you want to open."
                        : "Recognized vault ready. Device-linked verification will run automatically when it opens.";
                }

                this.RaisePropertyChanged(nameof(DetectedVaultPathDisplay));
                this.RaisePropertyChanged(nameof(UsbDisplayName));
                this.RaisePropertyChanged(nameof(OpenVaultButtonText));
                this.RaisePropertyChanged(nameof(HasSelectedVault));
            }
        }

        public ReactiveCommand<Unit, Unit> GetStartedCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDefaultSetupCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdvancedSetupCommand { get; }
        public ReactiveCommand<Unit, Unit> BackToWelcomeCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenExistingVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> DeveloperBypassCommand { get; }
        public bool IsDeveloperBypassVisible { get; }

#if DEBUG
        private void ExecuteDeveloperBypass()
        {
            StatusMessage = "Developer bypass active (debug build).";
            DeveloperBypassRequested?.Invoke(this, EventArgs.Empty);
        }
#endif

        private async Task RefreshUsbStateAsync()
        {
            if (_scanInProgress)
                return;

            _scanInProgress = true;
            IsCheckingForVault = true;
            IsDeviceDetectionActive = true;

            try
            {
                var removableDrives = _usbDetector
                    .GetRemovableDrives()
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (removableDrives.Count == 0)
                {
                    // USB-only policy: no removable drive → nothing to show.
                    // Local vaults on disk are no longer surfaced in the slot picker.
                    ApplyNoUsbState();
                    return;
                }

                HasUsbDevice = true;
                DeviceLinkHeadline = "Detecting";
                DeviceLinkDetail = "Scanning the connected USB device for Phantom Obscura vaults.";
                StatusMessage = "Detecting";

                var discoveredVaults = new List<DetectedVaultLaunchRequest>();
                foreach (var driveRoot in removableDrives)
                {
                    discoveredVaults.AddRange(DiscoverVaultsOnDrive(driveRoot));
                }

                if (discoveredVaults.Count == 0)
                {
                    // USB-only policy: never fall back to local-disk vaults.
                    await EnsureDetectionPresentationDelayAsync($"unrecognized:{removableDrives[0]}", true).ConfigureAwait(false);
                    ApplyUnrecognizedUsbState(removableDrives[0]);
                    return;
                }

                await EnsureDetectionPresentationDelayAsync(BuildDetectionSignature(discoveredVaults), true).ConfigureAwait(false);
                ApplyRecognizedVaultState(discoveredVaults);
                _ = TryAutoOpenSelectedVaultAsync();
            }
            catch (PrivilegedBrokerUnavailableException)
            {
                // Background detection is best-effort and must stay silent when the privileged
                // helper isn't installed — surfacing it as a modal turns into a per-scan nag.
                ApplyNoUsbState();
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Vault Detection Error",
                    "USB devices and vaults could not be checked. Reconnect the device and scan again.",
                    _ownerWindow);
                ApplyNoUsbState();
                Serilog.Log.Warning(ex, "[Welcome] Vault-device detection failed.");
                StatusMessage = "Vault devices could not be detected. Reconnect the device and scan again.";
            }
            finally
            {
                IsCheckingForVault = false;
                if (!HasRecognizedVaults)
                {
                    IsDeviceDetectionActive = false;
                }

                _scanInProgress = false;
            }
        }

        private void ApplyNoUsbState()
        {
            HasUsbDevice = false;
            HasRecognizedVaults = false;
            HasExistingVault = false;
            IsDeviceDetectionActive = false;
            _lastPresentedDetectionSignature = "none";
            _detectedUsbPath = null;
            _detectedUsbDisplayName = null;
            DeviceLinkHeadline = "Please insert a USB device";
            DeviceLinkDetail = "Insert a removable drive to create a new vault or open an existing one.";
            StatusMessage = "Please insert a USB device";
            DetectedVaults.Clear();
            RaiseSlotChanges();
            SelectedVault = null;
            this.RaisePropertyChanged(nameof(DetectedVaultPathDisplay));
            this.RaisePropertyChanged(nameof(UsbDisplayName));
            this.RaisePropertyChanged(nameof(ShowVaultPicker));
            this.RaisePropertyChanged(nameof(GetStartedButtonText));
        }

        private void ApplyUnrecognizedUsbState(string usbPath)
        {
            HasUsbDevice = true;
            HasRecognizedVaults = false;
            HasExistingVault = false;
            IsDeviceDetectionActive = false;
            _lastPresentedDetectionSignature = $"unrecognized:{usbPath}";
            _detectedUsbPath = usbPath;
            _detectedUsbDisplayName = GetUsbDisplayName(usbPath);
            DeviceLinkHeadline = "USB device detected";
            DeviceLinkDetail = "This USB is not recognized by Phantom Obscura yet. You can proceed to set up the device now.";
            StatusMessage = "USB device detected";
            DetectedVaults.Clear();
            RaiseSlotChanges();
            SelectedVault = null;
            this.RaisePropertyChanged(nameof(DetectedVaultPathDisplay));
            this.RaisePropertyChanged(nameof(UsbDisplayName));
            this.RaisePropertyChanged(nameof(ShowVaultPicker));
            this.RaisePropertyChanged(nameof(GetStartedButtonText));
        }

        private void ApplyRecognizedVaultState(IReadOnlyList<DetectedVaultLaunchRequest> discoveredVaults)
        {
            HasRecognizedVaults = true;
            HasExistingVault = true;
            IsDeviceDetectionActive = false;
            _lastPresentedDetectionSignature = BuildDetectionSignature(discoveredVaults);

            var previousSelection = SelectedVault?.VaultPath;
            DetectedVaults.Clear();
            // Cap at MaxVaultSlots so the fixed 3-slot UI never has to scroll
            // or hide overflow — extras beyond the cap are dropped from the picker.
            foreach (var vault in discoveredVaults.Take(MaxVaultSlots))
            {
                DetectedVaults.Add(vault);
            }
            RaiseSlotChanges();

            SelectedVault = DetectedVaults.FirstOrDefault(v => string.Equals(v.VaultPath, previousSelection, StringComparison.OrdinalIgnoreCase))
                ?? DetectedVaults.FirstOrDefault();

            if (SelectedVault != null)
            {
                // A vault entry with a null UsbPath (see DiscoverKnownLocalVaults) is stored
                // locally, not on a physical device — no USB was actually detected for it, so
                // don't claim otherwise in HasUsbDevice/headline/status.
                bool isDeviceBacked = SelectedVault.UsbPath != null;
                HasUsbDevice = isDeviceBacked;
                _detectedUsbPath = SelectedVault.UsbPath;
                _detectedUsbDisplayName = SelectedVault.UsbDisplayName;

                DeviceLinkHeadline = isDeviceBacked
                    ? $"Detected USB: {SelectedVault.UsbDisplayName}"
                    : "Local vault found";
                DeviceLinkDetail = DetectedVaults.Count > 1
                    ? "Select a vault from the list below, then open it."
                    : isDeviceBacked
                        ? "Recognized vault ready. If the linked device authenticator is available, Phantom Obscura will continue into verification automatically."
                        : "Recognized local vault ready. Open it to continue.";
                StatusMessage = isDeviceBacked ? "USB device detected" : "Local vault found";
            }
            else
            {
                HasUsbDevice = false;
            }

            this.RaisePropertyChanged(nameof(DetectedVaultPathDisplay));
            this.RaisePropertyChanged(nameof(UsbDisplayName));
            this.RaisePropertyChanged(nameof(ShowVaultPicker));
            this.RaisePropertyChanged(nameof(GetStartedButtonText));
        }

        private async Task TryAutoOpenSelectedVaultAsync()
        {
            var selectedVault = SelectedVault;
            if (selectedVault == null)
                return;

            if (DetectedVaults.Count != 1 || !selectedVault.AutoOpenEligible || !_passkeyService.IsSupported)
                return;

            if (string.Equals(_lastAutoOpenedVaultPath, selectedVault.VaultPath, StringComparison.OrdinalIgnoreCase))
                return;

            _lastAutoOpenedVaultPath = selectedVault.VaultPath;
            IsDeviceDetectionActive = true;
            DeviceLinkDetail = "Linked device binding detected. Verifying and opening the vault automatically.";
            StatusMessage = "Opening linked vault...";

            await Task.Delay(700);
            await Dispatcher.UIThread.InvokeAsync(() => NavigateToSecurityCheck?.Invoke(this, selectedVault));
        }

        private async Task GetStartedAsync()
        {
            StatusMessage = "Choose your setup path.";
            await Task.Delay(150);
            IsSetupChoiceVisible = true;
        }

        private async Task ShowDefaultSetupAsync()
        {
            StatusMessage = "Opening default setup...";
            await Task.Delay(200);
            await Dispatcher.UIThread.InvokeAsync(() => NavigateToQuickSetup?.Invoke(this, EventArgs.Empty));
        }

        private async Task ShowAdvancedSetupAsync()
        {
            StatusMessage = "Opening advanced setup...";
            await Task.Delay(200);
            await Dispatcher.UIThread.InvokeAsync(() => NavigateToSetupWizard?.Invoke(this, EventArgs.Empty));
        }

        private void HideSetupChoice()
        {
            IsSetupChoiceVisible = false;
            StatusMessage = HasUsbDevice ? "USB device detected" : "Please insert a USB device";
        }

        private async Task OpenExistingVaultAsync()
        {
            if (SelectedVault == null)
            {
                await _dialogService.ShowWarningAsync(
                    "No Vault Selected",
                    "Select a detected vault from the connected USB device first.",
                    _ownerWindow);
                return;
            }

            StatusMessage = $"Opening {SelectedVault.DisplayName}...";
            IsDeviceDetectionActive = true;
            await Task.Delay(250);
            await Dispatcher.UIThread.InvokeAsync(() => NavigateToSecurityCheck?.Invoke(this, SelectedVault));
        }

        public void SelectDetectedVault(DetectedVaultLaunchRequest? launchRequest)
        {
            if (launchRequest == null)
                return;

            SelectedVault = launchRequest;
            StatusMessage = $"Selected {launchRequest.DisplayName}";
        }

        private List<DetectedVaultLaunchRequest> DiscoverKnownLocalVaults()
        {
            var vaults = new List<DetectedVaultLaunchRequest>();
            var settings = SettingsService.Load();

            foreach (var vaultPath in settings.KnownLocalVaultPaths.ToList())
            {
                if (!Directory.Exists(vaultPath))
                    continue;

                var packedVolume = Path.Combine(vaultPath, "obscura.vol");
                if (File.Exists(packedVolume))
                {
                    vaults.Add(new DetectedVaultLaunchRequest
                    {
                        UsbPath = null,
                        UsbDisplayName = "Local",
                        VaultPath = packedVolume,
                        DisplayName = BuildVaultDisplayName(packedVolume, "Local vault"),
                        AutoOpenEligible = false
                    });
                    continue;
                }

                var rootPath = Path.Combine(vaultPath, "root");
                if (!Directory.Exists(rootPath))
                    continue;

                foreach (var container in Directory.GetFiles(rootPath, "*.pvault", SearchOption.TopDirectoryOnly))
                {
                    vaults.Add(new DetectedVaultLaunchRequest
                    {
                        UsbPath = null,
                        UsbDisplayName = "Local",
                        VaultPath = container,
                        DisplayName = BuildVaultDisplayName(container, "Local vault"),
                        AutoOpenEligible = false
                    });
                }
            }

            return vaults;
        }

        private List<DetectedVaultLaunchRequest> DiscoverVaultsOnDrive(string driveRoot)
        {
            var vaults = new List<DetectedVaultLaunchRequest>();
            var usbDisplayName = GetUsbDisplayName(driveRoot);
            bool autoOpenEligible = _usbBindingService.HasHiddenDeviceId(driveRoot);
            var packedVaultCandidates = DiscoverPackedVaultsOnDrive(driveRoot, usbDisplayName, autoOpenEligible);
            if (packedVaultCandidates.Count > 0)
            {
                return packedVaultCandidates;
            }

            var rootPath = Path.Combine(driveRoot, ".phantom", "root");
            if (Directory.Exists(rootPath))
            {
                foreach (var rootContainer in Directory.GetFiles(rootPath, "*.pvault", SearchOption.TopDirectoryOnly))
                {
                    vaults.Add(new DetectedVaultLaunchRequest
                    {
                        UsbPath = driveRoot,
                        UsbDisplayName = usbDisplayName,
                        VaultPath = rootContainer,
                        DisplayName = BuildVaultDisplayName(rootContainer, "Container vault"),
                        AutoOpenEligible = autoOpenEligible
                    });
                }
            }

            if (vaults.Count == 0)
            {
                var packedVolumePath = ResolveMasterVolumePath(driveRoot);
                if (!string.IsNullOrWhiteSpace(packedVolumePath))
                {
                    vaults.Add(new DetectedVaultLaunchRequest
                    {
                        UsbPath = driveRoot,
                        UsbDisplayName = usbDisplayName,
                        VaultPath = packedVolumePath,
                        DisplayName = "Ghost Secured vault",
                        AutoOpenEligible = autoOpenEligible
                    });
                }
            }

            if (vaults.Count == 0)
            {
                var legacyVaultsPath = Path.Combine(driveRoot, ".phantom", "vaults");
                if (Directory.Exists(legacyVaultsPath))
                {
                    foreach (var vaultContainer in Directory.GetFiles(legacyVaultsPath, "*.pvault", SearchOption.TopDirectoryOnly))
                    {
                        vaults.Add(new DetectedVaultLaunchRequest
                        {
                            UsbPath = driveRoot,
                            UsbDisplayName = usbDisplayName,
                            VaultPath = vaultContainer,
                            DisplayName = BuildVaultDisplayName(vaultContainer, "Legacy vault"),
                            AutoOpenEligible = autoOpenEligible
                        });
                    }
                }
            }

            if (vaults.Count == 0)
            {
                var manifestsPath = Path.Combine(driveRoot, ".phantom", "manifests");
                if (Directory.Exists(manifestsPath))
                {
                    foreach (var manifestPath in Directory.GetFiles(manifestsPath, "*.manifest", SearchOption.TopDirectoryOnly))
                    {
                        vaults.Add(new DetectedVaultLaunchRequest
                        {
                            UsbPath = driveRoot,
                            UsbDisplayName = usbDisplayName,
                            VaultPath = manifestPath,
                            DisplayName = BuildVaultDisplayName(manifestPath, "Manifest vault"),
                            AutoOpenEligible = false
                        });
                    }
                }
            }

            return vaults;
        }

        private List<DetectedVaultLaunchRequest> DiscoverPackedVaultsOnDrive(string driveRoot, string usbDisplayName, bool autoOpenEligible)
        {
            var packedVaults = new List<DetectedVaultLaunchRequest>();
            var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddPackedVaultCandidate(candidatePaths, PhantomDeviceLayout.GetSystemVolumePath(driveRoot));
            AddPackedVaultCandidate(candidatePaths, Path.Combine(driveRoot, "system.bin"));
            AddPackedVaultCandidate(candidatePaths, Path.Combine(driveRoot, ".phantom", "obscura.vol"));

            foreach (var directory in SafeEnumerateDirectories(driveRoot))
            {
                AddPackedVaultCandidate(candidatePaths, Path.Combine(directory, "system.bin"));
                AddPackedVaultCandidate(candidatePaths, Path.Combine(directory, "obscura.vol"));
            }

            foreach (var packedPath in candidatePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                packedVaults.Add(new DetectedVaultLaunchRequest
                {
                    UsbPath = driveRoot,
                    UsbDisplayName = usbDisplayName,
                    VaultPath = packedPath,
                    DisplayName = BuildPackedVaultDisplayName(driveRoot, packedPath),
                    AutoOpenEligible = autoOpenEligible
                });
            }

            return packedVaults;
        }

        private static void AddPackedVaultCandidate(ISet<string> candidates, string path)
        {
            if (File.Exists(path))
            {
                candidates.Add(path);
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string driveRoot)
        {
            try
            {
                return Directory.EnumerateDirectories(driveRoot, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private async Task<List<DetectedVaultLaunchRequest>> DiscoverRawVaultsAsync()
        {
            var vaults = new List<DetectedVaultLaunchRequest>();

            try
            {
                foreach (var rawSelection in _blackSecureRawVolumeService.GetSelectableRawDevices())
                {
                    var physicalDrivePath = _blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromSelection(rawSelection);
                    if (string.IsNullOrWhiteSpace(physicalDrivePath))
                        continue;

                    if (!await _blackSecureRawVolumeService.IsBlackSecureVolumeAsync(physicalDrivePath).ConfigureAwait(false))
                        continue;

                    vaults.Add(new DetectedVaultLaunchRequest
                    {
                        UsbPath = rawSelection,
                        UsbDisplayName = rawSelection,
                        VaultPath = rawSelection,
                        DisplayName = "Phantom Secured vault",
                        AutoOpenEligible = false
                    });
                }
            }
            catch (PrivilegedBrokerUnavailableException)
            {
                // Passive welcome-page detection must never demand elevation. Probing raw
                // physical volumes needs the privileged helper, but the only tier that used
                // them ("Phantom Secured"/BlackSecure) has been retired — so a missing helper
                // here simply means there are no legacy raw vaults to surface. Degrade
                // silently rather than popping the "Vault Detection Error" nag on every scan.
            }

            return vaults;
        }

        private string GetUsbDisplayName(string driveRoot)
        {
            var driveInfo = _usbDetector.GetDriveInfo(driveRoot);
            if (driveInfo != null)
            {
                return string.IsNullOrWhiteSpace(driveInfo.Label)
                    ? driveInfo.RootPath
                    : $"{driveInfo.Label} ({driveInfo.RootPath})";
            }

            try
            {
                var drive = new DriveInfo(driveRoot);
                return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name})";
            }
            catch
            {
                return driveRoot;
            }
        }

        private static string BuildVaultDisplayName(string vaultPath, string fallbackLabel)
        {
            var fileName = Path.GetFileNameWithoutExtension(vaultPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return fallbackLabel;

            if (string.Equals(fileName, "root", StringComparison.OrdinalIgnoreCase))
                return fallbackLabel;

            return fileName.Replace('_', ' ');
        }

        private static string BuildPackedVaultDisplayName(string driveRoot, string packedPath)
        {
            var relativePath = Path.GetRelativePath(driveRoot, packedPath);
            var parentFolder = Path.GetDirectoryName(relativePath);

            if (string.IsNullOrWhiteSpace(parentFolder) || parentFolder == ".")
                return "Ghost Secured vault";

            var sanitized = parentFolder.Replace(".phantom", "Hidden vault", StringComparison.OrdinalIgnoreCase)
                                        .Replace('\\', ' ')
                                        .Replace('/', ' ')
                                        .Trim();

            return string.IsNullOrWhiteSpace(sanitized)
                ? "Ghost Secured vault"
                : sanitized;
        }

        private static string? ResolveMasterVolumePath(string usbPath)
        {
            var candidates = new[]
            {
                PhantomDeviceLayout.GetSystemVolumePath(usbPath),
                Path.Combine(usbPath, "system.bin"),
                Path.Combine(usbPath, ".phantom", "obscura.vol")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private async Task EnsureDetectionPresentationDelayAsync(string detectionSignature, bool usbPresent)
        {
            if (!usbPresent || string.Equals(_lastPresentedDetectionSignature, detectionSignature, StringComparison.Ordinal))
                return;

            await Task.Delay(DetectionPresentationDelayMs).ConfigureAwait(false);
        }

        private static string BuildDetectionSignature(IReadOnlyCollection<DetectedVaultLaunchRequest> vaults)
        {
            return "recognized:" + string.Join("|", vaults
                .Select(vault => $"{vault.UsbPath}>{vault.VaultPath}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }
    }
}

