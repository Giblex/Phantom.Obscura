using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Serilog;

namespace PhantomVault.UI.Views.Settings
{
    public partial class AutoFillSettingsView : UserControl
    {
        public AutoFillSettingsView()
        {
            InitializeComponent();
        }

        private async void OpenAppPermissions_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                if (parentWindow != null)
                {
                    var permissionsWindow = new PhantomVault.UI.Views.AppPermissionsWindow();
                    await permissionsWindow.ShowDialog(parentWindow);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open App Permissions window");
                try
                {
                    var parentWindow = this.FindAncestorOfType<Window>();
                    var dialogService = new PhantomVault.UI.Services.DialogService();
                    await dialogService.ShowErrorAsync("Error", $"Could not open app permissions: {ex.Message}", parentWindow);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog for App Permissions");
                }
            }
        }
    }
}
