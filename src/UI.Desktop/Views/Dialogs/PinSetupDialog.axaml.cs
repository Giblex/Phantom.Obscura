using System;
using System.Threading.Tasks;
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

        /// <param name="setPin">Stores the PIN in the unlocked vault's encrypted manifest.</param>
        public PinSetupDialog(Func<string, Task> setPin)
        {
            InitializeComponent();
            DataContext = new PinSetupDialogViewModel(this, setPin);
        }
    }
}
