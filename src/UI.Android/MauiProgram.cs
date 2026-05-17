using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhantomVault.Android.Pages;
using PhantomVault.Android.Services;
using PhantomVault.Android.ViewModels;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Platform.Android;

namespace PhantomVault.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        RegisterServices(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<EncryptionService>();
        services.AddSingleton(sp => new ManifestService(sp.GetRequiredService<EncryptionService>()));
        services.AddSingleton<TotpService>();
        services.AddSingleton<PasswordHealthService>();
        services.AddSingleton<ImportExportService>();
        services.AddSingleton<RecoveryCodeService>();
        services.AddSingleton(new IdleLockService(TimeSpan.FromMinutes(5)));

        // Mobile vault service
        services.AddSingleton<MobileVaultService>(sp =>
            new MobileVaultService(sp.GetRequiredService<EncryptionService>()));

        // Android platform services
        services.AddSingleton<AndroidBiometricService>();
        services.AddSingleton<AndroidKeystoreService>();
        services.AddSingleton<AndroidUsbDetector>();
        services.AddSingleton<AndroidAutoTypeService>();
        services.AddSingleton<AndroidActiveWindowDetector>();

        // USB-gate brain: singleton so all VMs share state. The Welcome page
        // subscribes to its events; Setup/Unlock pull the active drive root
        // from it on demand.
        services.AddSingleton<UsbVaultLocator>(sp =>
            new UsbVaultLocator(sp.GetRequiredService<AndroidUsbDetector>()));

        // ViewModels
        services.AddSingleton<VaultViewModel>(sp =>
            new VaultViewModel(sp.GetRequiredService<MobileVaultService>()));

        services.AddTransient<WelcomeViewModel>(sp => new WelcomeViewModel(
            sp.GetRequiredService<UsbVaultLocator>()));

        services.AddTransient<UnlockViewModel>(sp => new UnlockViewModel(
            sp.GetRequiredService<MobileVaultService>(),
            sp.GetRequiredService<UsbVaultLocator>(),
            sp.GetRequiredService<AndroidBiometricService>()));

        services.AddTransient<CredentialDetailViewModel>();
        services.AddTransient<AddEditCredentialViewModel>();
        services.AddTransient<SetupWizardViewModel>(sp => new SetupWizardViewModel(
            sp.GetRequiredService<MobileVaultService>(),
            sp.GetRequiredService<UsbVaultLocator>()));
        services.AddTransient<PasswordGeneratorViewModel>();

        services.AddTransient<PasswordHealthViewModel>(sp => new PasswordHealthViewModel(
            sp.GetRequiredService<PasswordHealthService>(),
            sp.GetRequiredService<VaultViewModel>()));

        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IdleLockService>()));

        services.AddTransient<ImportExportViewModel>(sp => new ImportExportViewModel(
            sp.GetRequiredService<ImportExportService>(),
            sp.GetRequiredService<VaultViewModel>()));

        services.AddTransient<RecoveryViewModel>(sp => new RecoveryViewModel(
            sp.GetRequiredService<RecoveryCodeService>()));

        services.AddTransient<SecuritySettingsViewModel>(sp => new SecuritySettingsViewModel(
            sp.GetRequiredService<AndroidBiometricService>(),
            sp.GetRequiredService<AndroidKeystoreService>()));

        services.AddTransient<AutofillSettingsViewModel>();

        // New feature ViewModels
        services.AddTransient<TrashViewModel>(sp => new TrashViewModel(sp.GetRequiredService<VaultViewModel>()));
        services.AddTransient<CategoryViewModel>(sp => new CategoryViewModel(sp.GetRequiredService<VaultViewModel>()));
        services.AddTransient<DuplicateScanViewModel>(sp => new DuplicateScanViewModel(sp.GetRequiredService<VaultViewModel>()));
        services.AddTransient<TotpScannerViewModel>(sp => new TotpScannerViewModel(
            sp.GetRequiredService<VaultViewModel>(),
            sp.GetRequiredService<TotpService>()));
        services.AddTransient<PasskeyManagementViewModel>(sp => new PasskeyManagementViewModel(sp.GetRequiredService<VaultViewModel>()));

        // Pages
        services.AddTransient<WelcomePage>(sp => new WelcomePage(
            sp.GetRequiredService<WelcomeViewModel>()));

        services.AddTransient<UnlockPage>(sp => new UnlockPage(
            sp.GetRequiredService<UnlockViewModel>(),
            sp.GetRequiredService<VaultViewModel>()));

        services.AddTransient<VaultPage>();
        services.AddTransient<CredentialDetailPage>();

        services.AddTransient<AddEditCredentialPage>(sp => new AddEditCredentialPage(
            sp.GetRequiredService<AddEditCredentialViewModel>(),
            sp.GetRequiredService<VaultViewModel>()));

        services.AddTransient<SetupWizardPage>(sp => new SetupWizardPage(
            sp.GetRequiredService<SetupWizardViewModel>(),
            sp.GetRequiredService<VaultViewModel>(),
            sp.GetRequiredService<MobileVaultService>()));

        services.AddTransient<PasswordGeneratorPage>();
        services.AddTransient<PasswordHealthPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<ImportExportPage>();
        services.AddTransient<RecoveryPage>();
        services.AddTransient<SecuritySettingsPage>();
        services.AddTransient<AutofillSettingsPage>();

        // New feature pages
        services.AddTransient<TrashPage>();
        services.AddTransient<CategoryPage>();
        services.AddTransient<DuplicateScanPage>();
        services.AddTransient<TotpScannerPage>();
        services.AddTransient<PasskeyManagementPage>();

        // ── New services ─────────────────────────────────────────────────────
        services.AddSingleton<MobileBackupService>(sp =>
            new MobileBackupService(sp.GetRequiredService<MobileVaultService>()));
        services.AddSingleton<SharingService>();

        // ── New ViewModels ────────────────────────────────────────────────────
        services.AddTransient<DashboardViewModel>(sp =>
            new DashboardViewModel(sp.GetRequiredService<VaultViewModel>()));
        services.AddTransient<VaultSettingsViewModel>(sp =>
            new VaultSettingsViewModel(sp.GetRequiredService<VaultViewModel>(), sp.GetRequiredService<MobileVaultService>()));
        services.AddTransient<TotpSettingsViewModel>(sp =>
            new TotpSettingsViewModel(sp.GetRequiredService<TotpService>()));
        services.AddTransient<BackupSnapshotsViewModel>(sp =>
            new BackupSnapshotsViewModel(sp.GetRequiredService<MobileBackupService>(), sp.GetRequiredService<VaultViewModel>()));
        services.AddTransient<SecurityDashboardViewModel>(sp =>
            new SecurityDashboardViewModel(sp.GetRequiredService<VaultViewModel>(), sp.GetRequiredService<PasswordHealthService>()));
        services.AddTransient<ShareViewModel>(sp =>
            new ShareViewModel(sp.GetRequiredService<VaultViewModel>(), sp.GetRequiredService<SharingService>()));
        services.AddSingleton<ThemeSettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<AccessibilitySettingsViewModel>();

        // ── New Pages ─────────────────────────────────────────────────────────
        services.AddTransient<DashboardPage>();
        services.AddTransient<VaultSettingsPage>();
        services.AddTransient<TotpSettingsPage>();
        services.AddTransient<BackupSnapshotsPage>();
        services.AddTransient<SecurityDashboardPage>();
        services.AddTransient<SharePage>();
        services.AddTransient<ThemeSettingsPage>();
        services.AddTransient<AboutPage>();
        services.AddTransient<AccessibilitySettingsPage>();
    }
}