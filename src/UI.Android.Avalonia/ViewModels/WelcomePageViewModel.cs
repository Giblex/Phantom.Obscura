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

public sealed partial class WelcomePageViewModel : ObservableObject
{
    private readonly UsbDriveMonitor _monitor;

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

            if (HasRecognizedVaults) RequestUnlock?.Invoke();
            else                     RequestOpenExistingVault?.Invoke();
        });
        BackToWelcomeCommand     = new RelayCommand(() => { IsLandingVisible = true; IsSetupChoiceVisible = false; });
        DeveloperBypassCommand   = new RelayCommand(() => RequestDashboard?.Invoke());

        _monitor.DrivesChanged += OnDrivesChanged;
        OnDrivesChanged(_monitor.CurrentDrives);
    }

    private void OnDrivesChanged(IReadOnlyList<UsbDriveInfo> drives)
    {

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

        DetectedVaults.Clear();
        foreach (var d in withVault)
        {
            DetectedVaults.Add(new DetectedVaultLaunchRequest
            {
                DisplayName = d.Label,
                VaultPath   = d.MountPath,
            });
        }

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

    [ObservableProperty] private string _appName        = "Phantom Obscura";
    [ObservableProperty] private string _welcomeMessage = "Plug in your Phantom USB drive to begin.";
    [ObservableProperty] private string _appVersion     = "v1.0";

    [ObservableProperty] private string _statusMessage             = "Waiting for USB drive…";
    [ObservableProperty] private bool   _isDeviceDetectionActive   = true;
    [ObservableProperty] private string _deviceLinkHeadline        = "Searching for a Phantom USB drive";
    [ObservableProperty] private string _deviceLinkDetail          = "Connect the USB drive that holds your Phantom vault. The vault never leaves the drive — this tablet only reads it while connected.";
    [ObservableProperty] private string _detectedVaultPathDisplay  = string.Empty;
    [ObservableProperty] private bool   _hasRecognizedVaults;
    [ObservableProperty] private bool   _showVaultPicker;
    [ObservableProperty] private bool   _hasSelectedVault;
    [ObservableProperty] private string _openVaultButtonText;

    [ObservableProperty] private bool _isLandingVisible      = true;
    [ObservableProperty] private bool _isSetupChoiceVisible  = false;
    [ObservableProperty] private bool _isDeveloperBypassVisible = false;

    public ObservableCollection<DetectedVaultLaunchRequest> DetectedVaults { get; }

    public ICommand ShowDefaultSetupCommand  { get; }
    public ICommand ShowAdvancedSetupCommand { get; }
    public ICommand GetStartedCommand        { get; }
    public ICommand OpenExistingVaultCommand { get; }
    public ICommand BackToWelcomeCommand     { get; }
    public ICommand DeveloperBypassCommand   { get; }
}

