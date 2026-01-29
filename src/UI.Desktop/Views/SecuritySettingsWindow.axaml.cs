using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views
{
    public partial class SecuritySettingsWindow : ThemeAwareWindow
    {
        public SecuritySettingsWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}