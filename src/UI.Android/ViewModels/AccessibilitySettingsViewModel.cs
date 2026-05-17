using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class AccessibilitySettingsViewModel : BaseViewModel
    {
        [ObservableProperty] private double _fontSize = 15;
        [ObservableProperty] private bool _highContrastEnabled;
        [ObservableProperty] private bool _reduceMotionEnabled;
        [ObservableProperty] private bool _largeTouchTargetsEnabled;
        [ObservableProperty] private string _previewText = "The quick brown fox";

        partial void OnFontSizeChanged(double value) => PreviewText = "The quick brown fox";

        [RelayCommand]
        private void Save()
        {
            Microsoft.Maui.Storage.Preferences.Set("accessibility_font_size", (float)FontSize);
            Microsoft.Maui.Storage.Preferences.Set("accessibility_high_contrast", HighContrastEnabled);
            Microsoft.Maui.Storage.Preferences.Set("accessibility_reduce_motion", ReduceMotionEnabled);
            Microsoft.Maui.Storage.Preferences.Set("accessibility_large_targets", LargeTouchTargetsEnabled);
            StatusMessage = "Accessibility settings saved.";
        }

        public void Load()
        {
            FontSize = Microsoft.Maui.Storage.Preferences.Get("accessibility_font_size", 15f);
            HighContrastEnabled = Microsoft.Maui.Storage.Preferences.Get("accessibility_high_contrast", false);
            ReduceMotionEnabled = Microsoft.Maui.Storage.Preferences.Get("accessibility_reduce_motion", false);
            LargeTouchTargetsEnabled = Microsoft.Maui.Storage.Preferences.Get("accessibility_large_targets", false);
        }
    }
}
