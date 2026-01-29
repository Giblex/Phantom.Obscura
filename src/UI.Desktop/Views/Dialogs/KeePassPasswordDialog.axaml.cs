using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PhantomVault.UI.Views.Dialogs
{
    public partial class KeePassPasswordDialog : Window
    {
        public string? Password { get; private set; }
        public string? KeyfilePath { get; private set; }
        public bool WasConfirmed { get; private set; }

        public KeePassPasswordDialog()
        {
            InitializeComponent();
        }

        public static async Task<(string? password, string? keyfilePath)?> ShowAsync(Window? owner = null)
        {
            var dialog = new KeePassPasswordDialog();

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            if (dialog.WasConfirmed && !string.IsNullOrWhiteSpace(dialog.Password))
            {
                return (dialog.Password, dialog.KeyfilePath);
            }

            return null;
        }

        private async void BrowseKeyfile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Keyfile",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    KeyfilePath = files[0].Path.LocalPath;
                    KeyfileTextBox.Text = System.IO.Path.GetFileName(KeyfilePath);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error selecting keyfile: {ex.Message}";
            }
        }

        private void ShowPasswordCheckBox_Changed(object? sender, RoutedEventArgs e)
        {
            if (ShowPasswordCheckBox.IsChecked == true)
            {
                PasswordTextBox.PasswordChar = '\0'; // Show password
            }
            else
            {
                PasswordTextBox.PasswordChar = '•'; // Hide password
            }
        }

        private void Unlock_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PasswordTextBox.Text))
            {
                StatusTextBlock.Text = "Password is required";
                return;
            }

            Password = PasswordTextBox.Text;
            KeyfilePath = string.IsNullOrWhiteSpace(KeyfileTextBox.Text) ? null : KeyfilePath;
            WasConfirmed = true;
            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            WasConfirmed = false;
            Close();
        }
    }
}
