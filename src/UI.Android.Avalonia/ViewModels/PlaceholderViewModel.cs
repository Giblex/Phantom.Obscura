using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class PlaceholderViewModel : ObservableObject
{
    [ObservableProperty] private string _title          = "Coming soon";
    [ObservableProperty] private string _sourceViewName = "";
    [ObservableProperty] private string _description    = "";
    [ObservableProperty] private string _portStatus     = "";
}
