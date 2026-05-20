using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class DuplicateScanWindow : Window
    {
        public DuplicateScanWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public DuplicateScanWindow(DuplicateScanViewModel vm) : this()
        {
            DataContext = vm;
            vm.CloseRequested += () => Close();
        }
    }
}
