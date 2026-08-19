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

        private void PreviewAnimation_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var owner = this.FindAncestorOfType<Window>();
                PhantomVault.UI.Views.Autofill.AutofillDemo.ShowDemo(owner);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open AutoFill animation demo");
            }
        }

        private async void ConfigureMobileAutoFill_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var parentWindow = this.FindAncestorOfType<Window>();
                var dialogService = new PhantomVault.UI.Services.DialogService();
                await dialogService.ShowInfoAsync(
                    "Mobile Auto-Fill",
                    "Mobile auto-fill pairing isn't available yet.\n\n" +
                    "This will let you pair the PhantomObscura Android/iOS app so it can " +
                    "auto-fill credentials on your phone. The desktop pairing service that " +
                    "backs this flow is still in development.\n\n" +
                    "Desktop and USB-triggered auto-fill are unaffected.",
                    parentWindow);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Mobile Auto-Fill dialog");
            }
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
                    Log.Warning(ex, "[AutoFillSettings] Failed to open application permissions.");
                    await dialogService.ShowErrorAsync("Error", "Application permissions could not be opened. Open them from Windows Settings and try again.", parentWindow);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog for App Permissions");
                }
            }
        }
    }
}

