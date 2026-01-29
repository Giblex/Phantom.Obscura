using Avalonia;
using Avalonia.Controls;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class AccessibilitySettingsWindow : Window
    {
        public AccessibilitySettingsWindow()
        {
            InitializeComponent();
            DataContext = new AccessibilitySettingsViewModel();
        }

        /// <summary>
        /// Sets the owner window for modal dialog display.
        /// </summary>
        public void SetOwnerWindow(Window owner)
        {
            // Store owner reference if needed for child dialogs
        }
    }
}
