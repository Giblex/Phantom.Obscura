using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.UI.Models;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Welcome / USB-gate ViewModel for the Avalonia.Android port.
///
/// Live-bound to <see cref="UsbDriveMonitor"/>. Whenever a removable drive
/// mounts / unmounts (or the emulator's simulated drive comes online in
/// DEBUG), the visible state — headline, detail copy, status pill, picker
/// list, "open vault" button enablement — flips accordingly.
///
/// Surface still mirrors the desktop <c>WelcomePageViewModel</c> so the AXAML
/// from <c>Views/WelcomePage.axaml</c> can bind against it unchanged.
/// </summary>
public sealed partial class WelcomePageViewModel : ObservableObject
{
    private readonly UsbDriveMonitor _monitor;

    // Navigation requests — handled by WelcomePage.xaml.cs which bridges them to the ShellViewModel.
    public event System.Action? RequestUnlock;
    public event System.Action? RequestDashboard;
    public event System.Action? RequestDefaultSetup;
    public event System.Action? RequestAdvancedSetup;
    public event System.Action? RequestOpenExistingVault;

    public WelcomePageViewModel() : this(UsbDriveMonitor.Instance) { }

    public WelcomePageViewModel(UsbDriveMonitor monitor)
    {
        _monitor = monitor;
        DetectedVaults = new ObservableCollection<DetectedVaultLaunchRequest>();
        OpenVaultButtonText = "Open Vault";

        ShowDefaultSetupCommand  = new RelayCommand(() => RequestDefaultSetup?.Invoke());
        ShowAdvancedSetupCommand = new RelayCommand(() => RequestAdvancedSetup?.Invoke());
        GetStartedCommand        = new RelayCommand(() => { IsLandingVisible = false; IsSetupChoiceVisible = true; });
        OpenExistingVaultCommand = new RelayCommand(() =>
        {
            // If we've already detected a vault on USB, route to unlock; otherwise
            // fall back to the legacy "open existing" navigation request.
            if (HasRecognizedVaults) RequestUnlock?.Invoke();
            else                     RequestOpenExistingVault?.Invoke();
        });
        BackToWelcomeCommand     = new RelayCommand(() => { IsLandingVisible = true; IsSetupChoiceVisible = false; });
        DeveloperBypassCommand   = new RelayCommand(() => RequestDashboard?.Invoke());

        // Subscribe + seed.
        _monitor.DrivesChanged += OnDrivesChanged;
        OnDrivesChanged(_monitor.CurrentDrives);
    }

    private void OnDrivesChanged(IReadOnlyList<UsbDriveInfo> drives)
    {
        // Always marshal to the UI thread — the BroadcastReceiver fires on a
        // pool thread and ObservableCollection isn't safe for cross-thread mutation.
        if (Dispatcher.UIThread.CheckAccess()) Apply(drives);
        else                                   Dispatcher.UIThread.Post(() => Apply(drives));
    }

    private void Apply(IReadOnlyList<UsbDriveInfo> drives)
    {
        var withVault = drives.Where(d => d.HasVault).ToList();
        var anyDrive  = drives.Count > 0;

        HasRecognizedVaults = withVault.Count > 0;
        ShowVaultPicker     = withVault.Count > 1;
        HasSelectedVault    = withVault.Count >= 1;
        IsDeviceDetectionActive = !anyDrive;
        DetectedVaultPathDisplay = withVault.FirstOrDefault()?.MountPath ?? string.Empty;

        // Refresh the picker list when there's more than one vault on the connected drives.
        DetectedVaults.Clear();
        foreach (var d in withVault)
        {
            DetectedVaults.Add(new DetectedVaultLaunchRequest
            {
                DisplayName = d.Label,
                VaultPath   = d.MountPath,
            });
        }

        // Headline / detail / status copy — three distinct visible states.
        if (HasRecognizedVaults)
        {
            DeviceLinkHeadline = withVault.Count == 1
                ? $"Vault detected on {withVault[0].Label}"
                : $"{withVault.Count} vaults detected";
            DeviceLinkDetail   = "Tap Open Vault to unlock. The vault stays on the drive while this tablet reads it.";
            StatusMessage      = "USB ready";
            OpenVaultButtonText = "Open Vault";
        }
        else if (anyDrive)
        {
            var d = drives[0];
            DeviceLinkHeadline = $"Drive connected: {d.Label}";
            DeviceLinkDetail   = "No Phantom vault found on this drive. Use Get Started to provision one, or connect a drive that already holds a vault.";
            StatusMessage      = "Connected — no vault";
            OpenVaultButtonText = "Open Vault";
        }
        else
        {
            DeviceLinkHeadline = "Searching for a Phantom USB drive";
            DeviceLinkDetail   = "Connect the USB drive that holds your Phantom vault. The vault never leaves the drive — this tablet only reads it while connected.";
            StatusMessage      = "Waiting for USB drive…";
            OpenVaultButtonText = "Open Vault";
        }
    }

    // ── Header / branding ────────────────────────────────────────────────────
    [ObservableProperty] private string _appName        = "Phantom Obscura";
    [ObservableProperty] private string _welcomeMessage = "Plug in your Phantom USB drive to begin.";
    [ObservableProperty] private string _appVersion     = "v1.0";

    // ── Status / USB detection (mutated by Apply()) ─────────────────────────
    [ObservableProperty] private string _statusMessage             = "Waiting for USB drive…";
    [ObservableProperty] private bool   _isDeviceDetectionActive   = true;
    [ObservableProperty] private string _deviceLinkHeadline        = "Searching for a Phantom USB drive";
    [ObservableProperty] private string _deviceLinkDetail          = "Connect the USB drive that holds your Phantom vault. The vault never leaves the drive — this tablet only reads it while connected.";
    [ObservableProperty] private string _detectedVaultPathDisplay  = string.Empty;
    [ObservableProperty] private bool   _hasRecognizedVaults;
    [ObservableProperty] private bool   _showVaultPicker;
    [ObservableProperty] private bool   _hasSelectedVault;
    [ObservableProperty] private string _openVaultButtonText;

    // ── Visibility gates ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLandingVisible      = true;
    [ObservableProperty] private bool _isSetupChoiceVisible  = false;
    [ObservableProperty] private bool _isDeveloperBypassVisible = false;

    // ── Collections ─────────────────────────────────────────────────────────
    public ObservableCollection<DetectedVaultLaunchRequest> DetectedVaults { get; }

    // ── Commands ────────────────────────────────────────────────────────────
    public ICommand ShowDefaultSetupCommand  { get; }
    public ICommand ShowAdvancedSetupCommand { get; }
    public ICommand GetStartedCommand        { get; }
    public ICommand OpenExistingVaultCommand { get; }
    public ICommand BackToWelcomeCommand     { get; }
    public ICommand DeveloperBypassCommand   { get; }
}
