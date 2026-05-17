using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Android.Services;

namespace PhantomVault.Android.ViewModels;

/// <summary>
/// USB-gate landing screen. The vault is intentionally inaccessible until a
/// Phantom-formatted removable drive is connected.
///
/// Behaviour matrix:
///   no drive            → "Connect your Phantom USB drive" + Help/About
///   drive, no vault     → "Set up new vault on this drive" CTA → setup wizard
///   drive, vault, bound → auto-route to unlock
///   drive, vault, unbound for this device → "Bind this device" CTA
/// </summary>
public sealed partial class WelcomeViewModel : BaseViewModel
{
    private readonly UsbVaultLocator _locator;

    [ObservableProperty] private bool   _hasDrive;
    [ObservableProperty] private bool   _hasVault;
    [ObservableProperty] private bool   _isDeviceBound;
    [ObservableProperty] private string _driveLabel = string.Empty;
    [ObservableProperty] private string _headline   = "Waiting for your Phantom USB drive…";
    [ObservableProperty] private string _detail     = "Plug your Phantom Obscura USB drive into this device to continue.";

    // Explicit visibility flags. We don't bind XAML directly to RelayCommand.CanExecute
    // because that's a method (not a property) and MAUI bindings won't re-evaluate
    // it reliably on state changes — that would leak stale UI like a "bind" button
    // showing while the headline still says "no drive".
    [ObservableProperty] private bool _canShowSetup;
    [ObservableProperty] private bool _canShowUnlock;
    [ObservableProperty] private bool _canShowBind;

    /// <summary>Raised when the gate believes the user should be sent to a destination route.</summary>
    public event System.Action<string>? NavigateRequested;

    public WelcomeViewModel(UsbVaultLocator locator)
    {
        _locator = locator;
        _locator.StateChanged += OnUsbStateChanged;

        // Bootstrap with whatever the locator already knows.
        OnUsbStateChanged(_locator.CurrentState);
    }

    [RelayCommand(CanExecute = nameof(CanRunSetup))]
    private Task SetupNewVaultAsync()
    {
        // Tell the shell to push the setup wizard with the locator's current drive.
        NavigateRequested?.Invoke("//setup");
        return Task.CompletedTask;
    }
    private bool CanRunSetup() => HasDrive && !HasVault;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private Task OpenUnlockAsync()
    {
        NavigateRequested?.Invoke("//unlock");
        return Task.CompletedTask;
    }
    private bool CanUnlock() => HasDrive && HasVault && IsDeviceBound;

    [RelayCommand(CanExecute = nameof(CanBindDevice))]
    private Task BindThisDeviceAsync()
    {
        // The Unlock page will handle the actual bind after a successful password check —
        // we just gate the user there so they prove vault ownership first.
        NavigateRequested?.Invoke("//unlock");
        return Task.CompletedTask;
    }
    private bool CanBindDevice() => HasDrive && HasVault && !IsDeviceBound;

    [RelayCommand]
    private void Rescan() => _locator.Rescan();

    private void OnUsbStateChanged(UsbVaultState state)
    {
        // Marshal to UI thread so commands re-evaluate their CanExecute correctly.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            HasDrive      = state.HasDrive;
            HasVault      = state.HasVault;
            IsDeviceBound = state.IsCurrentDeviceBound;
            DriveLabel    = state.DriveRoot ?? string.Empty;

            CanShowSetup  = state.HasDrive && !state.HasVault;
            CanShowUnlock = state.HasDrive &&  state.HasVault &&  state.IsCurrentDeviceBound;
            CanShowBind   = state.HasDrive &&  state.HasVault && !state.IsCurrentDeviceBound;

            if (!HasDrive)
            {
                Headline = "Waiting for your Phantom USB drive…";
                Detail   = "Plug your Phantom Obscura USB drive into this device to continue. The vault stays on the drive — never on this tablet.";
            }
            else if (!HasVault)
            {
                Headline = "Drive detected — no vault yet";
                Detail   = $"The drive at {DriveLabel} is empty. Tap below to set up a new Phantom vault on it.";
            }
            else if (!IsDeviceBound)
            {
                Headline = "New device for this vault";
                Detail   = "Enter your master password to authorise this tablet for the vault on this drive.";
            }
            else
            {
                Headline = "Ready to unlock";
                Detail   = $"Phantom vault found on {DriveLabel}.";
                // Auto-advance: the user has already authorised this device.
                NavigateRequested?.Invoke("//unlock");
            }

            // Re-evaluate command availability after state change.
            SetupNewVaultCommand.NotifyCanExecuteChanged();
            OpenUnlockCommand.NotifyCanExecuteChanged();
            BindThisDeviceCommand.NotifyCanExecuteChanged();
        });
    }
}
