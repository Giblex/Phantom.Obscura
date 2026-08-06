using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class LogViewerWindow : ThemeAwareWindow
    {
        public LogViewerWindow()
        {
            InitializeComponent();
            DataContext = new LogViewerViewModel();
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

