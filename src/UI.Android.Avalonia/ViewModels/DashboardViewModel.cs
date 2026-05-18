using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _vaultName         = "My Vault";
    [ObservableProperty] private string _statusSubtitle    = "USB-bound vault, this device authorised";
    [ObservableProperty] private string _unlockedBadgeText = "UNLOCKED";
    [ObservableProperty] private int    _totalCount        = 0;
    [ObservableProperty] private int    _categoriesCount   = 6;
    [ObservableProperty] private int    _healthScore       = 0;

    public ObservableCollection<DashboardCategoryTile> Categories { get; } = new()
    {
        new() { Name = "Logins",     Count = 0, Glyph = "🔐", Color = new SolidColorBrush(Color.Parse("#3F5675")) },
        new() { Name = "Cards",      Count = 0, Glyph = "💳", Color = new SolidColorBrush(Color.Parse("#5B4877")) },
        new() { Name = "Identities", Count = 0, Glyph = "🪪", Color = new SolidColorBrush(Color.Parse("#3D6B5F")) },
        new() { Name = "Notes",      Count = 0, Glyph = "📝", Color = new SolidColorBrush(Color.Parse("#7A5A38")) },
        new() { Name = "API Keys",   Count = 0, Glyph = "🔑", Color = new SolidColorBrush(Color.Parse("#7A4A4A")) },
        new() { Name = "Wi-Fi",      Count = 0, Glyph = "📶", Color = new SolidColorBrush(Color.Parse("#3D6473")) },
    };

    public ObservableCollection<DashboardRecentItem> Recent { get; } = new();
}

public sealed partial class DashboardCategoryTile : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int    _count;
    [ObservableProperty] private string _glyph = "";
    [ObservableProperty] private IBrush _color = Brushes.Gray;
}

public sealed partial class DashboardRecentItem : ObservableObject
{
    [ObservableProperty] private string _title    = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _glyph    = "";
    [ObservableProperty] private IBrush _tintColor = Brushes.SteelBlue;
    [ObservableProperty] private string _lastUsed = "";
}
