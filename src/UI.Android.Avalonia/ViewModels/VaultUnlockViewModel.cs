using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class VaultUnlockViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private string _status = "Deriving key from master password…";
    [ObservableProperty] private int _progressPercent = 42;
}

