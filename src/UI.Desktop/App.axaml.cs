using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Options;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.Core.Services.Platform.Windows;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.AutoFill;
using PhantomVault.UI.Services.TrayBackground;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using GiblexVault.Security.ZK.Models;
using Giblex.Controls;

namespace PhantomVault.UI
{
    /// <summary>
    /// Application entry point. Configures dependency injection and
    /// starts the main window. Avalonia uses application lifetimes to
    /// support desktop and single view scenarios.
    /// </summary>
    public sealed partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        private string _currentTheme = "dark";
        private readonly List<WeakReference<Window>> _themeAwareWindows = new();
        private static readonly Uri ThemeBaseUri = new("avares://PhantomVault.UI/");
        private static readonly Uri LightThemeUri = new("avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Light.axaml");
        private static readonly Uri DarkThemeUri = new("avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Dark.axaml");
        public IReadOnlyList<WeakReference<Window>> ThemeAwareWindows => _themeAwareWindows;

        /// <summary>
        /// Exposes the configured service provider to views. This is used
        /// sparingly to obtain view models for secondary windows. Consider
        /// using an MVVM framework with proper dependency injection in a
        /// larger application.
        /// </summary>
        public ServiceProvider? Services => _serviceProvider;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            LiquidGlassScrollChrome.Register();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Register core services with the DI container. We keep the
            // container simple; in a larger application you may want to
            // separate configuration from code.
            var services = new ServiceCollection();
            services.AddSingleton(new VaultOptions());

            // Register zero-knowledge vault service with Advanced encryption profile
            services.AddSingleton(new EngineOptions(EncryptionProfile.Advanced));
            services.AddSingleton<IZkVaultService, ZkVaultService>();

            services.AddSingleton<EncryptionService>();
            // Additional services for hybrid encryption and audit logging
            // Use the production implementation with ML-KEM-768 (CRYSTALS-Kyber) for post-quantum security
            services.AddSingleton<IHybridEncryptionService, HybridEncryptionService>();
            // Register the backup service which creates and prunes encrypted manifest backups
            services.AddSingleton<BackupService>(sp => new BackupService(
                sp.GetRequiredService<EncryptionService>(),
                sp.GetRequiredService<PhantomContainerService>()));
            services.AddSingleton<ThemeManagerService>();
            services.AddSingleton<ShadowTrashService>();
            // Register migration service for post‑quantum agility
            services.AddSingleton<MigrationService>();
            // Register layered encryption service for defense-in-depth security
            services.AddSingleton<LayeredEncryptionService>();
            services.AddSingleton<KeyfileGeneratorService>();
            services.AddSingleton<KeePassImportService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<AuditService>();
            services.AddSingleton<PasskeyService>();
            services.AddSingleton<IPasskeyService>(sp => sp.GetRequiredService<PasskeyService>());
            services.AddSingleton<SuiteWorkspaceService>();
            services.AddSingleton<IntegratedAttestorService>();
            services.AddSingleton<IntegratedRecoveryService>();
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
            services.AddSingleton<SecureTrashService>();
            services.AddSingleton<IdleLockService>(provider => new IdleLockService(TimeSpan.FromMinutes(5)));
            services.AddSingleton<YubiKeyService>();
            services.AddSingleton<SharingService>();
            services.AddSingleton<PasswordHealthService>();
            services.AddSingleton<IntrusionService>();
            // Register Runtime Theme Service for scoped theme switching
            services.AddSingleton<IRuntimeThemeService, RuntimeThemeService>();
            // Register Cross-App Sync Service for theme sync with PhantomAttestor
            services.AddSingleton<CrossAppSyncService>();

            // NEW: UI Polish Services
            services.AddSingleton<SvgIconService>(sp => new SvgIconService("ZluQ8qGZzB"));
            services.AddSingleton<HaveIBeenPwnedService>();
            services.AddSingleton(PhantomVault.UI.Desktop.Services.ToastNotificationManager.Instance);

            // Register Defence Engine with default rules for automated threat response
            services.AddSingleton<IAuthController, AuthController>();
            services.AddSingleton<IVaultController, VaultController>();
            services.AddSingleton<ISystemSecurityController, SystemSecurityController>();
            services.AddSingleton<IDeviceFingerprintProvider, DeviceFingerprintProvider>();
            services.AddSingleton<IClipboardGuard, ClipboardGuard>();
            services.AddSingleton<IExportGuard, ExportGuard>();
            services.AddSingleton<RekeyService>();
            services.AddSingleton<IReadOnlyList<DefenceRule>>(provider => CreateDefaultDefenceRules());
            services.AddSingleton<IDefenceEngine, DefenceEngine>();
            services.AddSingleton<IDefenceSettingsService, DefenceSettingsService>();

            services.AddSingleton<IconManager>(provider =>
            {
                // Use the Assets\Visuals root as the canonical icons source (IconManager enumerates subfolders)
                var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Visuals");
                return new IconManager(iconsDir);
            });

            // Browser-extension / native-messaging autofill is provided via
            // PhantomVault.UI.Desktop.Services.AutoFill (NativeHostPipeServer
            // + AutoFillOrchestrator), registered below alongside the USB
            // auto-inject services. The older PhantomVault.Core.Services.Autofill
            // infrastructure (ICredentialRepository / INativeMessagingHost /
            // platform IAutofillProvider) was retired in favour of that pipeline
            // and the per-platform AutofillService implementations.

            // USB Auto-Inject Services
            // Note: VaultViewModel is registered as Transient, so we'll need to get it dynamically
            // Platform-specific services (Windows)
            services.AddSingleton<PhantomVault.Core.Services.Platform.Windows.WindowsActiveWindowDetector>();
            services.AddSingleton<PhantomVault.Core.Services.Platform.IActiveWindowDetector>(sp =>
                sp.GetRequiredService<PhantomVault.Core.Services.Platform.Windows.WindowsActiveWindowDetector>());
            services.AddSingleton<PhantomVault.Platform.Services.IActiveWindowDetector>(sp =>
                sp.GetRequiredService<PhantomVault.Core.Services.Platform.Windows.WindowsActiveWindowDetector>());
            services.AddSingleton<PhantomVault.Core.Services.Platform.IAutoTypeService, PhantomVault.Core.Services.Platform.Windows.WindowsAutoTypeService>();

            // Auto-inject core services
            services.AddSingleton<PhantomVault.Core.Services.AutoInject.ICredentialMatchingEngine, PhantomVault.Core.Services.AutoInject.CredentialMatchingEngine>();
            services.AddSingleton<PhantomVault.Core.Services.AutoInject.IAutoInjectPolicyEngine>(sp =>
            {
                var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault", "Policies");
                Directory.CreateDirectory(dataDirectory);
                return new PhantomVault.Core.Services.AutoInject.AutoInjectPolicyEngine(dataDirectory);
            });

            // Main orchestrator - will be initialized with VaultViewModel after it's created
            services.AddSingleton<PhantomVault.Core.Services.AutoInject.IUsbAutoInjectService, PhantomVault.Core.Services.AutoInject.UsbAutoInjectService>();

            // ── AutoFill Mode (USB-triggered) ───────────────────────────────────────
            // Vault context bridge for native messaging host
            services.AddSingleton<VaultAutofillContext>();
            services.AddSingleton<IAutofillVaultContext>(sp => sp.GetRequiredService<VaultAutofillContext>());

            // Windows UI Automation login detector
            services.AddSingleton<WindowsNativeLoginDetector>();

            // TOTP field poller (subscribes to NativeMessagingHostService.FormDetected events)
            services.AddSingleton<ITotpFieldPoller>(sp => new TotpFieldPoller(
                sp.GetRequiredService<WindowsNativeLoginDetector>(),
                new PhantomVault.Core.Services.Autofill.FormFieldDetector()));

            // AutoFill orchestrator state machine
            services.AddSingleton<IAutoFillOrchestrator, AutoFillOrchestrator>();

            // Named-pipe server bridging the desktop app to
            // `PhantomVault.UI.exe --native-messaging` subprocesses spawned by
            // browsers. Started after the service provider is built.
            services.AddSingleton<INativeHostPipeServer, NativeHostPipeServer>();

            // System tray background service — owns TrayIcon and wires USB events
            services.AddSingleton<ITrayBackgroundService, TrayBackgroundService>();
            // ───────────────────────────────────────────────────────────────────────

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

            _serviceProvider = services.BuildServiceProvider();

            // Enable developer mode for PhantomRecovery integration
            // Configurable at runtime via AdvancedSettings > IsRecoveryDeveloperModeEnabled
#if DEBUG
            RecoveryDeveloperMode.IsEnabled = true;
            RecoveryDeveloperMode.Log("Developer mode enabled for PhantomRecovery integration");
#endif

            var persistedSettings = SettingsService.Load();
            PrivacyShield.PrivacyModeEnabled = persistedSettings.PrivacyModeEnabled;
            PrivacyShield.RedactDiagnostics = persistedSettings.RedactDiagnosticLogs;
            PrivacyShield.DebugLoggingEnabled = persistedSettings.EnableDebugLogging;
            // Apply persisted render scale and theme early so windows inherit user preference.
            try
            {
                var themeManager = _serviceProvider.GetRequiredService<ThemeManagerService>();
                var scale = Math.Clamp(persistedSettings.RenderScale, 0.8, 1.5);
                themeManager.SetRenderScale(scale);

                // Apply persisted runtime theme FIRST (before ThemeManagerService loads PhantomTheme)
                var runtimeThemeService = _serviceProvider.GetRequiredService<IRuntimeThemeService>();
                if (!string.IsNullOrEmpty(persistedSettings.SelectedThemeId))
                {
                    runtimeThemeService.Apply(persistedSettings.SelectedThemeId);
                }

                var theme = persistedSettings.EnableHighContrast
                    ? AppTheme.HighContrast
                    : persistedSettings.IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
                themeManager.SetTheme(theme);

                // Apply other theme settings
                themeManager.SetSkin(persistedSettings.ThemeSkin);
                themeManager.SetEffects(persistedSettings.ReduceAnimations, persistedSettings.ReduceTransparency);
            }
            catch
            {
                // best effort; ignore if theme service unavailable during init
            }

            // Start cross-app sync (theme sync with PhantomAttestor)
            try
            {
                var syncService = _serviceProvider.GetRequiredService<CrossAppSyncService>();
                syncService.Start();
            }
            catch
            {
                // best effort
            }
            var secureTrash = _serviceProvider.GetRequiredService<SecureTrashService>();
            secureTrash.ApplyConfiguration(
                persistedSettings.SecureTrashEnabled,
                persistedSettings.SecureTrashAutoPurge,
                persistedSettings.SecureTrashRetentionDays,
                persistedSettings.SecureTrashWipePasses);
            secureTrash.SecurelyPurgeExpired();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Cleanup orphaned temp files from previous sessions
                var zkVaultService = _serviceProvider.GetRequiredService<IZkVaultService>();
                _ = zkVaultService.CleanupOrphanedTempFilesAsync(TimeSpan.FromHours(24));

                // Register shutdown handler to ensure all processes are killed
                desktop.ShutdownRequested += OnShutdownRequested;
                desktop.Exit += OnApplicationExit;

                // Start with WelcomePage for first-time users
                var welcomePage = new WelcomePage
                {
                    DataContext = _serviceProvider.GetRequiredService<WelcomePageViewModel>()
                };
                desktop.MainWindow = welcomePage;

                // ── AutoFill Mode: tray startup & close-to-tray ────────────────────
                if (persistedSettings.AutoFillModeEnabled)
                {
                    try
                    {
                        var trayService = _serviceProvider.GetRequiredService<ITrayBackgroundService>();
                        _ = trayService.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Failed to start AutoFill tray service: {ex.Message}");
                    }
                }

                // Start the native-host pipe server so browser-spawned
                // `--native-messaging` subprocesses can query vault state and
                // credentials. The server is harmless when no vault is unlocked
                // (it answers `locked` to every query).
                try
                {
                    var pipeServer = _serviceProvider.GetRequiredService<INativeHostPipeServer>();
                    pipeServer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to start native-host pipe server: {ex.Message}");
                }

                // When AutoFill Mode is enabled, closing the main window hides it to tray
                // rather than exiting the process so the USB listener stays alive.
                desktop.MainWindow.Closing += (sender, e) =>
                {
                    var settings = SettingsService.Load();
                    if (settings.AutoFillModeEnabled)
                    {
                        e.Cancel = true;
                        ((Window)sender!).Hide();
                    }
                };
                // ───────────────────────────────────────────────────────────────────
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Handles application shutdown request to ensure all processes are properly terminated.
        /// </summary>
        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            try
            {
                Debug.WriteLine("[App] Shutdown requested - cleaning up resources");
#if DEBUG
                Console.WriteLine("[App] Shutdown requested - cleaning up resources");
#endif

                // Dispose service provider to clean up resources
                _serviceProvider?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles application exit to terminate only processes spawned by this application.
        /// Uses SpawnedProcessTracker to avoid killing unrelated spawned instances.
        /// </summary>
        private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            try
            {
                Debug.WriteLine("[App] Application exiting - cleaning up spawned processes");
#if DEBUG
                Console.WriteLine("[App] Application exiting - cleaning up spawned processes");
#endif

                // Only terminate processes that were spawned by this application
                // This prevents killing processes used by other apps or the user
                try
                {
                    SpawnedProcessTracker.Instance.TerminateAllTrackedProcesses();
                    Debug.WriteLine("[App] Spawned processes cleaned up successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error cleaning up spawned processes: {ex.Message}");
                }

                // Force current process to exit
                Environment.Exit(e.ApplicationExitCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during exit: {ex.Message}");
                Environment.Exit(-1);
            }
        }

        /// <summary>
        /// Swap the application's theme between 'light' and 'dark'. This replaces the
        /// StyleInclude with the requested theme resource at runtime.
        /// </summary>
        public void SetTheme(string theme)
        {
            _currentTheme = string.IsNullOrWhiteSpace(theme) ? "dark" : theme.Trim().ToLowerInvariant();
            var themePath = _currentTheme == "light"
                ? "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Light.axaml"
                : "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Dark.axaml";

            TraceStyles("App.SetTheme entry", this.Styles);

            // Remove any existing StyleInclude entries referencing our theme, then add a fresh include
            try
            {
                var toRemove = new System.Collections.Generic.List<StyleInclude>();
                for (int i = 0; i < this.Styles.Count; i++)
                {
                    if (this.Styles[i] is StyleInclude si && si.Source != null && si.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                    {
                        Debug.WriteLine($"[SetTheme] App.Styles removing include {si.Source.OriginalString}");
#if DEBUG
                        Console.WriteLine($"[SetTheme] App.Styles removing include {si.Source.OriginalString}");
#endif
                        toRemove.Add(si);
                    }
                }

                foreach (var rem in toRemove)
                {
                    this.Styles.Remove(rem);
                }

                // Add the new include so Avalonia reloads resources
                var newInclude = new StyleInclude(new Uri("avares://PhantomVault.UI/")) { Source = new Uri(themePath) };
                this.Styles.Add(newInclude);
                Debug.WriteLine($"[SetTheme] App.Styles added include {themePath}");
#if DEBUG
                Console.WriteLine($"[SetTheme] App.Styles added include {themePath}");
#endif
                TraceStyles("App.SetTheme after App.Styles update", this.Styles);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetTheme] Error updating App.Styles: {ex.Message}");
#if DEBUG
                Console.WriteLine($"[SetTheme] Error updating App.Styles: {ex.Message}");
#endif
            }

            // Also set Avalonia's RequestedThemeVariant so built-in controls update appropriately
            try
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = _currentTheme == "light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                }
                // Also update any open windows that include the theme locally (use desktop lifetime Windows collection)
                if (desktop != null)
                {
                    foreach (var win in desktop.Windows)
                    {
                        Debug.WriteLine($"[SetTheme] Processing window: '{win.Title ?? win.GetType().Name}'");
#if DEBUG
                        Console.WriteLine($"[SetTheme] Processing window: '{win.Title ?? win.GetType().Name}'");
#endif
                        // Update styles collection for the window
                        UpdateStylesCollection(win.Styles, themePath);

                        // Also check window resources merged dictionaries (some views merge the theme here)
                        try
                        {
                            if (win.Resources?.MergedDictionaries is { } merged)
                            {
                                for (int mdIdx = 0; mdIdx < merged.Count; mdIdx++)
                                {
                                    var md = merged[mdIdx];
                                    if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                                    {
                                        Debug.WriteLine($"[SetTheme] Window '{win.Title ?? win.GetType().Name}' merged dict[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#if DEBUG
                                        Console.WriteLine($"[SetTheme] Window '{win.Title ?? win.GetType().Name}' merged dict[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#endif
                                        mdsi.Source = new Uri(themePath);
                                    }
                                    else if (md is Avalonia.Styling.Styles nestedStyles)
                                    {
                                        UpdateStylesCollection(nestedStyles, themePath);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SetTheme] Error updating window resources for '{win.Title ?? win.GetType().Name}': {ex.Message}");
#if DEBUG
                            Console.WriteLine($"[SetTheme] Error updating window resources for '{win.Title ?? win.GetType().Name}': {ex.Message}");
#endif
                        }
                    }
                }
            }
            catch
            {
                // Best-effort; if the platform doesn't support theme variants or lifetime isn't available, ignore
            }

            // Also update application-level merged dictionaries (some code may have placed style includes here)
            try
            {
                if (Application.Current?.Resources?.MergedDictionaries is { } appMerged)
                {
                    TraceMergedDictionaries("App.SetTheme before resource update", appMerged);
                    for (int mdIdx = 0; mdIdx < appMerged.Count; mdIdx++)
                    {
                        var md = appMerged[mdIdx];
                        if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                        {
                            Debug.WriteLine($"[SetTheme] App.Resources.MergedDictionaries[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#if DEBUG
                            Console.WriteLine($"[SetTheme] App.Resources.MergedDictionaries[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#endif
                            mdsi.Source = new Uri(themePath);
                        }
                        else if (md is Avalonia.Styling.Styles nestedStyles)
                        {
                            UpdateStylesCollection(nestedStyles, themePath);
                        }
                    }
                    TraceMergedDictionaries("App.SetTheme after resource update", appMerged);
                }
            }
            catch
            {
                // ignore
            }

            CleanupThemeAwareWindows();

            foreach (var reference in _themeAwareWindows)
            {
                if (reference.TryGetTarget(out var window))
                {
                    ApplyThemeToWindow(window);
                }
            }

            // Final normalization: ensure any lingering PhantomTheme style includes
            // (whether in Application styles, merged dictionaries, nested Styles or
            // window-level styles) point to the requested theme path. This is a
            // best-effort sweep to prevent stale Light includes from remaining
            // when switching back to Dark (or vice-versa).
            try
            {
                var desktop2 = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

                if (Application.Current?.Styles is Styles appStyles)
                {
                    UpdateStylesCollection(appStyles, themePath);
                }

                if (Application.Current?.Resources?.MergedDictionaries is { } appMerged2)
                {
                    for (int mdIdx = 0; mdIdx < appMerged2.Count; mdIdx++)
                    {
                        var md = appMerged2[mdIdx];
                        if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                        {
                            mdsi.Source = new Uri(themePath);
                        }
                        else if (md is Styles nested)
                        {
                            UpdateStylesCollection(nested, themePath);
                        }
                    }
                }

                if (desktop2 != null)
                {
                    foreach (var win in desktop2.Windows)
                    {
                        UpdateStylesCollection(win.Styles, themePath);

                        if (win.Resources?.MergedDictionaries is { } merged2)
                        {
                            for (int mdIdx = 0; mdIdx < merged2.Count; mdIdx++)
                            {
                                var md = merged2[mdIdx];
                                if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                                {
                                    mdsi.Source = new Uri(themePath);
                                }
                                else if (md is Styles nested)
                                {
                                    UpdateStylesCollection(nested, themePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetTheme] FinalizeThemeIncludes error: {ex.Message}");
#if DEBUG
                Console.WriteLine($"[SetTheme] FinalizeThemeIncludes error: {ex.Message}");
#endif
            }
        }

        public void RegisterThemeAwareWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            CleanupThemeAwareWindows();

            if (!IsWindowTracked(window))
            {
                _themeAwareWindows.Add(new WeakReference<Window>(window));
            }

            ApplyThemeToWindow(window);
        }

        public void UnregisterThemeAwareWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            for (int i = _themeAwareWindows.Count - 1; i >= 0; i--)
            {
                if (!_themeAwareWindows[i].TryGetTarget(out var existing) || ReferenceEquals(existing, window))
                {
                    _themeAwareWindows.RemoveAt(i);
                }
            }
        }

        private bool IsWindowTracked(Window window)
        {
            foreach (var reference in _themeAwareWindows)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                {
                    return true;
                }
            }

            return false;
        }

        private void CleanupThemeAwareWindows()
        {
            for (int i = _themeAwareWindows.Count - 1; i >= 0; i--)
            {
                if (!_themeAwareWindows[i].TryGetTarget(out _))
                {
                    _themeAwareWindows.RemoveAt(i);
                }
            }
        }

        private void ApplyThemeToWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            RemoveThemeIncludes(window);

            if (_currentTheme == "light")
            {
                window.Styles.Add(new StyleInclude(ThemeBaseUri) { Source = LightThemeUri });
                window.RequestedThemeVariant = ThemeVariant.Light;
            }
            else
            {
                // Ensure windows that opt into theming get the dark theme include as well
                window.Styles.Add(new StyleInclude(ThemeBaseUri) { Source = DarkThemeUri });
                window.RequestedThemeVariant = ThemeVariant.Dark;
            }
        }

        private static void RemoveThemeIncludes(Window window)
        {
            // Remove any style includes that reference our PhantomTheme (light or dark)
            for (int i = window.Styles.Count - 1; i >= 0; i--)
            {
                if (window.Styles[i] is StyleInclude include && include.Source != null)
                {
                    var src = include.Source.OriginalString ?? string.Empty;
                    if (src.Contains("Assets/Themes/PhantomTheme"))
                    {
                        window.Styles.RemoveAt(i);
                    }
                }
            }
        }

        private static void UpdateStylesCollection(Styles? styles, string themePath)
        {
            if (styles == null || styles.Count == 0)
            {
                return;
            }

            TraceStyles("UpdateStylesCollection before", styles);

            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] is StyleInclude include && include.Source != null && include.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                {
                    Debug.WriteLine($"[UpdateStylesCollection] Updating style include from {include.Source.OriginalString} to {themePath}");
#if DEBUG
                    Console.WriteLine($"[UpdateStylesCollection] Updating style include from {include.Source.OriginalString} to {themePath}");
#endif
                    include.Source = new Uri(themePath);
                }
            }

            TraceStyles("UpdateStylesCollection after", styles);
        }

        private static void TraceStyles(string context, Styles? styles)
        {
            if (styles == null)
            {
                Debug.WriteLine($"[TraceStyles] {context}: styles=null");
#if DEBUG
                Console.WriteLine($"[TraceStyles] {context}: styles=null");
#endif
                return;
            }

            Debug.WriteLine($"[TraceStyles] {context}: count={styles.Count}");
#if DEBUG
            Console.WriteLine($"[TraceStyles] {context}: count={styles.Count}");
#endif

            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] is StyleInclude include)
                {
                    var source = include.Source?.OriginalString ?? "<null>";
                    Debug.WriteLine($"[TraceStyles]   [{i}] StyleInclude -> {source}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] StyleInclude -> {source}");
#endif
                }
                else if (styles[i] is Styles nested)
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] Styles (nested) count={nested.Count}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] Styles (nested) count={nested.Count}");
#endif
                }
                else if (styles[i] != null)
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] {styles[i].GetType().Name}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] {styles[i].GetType().Name}");
#endif
                }
                else
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] <null entry>");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] <null entry>");
#endif
                }
            }
        }

        private static void TraceMergedDictionaries(string context, IList<IResourceProvider>? dictionaries)
        {
            if (dictionaries == null)
            {
                Debug.WriteLine($"[TraceMergedDictionaries] {context}: dictionaries=null");
#if DEBUG
                Console.WriteLine($"[TraceMergedDictionaries] {context}: dictionaries=null");
#endif
                return;
            }

            Debug.WriteLine($"[TraceMergedDictionaries] {context}: count={dictionaries.Count}");
#if DEBUG
            Console.WriteLine($"[TraceMergedDictionaries] {context}: count={dictionaries.Count}");
#endif

            for (int i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i] is StyleInclude include)
                {
                    var source = include.Source?.OriginalString ?? "<null>";
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] StyleInclude -> {source}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] StyleInclude -> {source}");
#endif
                }
                else if (dictionaries[i] is Styles nested)
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] Styles (nested) count={nested.Count}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] Styles (nested) count={nested.Count}");
#endif
                }
                else if (dictionaries[i] != null)
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] {dictionaries[i].GetType().Name}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] {dictionaries[i].GetType().Name}");
#endif
                }
                else
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] <null entry>");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] <null entry>");
#endif
                }
            }
        }

        /// <summary>
        /// Creates default defence rules for automated threat response.
        /// These rules define how the Defence Engine responds to various security threats.
        /// </summary>
        private static List<DefenceRule> CreateDefaultDefenceRules()
        {
            return new List<DefenceRule>
            {
                // Failed login burst - progressive response
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

                // New device fingerprint - require additional authentication
                new DefenceRule(
                    id: "new-device",
                    triggerType: ThreatType.NewDeviceFingerprint,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.RequirePhantomKey, DefenceActionType.AddDelay },
                    cooldown: TimeSpan.FromMinutes(1)
                ),

                // Integrity mismatch - critical security threat
                new DefenceRule(
                    id: "integrity-critical",
                    triggerType: ThreatType.IntegrityMismatch,
                    minLevel: ThreatLevel.Critical,
                    actions: new[] { DefenceActionType.EnterReadOnlyMode, DefenceActionType.ScrubShortLivedData },
                    cooldown: null // No cooldown - always respond to integrity failures
                ),

                // Excessive exports - possible data exfiltration
                new DefenceRule(
                    id: "excessive-exports",
                    triggerType: ThreatType.ExcessiveExports,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay, DefenceActionType.EnterReadOnlyMode },
                    cooldown: TimeSpan.FromMinutes(10)
                ),

                // High-risk entry flood - automated attack
                new DefenceRule(
                    id: "entry-flood",
                    triggerType: ThreatType.HighRiskEntryFlood,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.EnterReadOnlyMode, DefenceActionType.TempLockout },
                    cooldown: TimeSpan.FromMinutes(5)
                ),

                // Behavior deviation - suspicious activity
                new DefenceRule(
                    id: "behavior-deviation",
                    triggerType: ThreatType.BehaviourDeviation,
                    minLevel: ThreatLevel.Warning,
                    actions: new[] { DefenceActionType.AddDelay, DefenceActionType.ScrubShortLivedData },
                    cooldown: TimeSpan.FromMinutes(2)
                ),

                // Physical removal - emergency response
                new DefenceRule(
                    id: "physical-removal",
                    triggerType: ThreatType.PhysicalRemoval,
                    minLevel: ThreatLevel.Critical,
                    actions: new[] { DefenceActionType.ScrubShortLivedData, DefenceActionType.TempLockout },
                    cooldown: null // No cooldown - always respond to physical security threats
                )
            };
        }

    }
}
