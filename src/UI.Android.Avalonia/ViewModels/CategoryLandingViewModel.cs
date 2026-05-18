using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class CategoryLandingViewModel : ObservableObject
{
    public ObservableCollection<CategoryTileViewModel> CategoryTiles { get; } = new()
    {
        new() { Name = "Logins",      Count = 0, Icon = "🔐", Color = Brushes.SteelBlue,    FilterType = "logins" },
        new() { Name = "Cards",       Count = 0, Icon = "💳", Color = Brushes.MediumPurple, FilterType = "cards" },
        new() { Name = "Identities",  Count = 0, Icon = "🪪", Color = Brushes.SeaGreen,     FilterType = "identities" },
        new() { Name = "Notes",       Count = 0, Icon = "📝", Color = Brushes.DarkOrange,   FilterType = "notes" },
        new() { Name = "API Keys",    Count = 0, Icon = "🔑", Color = Brushes.IndianRed,    FilterType = "apikeys" },
        new() { Name = "Wi-Fi",       Count = 0, Icon = "📶", Color = Brushes.Teal,         FilterType = "wifi" },
    };
}

public sealed partial class CategoryTileViewModel : ObservableObject
{
    [ObservableProperty] private string  _name      = "";
    [ObservableProperty] private int     _count;
    [ObservableProperty] private string  _icon      = "";
    [ObservableProperty] private IBrush  _color     = Brushes.Gray;
    [ObservableProperty] private string  _filterType= "";
}
