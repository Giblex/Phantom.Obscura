using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isNewDeviceProtectionEnabled = true;
    [ObservableProperty] private bool _isIntegritySafeModeEnabled = true;
    [ObservableProperty] private bool _isClipboardGuardEnabled = true;
    [ObservableProperty] private bool _isExportGuardEnabled = true;
    [ObservableProperty] private bool _isBehaviourDeviationProtectionEnabled = false;
}

