using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class FrontPageWindow : Window
    {
        public FrontPageWindow()
        {
            InitializeComponent();
            this.Opened += FrontPageWindow_Opened;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void FrontPageWindow_Opened(object? sender, System.EventArgs e)
        {
            if (DataContext is FrontPageViewModel vm)
            {
                vm.OnContinueRequested += () => Close();
            }
        }
    }
}