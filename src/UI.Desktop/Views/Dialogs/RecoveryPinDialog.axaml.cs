using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Dialogs;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class RecoveryPinDialog : ThemeAwareWindow
    {
        public RecoveryPinDialog()
        {
            InitializeComponent();
            DataContext = new RecoveryPinDialogViewModel(this);
        }
    }
}
