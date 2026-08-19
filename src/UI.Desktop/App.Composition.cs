using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Options;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.Core.Services.DomainKeys;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.Core.Services.Platform.Windows;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.AutoFill;
using PhantomVault.UI.Services.TrayBackground;
using PhantomVault.UI.ViewModels;
using GiblexVault.Security.ZK.Models;

namespace PhantomVault.UI
{
    // Composition root for App. Behaviour-identical to the original inline registration
    // block that lived in App.axaml.cs; extracted as a partial-class file so the lifecycle
    // logic (OnFrameworkInitializationCompleted, shutdown, theme, etc.) stays small.
    public sealed partial class App
    {
        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(new VaultOptions());

            services.AddSingleton(new EngineOptions(EncryptionProfile.Advanced));
            services.AddSingleton<IZkVaultService, ZkVaultService>();

            services.AddSingleton<EncryptionService>();

            services.AddSingleton<IHybridEncryptionService, HybridEncryptionService>();

            services.AddSingleton<BackupService>(sp => new BackupService(
                sp.GetRequiredService<EncryptionService>(),
                sp.GetRequiredService<PhantomContainerService>()));
            services.AddSingleton<ThemeManagerService>();
            services.AddSingleton<ShadowTrashService>();

            services.AddSingleton<MigrationService>();

            services.AddSingleton<LayeredEncryptionService>();
            services.AddSingleton<KeyfileGeneratorService>();
            services.AddSingleton<KeePassImportService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<AuditService>();
            services.AddSingleton<PasskeyService>();
            services.AddSingleton<IPasskeyService>(sp => sp.GetRequiredService<PasskeyService>());
            services.AddSingleton<SuiteWorkspaceService>();
            services.AddSingleton<IntegratedAttestorService>();
            services.AddSingleton<AttestorCredentialBrokerClient>();
            services.AddSingleton<IntegratedRecoveryService>();
            services.AddSingleton<ScopedRecoveryService>(sp => new ScopedRecoveryService(
                sp.GetRequiredService<EncryptionService>()));
            services.AddSingleton<RecoveryStoreFactory>(sp => new RecoveryStoreFactory(
                sp.GetRequiredService<ScopedRecoveryService>()));
            services.AddSingleton<RecoveryCodeService>();
            services.AddSingleton<KeyfileRecoveryBundleService>(sp => new KeyfileRecoveryBundleService(
                sp.GetRequiredService<RecoveryStoreFactory>(),
                sp.GetRequiredService<RecoveryCodeService>()));
            services.AddSingleton<PhantomContainerService>();
            services.AddSingleton<ManifestService>(sp => new ManifestService(
                sp.GetRequiredService<EncryptionService>(),
                sp.GetRequiredService<PhantomContainerService>()));
            services.AddSingleton<UsbArtifactProtectionService>();
            services.AddSingleton<RecoveryVaultPathResolver>();
            services.AddSingleton<RecoverySuiteBootstrapService>();
            services.AddSingleton<ObscuraCredentialIndexService>();
            services.AddSingleton<BlackSecureRawVolumeService>();
            services.AddSingleton<ObscuraVolumeService>();
            services.AddSingleton<VaultTierMigrationService>();
            services.AddSingleton<UsbDetector>();
            services.AddSingleton<IUsbDetector>(sp => sp.GetRequiredService<UsbDetector>());
            services.AddSingleton<UsbBindingService>();
            services.AddSingleton<SecureBackupManager>();
            services.AddSingleton<TotpService>();
            services.AddSingleton<VaultService>();
            services.AddSingleton<VaultLockDurationService>();

            services.AddSingleton<PhantomVault.UI.Services.SettingsDraftTracker>();
            services.AddSingleton<SecureTrashService>();
            // Idle window comes from the user's auto-lock setting, re-read on every reset.
            // This previously hardcoded 5 minutes, which overrode the setting entirely —
            // a user who chose 60 minutes still had their master key wiped after 5.
            // IdleTimeoutMinutes <= 0 means auto-lock is off.
            services.AddSingleton<IdleLockService>(provider => new IdleLockService(
                () => TimeSpan.FromMinutes(Math.Max(0, SettingsService.Load().IdleTimeoutMinutes))));

            // Keyless suite-session coordinator: reports this app's unlock presence to
            // the suite and honours suite-wide lock without ever sharing key material.
            services.AddSingleton<Phantom.Suite.Session.ISuiteSessionCoordinator>(
                _ => new Phantom.Suite.Session.SuiteSessionCoordinator(
                    Phantom.Suite.Session.SuiteSessionConstants.AppIdObscura));

            services.AddSingleton<YubiKeyService>();
            services.AddSingleton<SharingService>();
            services.AddSingleton<PasswordHealthService>();
            services.AddSingleton<IntrusionService>();

            services.AddSingleton<IRuntimeThemeService, RuntimeThemeService>();

            services.AddSingleton<CrossAppSyncService>();

            services.AddSingleton(PhantomVault.UI.Desktop.Services.ToastNotificationManager.Instance);

            // Internet gateway — every outbound HTTP call goes through this. Off by default.
            services.AddSingleton<PhantomVault.Core.Services.Network.InternetAccessAuditLog>();
            services.AddSingleton<PhantomVault.Core.Services.Network.IInternetConsentProvider>(sp =>
                new PhantomVault.UI.Services.DialogConsentProvider(sp.GetRequiredService<DialogService>()));
            services.AddSingleton<PhantomVault.Core.Services.Network.IInternetGateway>(sp =>
                new PhantomVault.Core.Services.Network.InternetGateway(
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.IInternetConsentProvider>(),
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.InternetAccessAuditLog>()));
            services.AddSingleton<HaveIBeenPwnedService>(sp =>
                new HaveIBeenPwnedService(
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.IInternetGateway>()));
            services.AddTransient<SecureIconDownloaderService>(sp =>
                new SecureIconDownloaderService(
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.IInternetGateway>()));

            // Update pipeline — verifier fails closed while the embedded Ed25519 key
            // remains the all-zero placeholder; UpdateGatewayPolicy also pins the
            // all-zero SPKI sentinel until the real update host is provisioned.
            services.AddSingleton<PhantomVault.Core.Services.Update.IUpdateVerifier>(_ =>
                new PhantomVault.Core.Services.Update.UpdateVerifier());
            services.AddSingleton<PhantomVault.Core.Services.Update.UpdateChannelSettings>(_ =>
            {
                // Load the user's preference (off by default) so the running service
                // honours the toggle without an app restart after Settings → Updates.
                var us = PhantomVault.UI.Services.SettingsService.Load();
                return new PhantomVault.Core.Services.Update.UpdateChannelSettings
                {
                    AutoCheckEnabled = us.UpdateAutoCheckEnabled,
                };
            });
            services.AddSingleton<PhantomVault.Core.Services.Update.IUpdateService>(sp =>
                new PhantomVault.Core.Services.Update.UpdateService(
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.IInternetGateway>(),
                    sp.GetRequiredService<PhantomVault.Core.Services.Update.IUpdateVerifier>(),
                    sp.GetRequiredService<PhantomVault.Core.Services.Update.UpdateChannelSettings>()));

            // Premium licensing / entitlements — fails closed to Free while the
            // embedded license key remains the all-zero placeholder. The dev
            // authority (env-gated PHANTOM_DEV_LICENSING=1, off by default) can
            // seed a trusting verifier so the purchase/unlock flow is testable.
            services.AddSingleton<PhantomVault.UI.Services.Licensing.DevLicenseAuthority>();
            services.AddSingleton<PhantomVault.Core.Services.Licensing.ILicenseVerifier>(sp =>
            {
                var dev = sp.GetRequiredService<PhantomVault.UI.Services.Licensing.DevLicenseAuthority>();
                var devKey = dev.PublicKey;
                return devKey is not null
                    ? new PhantomVault.Core.Services.Licensing.LicenseVerifier(devKey)
                    : new PhantomVault.Core.Services.Licensing.LicenseVerifier();
            });
            services.AddSingleton<PhantomVault.Core.Services.Licensing.IMonotonicTimeGuard>(sp =>
            {
                var stateDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PhantomVault", "Licensing");
                Directory.CreateDirectory(stateDir);
                return new PhantomVault.Core.Services.Licensing.MonotonicTimeGuard(
                    Path.Combine(stateDir, "clock.watermark"));
            });
            services.AddSingleton<PhantomVault.UI.Services.Entitlements.IEntitlementService,
                PhantomVault.UI.Services.Entitlements.EntitlementService>();
            // Real payment backend (Stripe hosted Checkout -> signed token) in normal
            // builds. When the env-gated dev authority is on, keep the stub so the
            // flow stays testable offline with locally-signed dev tokens.
            services.AddSingleton<PhantomVault.UI.Services.Licensing.ILicensingClient>(sp =>
            {
                if (PhantomVault.UI.Services.Licensing.DevLicenseAuthority.IsEnabled)
                {
                    var dev = sp.GetRequiredService<PhantomVault.UI.Services.Licensing.DevLicenseAuthority>();
                    return new PhantomVault.UI.Services.Licensing.StubLicensingClient(dev);
                }
                return new PhantomVault.UI.Services.Licensing.StripeLicensingClient(
                    sp.GetRequiredService<PhantomVault.Core.Services.Network.IInternetGateway>());
            });

            services.AddSingleton<IAuthController, AuthController>();
            services.AddSingleton<IVaultController, VaultController>();
            services.AddSingleton<ISystemSecurityController, SystemSecurityController>();
            services.AddSingleton<IDeviceFingerprintProvider, DeviceFingerprintProvider>();

            // Public-key device identity + QR pairing (manifest-centric multi-device).
            services.AddSingleton<PhantomVault.Core.Services.Security.IDeviceIdentityService,
                PhantomVault.Core.Services.Security.DeviceIdentityService>();
            services.AddSingleton<PhantomVault.Core.Services.Security.IDeviceEnrollmentService,
                PhantomVault.Core.Services.Security.DeviceEnrollmentService>();
            services.AddSingleton<IClipboardGuard, ClipboardGuard>();
            services.AddSingleton<IExportGuard, ExportGuard>();
            services.AddSingleton<RekeyService>();
            services.AddSingleton<IReadOnlyList<DefenceRule>>(provider => CreateDefaultDefenceRules());
            services.AddSingleton<IDefenceEngine, DefenceEngine>();
            services.AddSingleton<PhantomVault.UI.Services.Security.IntegrityWatchdogStatusService>();
            services.AddSingleton<IDefenceSettingsService, DefenceSettingsService>();

            services.AddSingleton<IconManager>(provider =>
            {
                var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Visuals");
                return new IconManager(iconsDir);
            });

            services.AddSingleton<PhantomVault.Core.Services.Platform.Windows.WindowsActiveWindowDetector>();
            services.AddSingleton<PhantomVault.Core.Services.Platform.IActiveWindowDetector>(sp =>
                sp.GetRequiredService<PhantomVault.Core.Services.Platform.Windows.WindowsActiveWindowDetector>());
            services.AddSingleton<PhantomVault.Core.Services.Platform.IAutoTypeService, PhantomVault.Core.Services.Platform.Windows.WindowsAutoTypeService>();

            services.AddSingleton<PhantomVault.Core.Services.AutoInject.ICredentialMatchingEngine, PhantomVault.Core.Services.AutoInject.CredentialMatchingEngine>();
            services.AddSingleton<PhantomVault.Core.Services.AutoInject.IAutoInjectPolicyEngine>(sp =>
            {
                var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault", "Policies");
                Directory.CreateDirectory(dataDirectory);
                return new PhantomVault.Core.Services.AutoInject.AutoInjectPolicyEngine(dataDirectory);
            });

            services.AddSingleton<PhantomVault.Core.Services.AutoInject.IUsbAutoInjectService, PhantomVault.Core.Services.AutoInject.UsbAutoInjectService>();

            services.AddSingleton<VaultAutofillContext>();
            services.AddSingleton<IAutofillVaultContext>(sp => sp.GetRequiredService<VaultAutofillContext>());

            services.AddSingleton<PhantomVault.UI.Services.Sync.TotpSyncVaultContext>();
            services.AddSingleton<PhantomVault.UI.Services.Sync.ITotpSyncVaultContext>(sp => sp.GetRequiredService<PhantomVault.UI.Services.Sync.TotpSyncVaultContext>());
            services.AddSingleton<PhantomVault.UI.Services.Sync.ITotpSyncPipeServer, PhantomVault.UI.Services.Sync.TotpSyncPipeServer>();
            services.AddSingleton<PhantomVault.UI.Services.Sync.TotpSyncPipeClient>();

            services.AddSingleton<PhantomVault.UI.Services.RecoveryActivationPolicyStore>();
            services.AddSingleton<PhantomVault.UI.Services.RecoveryActivationSessionState>();
            services.AddTransient<PhantomVault.UI.ViewModels.Settings.RecoveryActivationSettingsViewModel>(sp =>
                new PhantomVault.UI.ViewModels.Settings.RecoveryActivationSettingsViewModel(
                    sp.GetRequiredService<PhantomVault.UI.Services.RecoveryActivationPolicyStore>(),
                    sp.GetService<PhantomVault.UI.Services.RecoveryActivationSessionState>()));

            services.AddSingleton<WindowsNativeLoginDetector>();

            services.AddSingleton<ITotpFieldPoller>(sp => new TotpFieldPoller(
                sp.GetRequiredService<WindowsNativeLoginDetector>(),
                new PhantomVault.Core.Services.Autofill.FormFieldDetector()));

            services.AddSingleton<IAutoFillOrchestrator, AutoFillOrchestrator>();

            services.AddSingleton<INativeHostPipeServer, NativeHostPipeServer>();

            services.AddSingleton<ITrayBackgroundService, TrayBackgroundService>();

            // Privileged helper (Windows service broker). The non-elevated UI forwards
            // the few admin-only volume/write-protection primitives to the elevated
            // helper over a named pipe, so the app ships asInvoker with no per-launch UAC.
            services.AddSingleton<PhantomVault.UI.Services.Privileged.BrokerServiceController>();
            services.AddSingleton<PhantomVault.UI.Services.Privileged.NamedPipeBrokerClient>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<ProvisionViewModel>();
            services.AddTransient<VaultViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SecuritySettingsViewModel>();
            services.AddTransient<AutoFillSettingsViewModel>(sp =>
                new AutoFillSettingsViewModel(sp.GetService<ITrayBackgroundService>()));
            services.AddTransient<AddPasswordViewModel>();
            services.AddTransient<UsbSetupViewModel>();
            services.AddTransient<FrontPageViewModel>();
            services.AddTransient<PasswordHealthViewModel>();
            services.AddTransient<ShareViewModel>();
            services.AddTransient<WelcomePageViewModel>();
            services.AddTransient<TotpSettingsViewModel>();
            services.AddTransient<WindowsHelloSettingsViewModel>();
            services.AddTransient<PasskeySettingsViewModel>();
        }

        private static List<DefenceRule> CreateDefaultDefenceRules()
        {
            return new List<DefenceRule>
            {
                new DefenceRule(
                    id: "failed-login-warning",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay },
                    cooldown: TimeSpan.FromSeconds(30)
                ),
                new DefenceRule(
                    id: "failed-login-critical",
                    triggerType: ThreatType.FailedLoginBurst,
                    minLevel: ThreatLevel.Critical,
                    actions: new[] { DefenceActionType.TempLockout, DefenceActionType.RequirePhantomKey },
                    cooldown: TimeSpan.FromMinutes(5)
                ),

                new DefenceRule(
                    id: "new-device",
                    triggerType: ThreatType.NewDeviceFingerprint,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.RequirePhantomKey, DefenceActionType.AddDelay },
                    cooldown: TimeSpan.FromMinutes(1)
                ),

                new DefenceRule(
                    id: "integrity-critical",
                    triggerType: ThreatType.IntegrityMismatch,
                    minLevel: ThreatLevel.Critical,
                    actions: new[] { DefenceActionType.EnterReadOnlyMode, DefenceActionType.ScrubShortLivedData },
                    cooldown: null
                ),

                new DefenceRule(
                    id: "excessive-exports",
                    triggerType: ThreatType.ExcessiveExports,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay, DefenceActionType.EnterReadOnlyMode },
                    cooldown: TimeSpan.FromMinutes(10)
                ),

                new DefenceRule(
                    id: "entry-flood",
                    triggerType: ThreatType.HighRiskEntryFlood,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.EnterReadOnlyMode, DefenceActionType.TempLockout },
                    cooldown: TimeSpan.FromMinutes(5)
                ),

                new DefenceRule(
                    id: "behavior-deviation",
                    triggerType: ThreatType.BehaviourDeviation,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay, DefenceActionType.ScrubShortLivedData },
                    cooldown: TimeSpan.FromMinutes(2)
                ),

                new DefenceRule(
                    id: "physical-removal",
                    triggerType: ThreatType.PhysicalRemoval,
                    minLevel: ThreatLevel.Critical,
                    actions: new[] { DefenceActionType.ScrubShortLivedData, DefenceActionType.TempLockout },
                    cooldown: null
                )
            };
        }
    }
}
