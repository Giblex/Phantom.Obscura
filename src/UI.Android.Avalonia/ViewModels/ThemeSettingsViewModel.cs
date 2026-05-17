using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

/// <summary>Phase 3g stub. Real desktop equivalent ties into ThemeManagerService.</summary>
public sealed partial class ThemeSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private int  _selectedRuntimeThemeIndex = 0;

    public List<string> RuntimeThemeNames { get; } = new()
    {
        "Phantom (default)",
        "Arctic Frost",
        "Charcoal Pastel",
        "Classic Dark",
        "Classic Light",
        "Cyberpunk",
        "Giblex Dark",
        "Giblex Glass Navy",
        "Giblex Website",
        "Midnight Neon",
        "Modern System",
        "Natural",
        "Proton",
        "Sunset Ember",
    };
}
