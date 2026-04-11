using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class SecuritySettingsWindow : ThemeAwareWindow
    {
        public SecuritySettingsWindow()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                DataContext = new SecuritySettingsViewModel();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
