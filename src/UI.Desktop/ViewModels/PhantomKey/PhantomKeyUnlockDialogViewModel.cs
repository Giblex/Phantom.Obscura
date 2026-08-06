using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Phantom.Sync.Usb;
using Phantom.UI.Shared.PhantomKey;
using PhantomKey.Integration;

namespace PhantomVault.UI.ViewModels.PhantomKey;

/// <summary>
/// Obscura-side VM for the PhantomKey unlock dialog. Backs the shared
/// PhantomKeyUnlockControl. Talks to the PhantomKey broker over its pipe —
/// no keys, no PIN state leaves this process into Obscura's own vault code.
/// The successful-unlock result is exposed via <see cref="Unlocked"/> for
/// the caller to consume.
/// </summary>
public partial class PhantomKeyUnlockDialogViewModel : ObservableObject
{
    private const string BrokerPipeName = "phantomkey.broker";
    private const string DefaultRpId = "phantomkey.local";

    public ObservableCollection<PhantomKeyDrive> Drives { get; } = new();

    [ObservableProperty] private PhantomKeyDrive? _selectedDrive;
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _unlocked;

    public ICommand RescanCommand { get; }
    public ICommand UnlockCommand { get; }

    public PhantomKeyUnlockDialogViewModel()
    {
        RescanCommand = new RelayCommand(Rescan);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync);
        Rescan();
    }

    private void Rescan()
    {
        Drives.Clear();
        foreach (var drive in System.IO.DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType is not (System.IO.DriveType.Removable or System.IO.DriveType.Fixed)) continue;

                var root = drive.RootDirectory.FullName;
                var containerPath = PhantomVolumeProvisioner.ContainerPathFor(root);
                var hasContainer = System.IO.File.Exists(containerPath);
                var markerPath = System.IO.Path.Combine(root,
                    PhantomVolumeProvisioner.SubdirectoryName,
                    PhantomVolumeProvisioner.MarkerFileName);
                var marker = hasContainer ? PhantomVolumeProvisioner.ReadMarker(markerPath) : null;

                Drives.Add(new PhantomKeyDrive(
                    DriveRoot: root,
                    Label: string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    HasPhantomKeyContainer: hasContainer,
                    ProvisionedByApp: marker?.CreatedByApp));
            }
            catch { /* skip drives we can't enumerate */ }
        }

        SelectedDrive ??= Drives.Count > 0
            ? (System.Linq.Enumerable.FirstOrDefault(Drives, d => d.HasPhantomKeyContainer) ?? Drives[0])
            : null;

        Status = Drives.Count == 0
            ? "No drives detected. Insert your PhantomKey USB and hit Rescan."
            : $"{Drives.Count} drive(s) detected.";
    }

    private async Task UnlockAsync()
    {
        if (SelectedDrive is null) { Status = "Pick a drive first."; return; }
        if (!SelectedDrive.HasPhantomKeyContainer)
        {
            Status = "Selected drive has no PhantomKey partition.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Pin)) { Status = "Enter your PIN."; return; }

        try
        {
            IsBusy = true;
            Status = "Contacting PhantomKey broker…";
            var client = new PhantomKeyClient(BrokerPipeName);
            var result = await client.AuthenticateAsync(DefaultRpId, SelectedDrive.DriveRoot, Pin);
            Status = $"Decision: {result.Decision}   Risk: {result.RiskBand} ({result.RiskScore})";
            Unlocked = string.Equals(result.Decision, "green", System.StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Exception ex)
        {
            Status = $"Unlock failed: {ex.Message}";
            Unlocked = false;
        }
        finally
        {
            Pin = "";
            IsBusy = false;
        }
    }
}
