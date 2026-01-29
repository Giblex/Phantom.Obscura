using Avalonia.Controls;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class VaultUnlockWindow : Window
    {
        public VaultUnlockWindow()
        {
            InitializeComponent();
        }

        public VaultUnlockWindow(VaultUnlockViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.SetOwnerWindow(this);

            // Automatically start unlock process when window opens
            Opened += async (s, e) =>
            {
                await viewModel.UnlockVaultAsync();
            };
        }
    }
}