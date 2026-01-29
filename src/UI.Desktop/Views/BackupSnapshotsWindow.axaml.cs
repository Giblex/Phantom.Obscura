using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class BackupSnapshotsWindow : Window
    {
        public BackupSnapshotsWindow()
        {
            InitializeComponent();
            DataContext = new BackupSnapshotsViewModel();
        }

        public BackupSnapshotsWindow(string backupLocation)
        {
            InitializeComponent();
            DataContext = new BackupSnapshotsViewModel(backupLocation);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
