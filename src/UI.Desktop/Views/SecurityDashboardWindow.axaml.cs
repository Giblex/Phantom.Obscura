using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Comprehensive Security Dashboard window showing detailed security analytics.
    /// Displays security score breakdown, password health metrics, and weak credentials list.
    /// </summary>
    public partial class SecurityDashboardWindow : ThemeAwareWindow
    {
        public SecurityDashboardWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
