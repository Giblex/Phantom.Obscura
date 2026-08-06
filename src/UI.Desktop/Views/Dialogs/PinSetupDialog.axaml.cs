using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Dialogs;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class PinSetupDialog : ThemeAwareWindow
    {
        public PinSetupDialog()
        {
            InitializeComponent();
            DataContext = new PinSetupDialogViewModel(this);
        }

        public PinSetupDialog(string? manifestPath)
        {
            InitializeComponent();
            DataContext = new PinSetupDialogViewModel(this, manifestPath);
        }
    }
}

