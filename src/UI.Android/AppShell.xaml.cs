using PhantomVault.Android.Pages;

namespace PhantomVault.Android;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // ── Modal / push routes ───────────────────────────────────────────────
        Routing.RegisterRoute("detail", typeof(CredentialDetailPage));
        Routing.RegisterRoute("edit", typeof(AddEditCredentialPage));
        Routing.RegisterRoute("importexport", typeof(ImportExportPage));
        Routing.RegisterRoute("recovery", typeof(RecoveryPage));
        Routing.RegisterRoute("security", typeof(SecuritySettingsPage));
        Routing.RegisterRoute("autofill", typeof(AutofillSettingsPage));
        Routing.RegisterRoute("password-generator", typeof(PasswordGeneratorPage));
        Routing.RegisterRoute("password-health", typeof(PasswordHealthPage));
        Routing.RegisterRoute("trash", typeof(TrashPage));
        Routing.RegisterRoute("categories", typeof(CategoryPage));
        Routing.RegisterRoute("duplicate-scan", typeof(DuplicateScanPage));
        Routing.RegisterRoute("totp-scanner", typeof(TotpScannerPage));
        Routing.RegisterRoute("passkeys", typeof(PasskeyManagementPage));
        // New routes
        Routing.RegisterRoute("vault-settings", typeof(VaultSettingsPage));
        Routing.RegisterRoute("totp-settings", typeof(TotpSettingsPage));
        Routing.RegisterRoute("backup-snapshots", typeof(BackupSnapshotsPage));
        Routing.RegisterRoute("security-dashboard", typeof(SecurityDashboardPage));
        Routing.RegisterRoute("share", typeof(SharePage));
        Routing.RegisterRoute("theme-settings", typeof(ThemeSettingsPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
        Routing.RegisterRoute("accessibility", typeof(AccessibilitySettingsPage));

        // ── Adaptive layout: enable flyout for tablets ────────────────────────
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Tablet)
        {
            ApplyTabletShell();
        }
    }

    /// <summary>
    /// On tablets, switch to a side-flyout navigation instead of bottom tabs
    /// so users get a permanent master-detail experience on the larger screen.
    /// </summary>
    private void ApplyTabletShell()
    {
        // Enable the side flyout drawer
        FlyoutBehavior = FlyoutBehavior.Locked;

        // Add fly-out menu items pointing to the same registered routes
        Items.Add(new FlyoutItem
        {
            Title = "Vault",
            Route = "vault",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(VaultPage))
                        }
                    }
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Settings",
            Route = "settings",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(SettingsPage))
                        }
                    }
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Security",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(SecuritySettingsPage))
                        }
                    }
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Autofill",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(AutofillSettingsPage))
                        }
                    }
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Import / Export",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(ImportExportPage))
                        }
                    }
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Recovery",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new Tab
                {
                    Items =
                    {
                        new ShellContent
                        {
                            ContentTemplate = new DataTemplate(typeof(RecoveryPage))
                        }
                    }
                }
            }
        });
    }
}
