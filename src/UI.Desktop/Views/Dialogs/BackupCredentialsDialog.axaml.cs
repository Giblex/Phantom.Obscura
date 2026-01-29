using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class BackupCredentialsDialog : Window
    {
        public record Result(string? Passphrase, string? KeyfilePath, string Pin);

        public BackupCredentialsDialog() => InitializeComponent();

        public static async Task<Result?> PromptAsync(Window? owner)
        {
            var dialog = new BackupCredentialsDialog();
            var pinBox = dialog.FindControl<TextBox>("PinBox")!;
            var passBox = dialog.FindControl<TextBox>("PassphraseBox")!;
            var keyfileBox = dialog.FindControl<TextBox>("KeyfileBox")!;
            var browseButton = dialog.FindControl<Button>("BrowseButton")!;
            var okButton = dialog.FindControl<Button>("OkButton")!;
            var cancelButton = dialog.FindControl<Button>("CancelButton")!;

            Result? result = null;

            okButton.Click += (_, __) =>
            {
                var pin = pinBox?.Text ?? string.Empty;
                var pass = passBox?.Text;
                var keyfile = keyfileBox?.Text;

                if (string.IsNullOrWhiteSpace(pin))
                {
                    pinBox?.Focus();
                    return;
                }

                result = new Result(pass, keyfile, pin);
                dialog.Close();
            };

            cancelButton.Click += (_, __) =>
            {
                result = null;
                dialog.Close();
            };

            browseButton.Click += async (_, __) =>
            {
                var top = TopLevel.GetTopLevel(dialog);
                if (top?.StorageProvider == null) return;
                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select keyfile",
                    AllowMultiple = false
                });
                if (files != null && files.Count > 0)
                {
                    keyfileBox!.Text = files[0].Path.LocalPath;
                }
            };

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
