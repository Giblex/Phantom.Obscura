using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    public partial class TotpScannerDialog : ThemeAwareWindow
    {
        public TotpScannerDialog()
        {
            InitializeComponent();
        }

        public TotpScannerDialog(TotpScannerViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.CloseRequested += (s, result) => Close(result);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
