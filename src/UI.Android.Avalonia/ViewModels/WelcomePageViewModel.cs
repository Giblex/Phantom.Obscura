using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Phase 3a stub of the desktop <c>WelcomePageViewModel</c>. Exposes every
/// property and command the AXAML binds to, with values chosen to render the
/// "landing" state on tablets (USB gate visible, no vault yet detected).
///
/// Replaced in a later phase by the real ViewModel once Windows-only
/// dependencies (<c>UsbDetector</c>, <c>BlackSecureRawVolumeService</c>,
/// <c>UsbBindingService</c>, <c>IPasskeyService</c>) have Android-side shims.
/// </summary>
public sealed partial class WelcomePageViewModel : ObservableObject
{
    // Navigation requests — handled by WelcomePage.xaml.cs which bridges them to the ShellViewModel.
    public event System.Action? RequestUnlock;
    public event System.Action? RequestDashboard;
    public event System.Action? RequestDefaultSetup;
    public event System.Action? RequestAdvancedSetup;
    public event System.Action? RequestOpenExistingVault;

    public WelcomePageViewModel()
    {
        DetectedVaults = new ObservableCollection<DetectedVaultLaunchRequest>();
        OpenVaultButtonText = "Open Vault";

        ShowDefaultSetupCommand  = new RelayCommand(() => RequestDefaultSetup?.Invoke());
        ShowAdvancedSetupCommand = new RelayCommand(() => RequestAdvancedSetup?.Invoke());
        GetStartedCommand        = new RelayCommand(() => { IsLandingVisible = false; IsSetupChoiceVisible = true; });
        OpenExistingVaultCommand = new RelayCommand(() => RequestOpenExistingVault?.Invoke());
        BackToWelcomeCommand     = new RelayCommand(() => { IsLandingVisible = true; IsSetupChoiceVisible = false; });
        DeveloperBypassCommand   = new RelayCommand(() => RequestDashboard?.Invoke());
    }

    // ── Header / branding ────────────────────────────────────────────────────
    [ObservableProperty] private string _appName        = "Phantom Obscura";
    [ObservableProperty] private string _welcomeMessage = "Plug in your Phantom USB drive to begin.";
    [ObservableProperty] private string _appVersion     = "v1.0";

    // ── Status / USB detection ───────────────────────────────────────────────
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
