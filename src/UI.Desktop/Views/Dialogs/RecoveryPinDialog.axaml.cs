using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Dialogs;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class RecoveryPinDialog : Window
    {
        public RecoveryPinDialog()
        {
            InitializeComponent();
            DataContext = new RecoveryPinDialogViewModel(this);
        }
    }
}
