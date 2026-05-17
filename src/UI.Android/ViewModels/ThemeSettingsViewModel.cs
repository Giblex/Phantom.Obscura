using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class ThemeSettingsViewModel : BaseViewModel
    {
        [ObservableProperty] private bool _darkModeEnabled = true;
        [ObservableProperty] private bool _useSystemTheme;
        [ObservableProperty] private string _selectedAccent = "purple";
        [ObservableProperty] private string _selectedAccentName = "Purple";

        [RelayCommand]
        private void SetAccent(string? accent)
        {
            if (accent is null) return;
            SelectedAccent = accent;
            SelectedAccentName = accent switch
            {
                "purple" => "Purple",
                "blue"   => "Blue",
                "green"  => "Green",
                "red"    => "Crimson",
                "gold"   => "Gold",
                _        => accent
            };
        }

        [RelayCommand]
        private void ApplyTheme()
        {
            if (UseSystemTheme)
                Application.Current!.UserAppTheme = AppTheme.Unspecified;
            else
                Application.Current!.UserAppTheme = DarkModeEnabled ? AppTheme.Dark : AppTheme.Light;

            StatusMessage = "Theme applied.";
        }
    }
}
