using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Controls
{

    public partial class LoadingSpinner : UserControl
    {
        public LoadingSpinner()
        {
            InitializeComponent();
            UpdateForAccessibility();

            Services.AccessibilityService.Instance.SettingsChanged += (s, e) => UpdateForAccessibility();
        }

        private void UpdateForAccessibility()
        {
            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;

            var animatedSpinner = this.FindControl<Viewbox>("AnimatedSpinner");
            var staticSpinner = this.FindControl<Viewbox>("StaticSpinner");

            if (animatedSpinner != null)
                animatedSpinner.IsVisible = !reduceMotion;

            if (staticSpinner != null)
                staticSpinner.IsVisible = reduceMotion;
        }
    }
}

