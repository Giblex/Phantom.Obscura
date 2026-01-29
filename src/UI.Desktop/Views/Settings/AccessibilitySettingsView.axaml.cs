using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class AccessibilitySettingsView : UserControl
    {
        public AccessibilitySettingsView()
        {
            InitializeComponent();
            DataContext = new AccessibilitySettingsViewModel();
        }
    }
}
