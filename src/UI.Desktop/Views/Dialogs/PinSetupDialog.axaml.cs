using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Dialogs;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class PinSetupDialog : Window
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
