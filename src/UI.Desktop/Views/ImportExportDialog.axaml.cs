using Avalonia.Controls;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class ImportExportDialog : ThemeAwareWindow
    {
        public ImportExportDialog()
        {
            InitializeComponent();
        }

        public ImportExportDialog(ImportExportViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
