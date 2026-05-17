using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

/// <summary>Phase 3c stub of the desktop VaultUnlockViewModel.</summary>
public sealed partial class VaultUnlockViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private string _status = "Deriving key from master password…";
    [ObservableProperty] private int _progressPercent = 42;
}
