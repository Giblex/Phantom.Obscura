using Avalonia.Controls;
using PhantomVault.UI.ViewModels;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Views
{
    public partial class EditPasswordWindow : ThemeAwareWindow
    {
        public EditPasswordWindow()
        {
            InitializeComponent();
        }

        public static void ShowDialog(ThemeAwareWindow? owner, Credential credential, CredentialViewModel? credentialVM, VaultViewModel vaultViewModel)
        {
            var window = new EditPasswordWindow();
            var viewModel = new EditPasswordViewModel(credential, credentialVM, vaultViewModel);
            
            viewModel.RequestClose += (saved) =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            };

            window.DataContext = viewModel;
            window.Show();
        }
    }
}

