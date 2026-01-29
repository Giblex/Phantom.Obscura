namespace PhantomVault.UI.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute("VaultUnlockPage", typeof(Views.VaultUnlockPage));
        // Future routes:
        // Routing.RegisterRoute("VaultPage", typeof(Views.VaultPage));
        // Routing.RegisterRoute("CredentialDetailPage", typeof(Views.CredentialDetailPage));
        // Routing.RegisterRoute("SettingsPage", typeof(Views.SettingsPage));
    }
}
