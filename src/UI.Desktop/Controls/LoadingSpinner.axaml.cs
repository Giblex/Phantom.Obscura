using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Controls
{
    /// <summary>
    /// Modern loading spinner that respects ReduceMotion accessibility setting.
    /// Shows smooth rotating animation in normal mode, gentle pulse in ReduceMotion mode.
    /// </summary>
    public partial class LoadingSpinner : UserControl
    {
        public LoadingSpinner()
        {
            InitializeComponent();
            UpdateForAccessibility();

            // Subscribe to accessibility changes
            Services.AccessibilityService.Instance.SettingsChanged += (s, e) => UpdateForAccessibility();
        }

        private void UpdateForAccessibility()
        {
            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;

            // Show appropriate spinner based on ReduceMotion setting
            var animatedSpinner = this.FindControl<Viewbox>("AnimatedSpinner");
            var staticSpinner = this.FindControl<Viewbox>("StaticSpinner");

            if (animatedSpinner != null)
                animatedSpinner.IsVisible = !reduceMotion;

            if (staticSpinner != null)
                staticSpinner.IsVisible = reduceMotion;
        }
    }
}
