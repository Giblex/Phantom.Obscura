using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{

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

