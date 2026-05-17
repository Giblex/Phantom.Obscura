using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Mirrors the desktop SettingsWindow's Defence-tab bindings so the on-screen
/// checkboxes carry state. Persistence wiring lands in a later phase alongside
/// the cross-platform DefenceRule infrastructure.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isNewDeviceProtectionEnabled = true;
    [ObservableProperty] private bool _isIntegritySafeModeEnabled = true;
    [ObservableProperty] private bool _isClipboardGuardEnabled = true;
    [ObservableProperty] private bool _isExportGuardEnabled = true;
    [ObservableProperty] private bool _isBehaviourDeviationProtectionEnabled = false;
}
