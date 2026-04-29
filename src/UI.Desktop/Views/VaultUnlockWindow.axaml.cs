using Avalonia.Controls;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class VaultUnlockWindow : ThemeAwareWindow
    {
        public VaultUnlockWindow()
        {
            ThemeScope.SetIsThemed(this, false);
            InitializeComponent();
        }

        public VaultUnlockWindow(VaultUnlockViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.SetOwnerWindow(this);

            // Start vault unlock process when window opens
            // (Will auto-unlock with keyfile if available, otherwise prompt for password)
            Opened += async (s, e) =>
            {
                await viewModel.UnlockVaultAsync();
            };
        }
    }
}
