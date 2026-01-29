using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class ThemeSettingsView : UserControl
    {
        public ThemeSettingsView()
        {
            InitializeComponent();
            DataContext = new ThemeSettingsViewModel();
        }
    }
}
