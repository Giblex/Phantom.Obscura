using Avalonia;
using Avalonia.Controls;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class AccessibilitySettingsWindow : ThemeAwareWindow
    {
        public AccessibilitySettingsWindow()
        {
            InitializeComponent();
            DataContext = new AccessibilitySettingsViewModel();
        }

        public void SetOwnerWindow(Window owner)
        {

        }
    }
}

