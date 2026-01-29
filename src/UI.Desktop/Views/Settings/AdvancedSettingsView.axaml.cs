using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class AdvancedSettingsView : UserControl
    {
        public AdvancedSettingsView()
        {
            InitializeComponent();
            DataContext = new AdvancedSettingsViewModel();
        }
    }
}
