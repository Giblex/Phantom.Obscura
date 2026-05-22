using System;
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
            Serilog.Log.Information("[VaultUnlockWindow] before InitializeComponent");
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VaultUnlockWindow] InitializeComponent threw");
                throw;
            }
            Serilog.Log.Information("[VaultUnlockWindow] after InitializeComponent");
        }

        public VaultUnlockWindow(VaultUnlockViewModel viewModel) : this()
        {
            Serilog.Log.Information("[VaultUnlockWindow] this() returned — setting DataContext");
            DataContext = viewModel;
            Serilog.Log.Information("[VaultUnlockWindow] DataContext set — SetOwnerWindow");
            viewModel.SetOwnerWindow(this);
            Serilog.Log.Information("[VaultUnlockWindow] wiring Opened event");

            // Start vault unlock process when window opens
            // (Will auto-unlock with keyfile if available, otherwise prompt for password)
            Opened += async (s, e) =>
            {
                try
                {
                    Serilog.Log.Information("[VaultUnlockWindow] Opened — starting UnlockVaultAsync");
                    await viewModel.UnlockVaultAsync();
                    Serilog.Log.Information("[VaultUnlockWindow] UnlockVaultAsync returned");
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "[VaultUnlockWindow] UnlockVaultAsync threw unhandled");
                }
            };
            Serilog.Log.Information("[VaultUnlockWindow] constructor complete");
        }
    }
}
