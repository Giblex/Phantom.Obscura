using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.AutoFill;
using PhantomVault.UI.Services.TrayBackground;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Giblex.Controls;

namespace PhantomVault.UI
{

    public sealed partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        private PhantomVault.UI.Services.GlobalHotkeyService? _globalHotkey;
        private string _currentTheme = "dark";
        private readonly List<WeakReference<Window>> _themeAwareWindows = new();
        private static readonly Uri ThemeBaseUri = new("avares://PhantomVault.UI/");
        private static readonly Uri LightThemeUri = new("avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Light.axaml");
        private static readonly Uri DarkThemeUri = new("avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Dark.axaml");
        public IReadOnlyList<WeakReference<Window>> ThemeAwareWindows => _themeAwareWindows;

        public ServiceProvider? Services => _serviceProvider;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            LiquidGlassScrollChrome.Register();
        }

        public override void OnFrameworkInitializationCompleted()
        {
#if !DEBUG
            if (string.Equals(
                Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY"),
                "1",
                StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Refusing to start: PHANTOM_DEV_BYPASS_POLICY is not permitted in Release builds.");
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime blockedDesktop)
                {
                    blockedDesktop.Shutdown(1);
                }
                return;
            }
#endif

            // Restore the saved UI language before any window is created, so the
            // string dictionary is in place for the first view that binds to it.
            PhantomVault.UI.ViewModels.Settings.AccessibilitySettingsViewModel.ApplyPersistedLanguage();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Independent service health is bridged into the existing defence engine.
            // A critical executable or manifest mismatch enters read-only mode and
            // scrubs short-lived data through the integrity-critical rule.
            _serviceProvider.GetRequiredService<PhantomVault.UI.Services.Security.IntegrityWatchdogStatusService>().Start();

#if DEBUG
            RecoveryDeveloperMode.IsEnabled = true;
            RecoveryDeveloperMode.Log("Developer mode enabled for PhantomRecovery integration");
#endif

            var persistedSettings = SettingsService.Load();
            PrivacyShield.PrivacyModeEnabled = persistedSettings.PrivacyModeEnabled;
            PrivacyShield.RedactDiagnostics = persistedSettings.RedactDiagnosticLogs;
            PrivacyShield.DebugLoggingEnabled = persistedSettings.EnableDebugLogging;

            // P-1: bind the master "go offline" switch to the gateway. Privacy mode
            // engages offline mode, which revokes every active grant and refuses new
            // consent prompts until the user disables privacy mode.
            try
            {
                var gateway = _serviceProvider.GetRequiredService<PhantomVault.Core.Services.Network.IInternetGateway>();
                gateway.OfflineMode = PrivacyShield.PrivacyModeEnabled;
                PrivacyShield.PrivacyModeChanged += (_, _) => gateway.OfflineMode = PrivacyShield.PrivacyModeEnabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Failed to wire OfflineMode binding: {ex.Message}");
            }

            try
            {
                var themeManager = _serviceProvider.GetRequiredService<ThemeManagerService>();
                var scale = Math.Clamp(persistedSettings.RenderScale, 0.8, 1.5);
                themeManager.SetRenderScale(scale);

                var runtimeThemeService = _serviceProvider.GetRequiredService<IRuntimeThemeService>();
                if (!string.IsNullOrEmpty(persistedSettings.SelectedThemeId))
                {
                    runtimeThemeService.Apply(persistedSettings.SelectedThemeId);
                }

                // The dark/light variant follows the selected skin unless high contrast
                // overrides both. The variant chooses the app-level PhantomTheme fallback
                // for every key a skin does not define, and it drives Fluent's control
                // defaults — so a light skin under the dark variant produced dark fallback
                // surfaces and near-invisible checkbox outlines. Persisted IsDarkTheme is
                // only the fallback for skins with no declared polarity.
                var selectedSkin = runtimeThemeService.GetThemes()
                    .FirstOrDefault(t => string.Equals(t.Id, persistedSettings.SelectedThemeId, StringComparison.OrdinalIgnoreCase));

                var wantsDark = selectedSkin is null
                    ? persistedSettings.IsDarkTheme
                    : !selectedSkin.IsLight;

                var theme = persistedSettings.EnableHighContrast
                    ? AppTheme.HighContrast
                    : wantsDark ? AppTheme.Dark : AppTheme.Light;
                themeManager.SetTheme(theme);

                themeManager.SetSkin(persistedSettings.ThemeSkin);
                themeManager.SetEffects(persistedSettings.ReduceAnimations, persistedSettings.ReduceTransparency);
                themeManager.SetShowCategoryColorBarOnly(persistedSettings.ShowCategoryColorBarOnly);

                themeManager.SetAppFont(persistedSettings.AppFontFamily);
                themeManager.SetAppFontSize(persistedSettings.AppFontSize);
                // #2B4A7A was the old implicit default. Move installations that never
                // chose a custom accent to the Giblex Glass Navy button accent; explicit
                // user colour choices remain untouched.
                if (string.Equals(persistedSettings.SelectedThemeId, "GiblexGlassNavy", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(persistedSettings.AccentColorHex, "#2B4A7A", StringComparison.OrdinalIgnoreCase))
                {
                    persistedSettings.AccentColorHex = "#5A7AB0";
                    SettingsService.Update(s => s.AccentColorHex = "#5A7AB0");
                }
                themeManager.SetAccentColor(persistedSettings.AccentColorHex);
                themeManager.SetFlatButtons(persistedSettings.UseFlatButtons);
                themeManager.SetFlatButtonBorders(persistedSettings.UseFlatButtonBorders);

                // Accessibility runtime flags: hydrate the AccessibilityService from persisted
                // settings and apply the tooltip scale. The service is the single source the
                // animation/tooltip consumers read.
                AccessibilityService.Instance.UseHighContrast = persistedSettings.EnableHighContrast;
                AccessibilityService.Instance.LargeTooltips = persistedSettings.LargeTooltips;
                AccessibilityService.Instance.ScreenReaderOptimizations = persistedSettings.EnableScreenReader;
                ApplyTooltipScale(persistedSettings.LargeTooltips);
                ApplyScreenReaderOptimizations(persistedSettings.EnableScreenReader);
                AccessibilityService.Instance.SettingsChanged += (_, _) =>
                {
                    ApplyTooltipScale(AccessibilityService.Instance.LargeTooltips);
                    ApplyScreenReaderOptimizations(AccessibilityService.Instance.ScreenReaderOptimizations);
                };
            }
            catch
            {

            }

            try
            {
                var syncService = _serviceProvider.GetRequiredService<CrossAppSyncService>();
                syncService.Start();
            }
            catch
            {

            }

            // Keyless suite session: start watching shared suite state and honour a
            // suite-wide lock by locking the active vault. Subscribed ONCE here (app
            // scope) to avoid per-unlock handler accumulation. Carries no key material.
            try
            {
                var suiteSession = _serviceProvider.GetRequiredService<Phantom.Suite.Session.ISuiteSessionCoordinator>();
                suiteSession.SuiteLockRequested += (_, e) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dt)
                            {
                                foreach (var w in dt.Windows)
                                {
                                    if (w.DataContext is PhantomVault.UI.ViewModels.VaultViewModel vvm)
                                        vvm.RequestSuiteLock();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[App] Suite lock handling failed: {ex.Message}");
                        }
                    });
                };
                suiteSession.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to start suite session coordinator: {ex.Message}");
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

                var zkVaultService = _serviceProvider.GetRequiredService<IZkVaultService>();
                _ = zkVaultService.CleanupOrphanedTempFilesAsync(TimeSpan.FromHours(24));

                desktop.ShutdownRequested += OnShutdownRequested;
                desktop.Exit += OnApplicationExit;

                var welcomePage = new WelcomePage
                {
                    DataContext = _serviceProvider.GetRequiredService<WelcomePageViewModel>()
                };
                desktop.MainWindow = welcomePage;

                var sysSecController = _serviceProvider.GetRequiredService<ISystemSecurityController>();
                sysSecController.RegisterClipboardClearer(async () =>
                {
                    var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    if (window != null)
                    {
                        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;
                        if (clipboard != null)
                            await clipboard.ClearAsync();
                    }
                });

                var idleLockService = _serviceProvider.GetRequiredService<IdleLockService>();
                idleLockService.IdleElapsed += () =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await zkVaultService.LockAndWipeKeysAsync().ConfigureAwait(false);
                            await sysSecController.ClearClipboardAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Swallowing this meant a FAILED key wipe looked identical to a
                            // successful one: the vault stayed unlocked on an idle timeout
                            // and nothing recorded it. Log it, then make a best-effort
                            // second attempt at the wipe specifically.
                            Serilog.Log.Error(ex, "Idle auto-lock failed to wipe keys or clear the clipboard");

                            try
                            {
                                await zkVaultService.LockAndWipeKeysAsync().ConfigureAwait(false);
                            }
                            catch (Exception retryEx)
                            {
                                Serilog.Log.Fatal(retryEx, "Idle auto-lock key wipe failed on retry — vault may remain unlocked");
                            }
                        }
                    });
                };

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

                try
                {
                    // CredentialSubmitted is consumed by VaultViewModel (see
                    // InitializeAutoInject): it owns both the credential list needed to
                    // decide save-vs-update and the write path used to store the result.
                    var pipeServer = _serviceProvider.GetRequiredService<INativeHostPipeServer>();
                    pipeServer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to start native-host pipe server: {ex.Message}");
                }

                try
                {
                    var totpSyncPipeServer = _serviceProvider.GetRequiredService<PhantomVault.UI.Services.Sync.ITotpSyncPipeServer>();
                    totpSyncPipeServer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to start TOTP sync pipe server: {ex.Message}");
                }

                InitializePrivilegedBroker();

                desktop.MainWindow.Closing += (sender, e) =>
                {
                    var settings = SettingsService.Load();
                    if (settings.AutoFillModeEnabled)
                    {
                        e.Cancel = true;
                        ((Window)sender!).Hide();
                    }
                };

                if (persistedSettings.GlobalHotkeyEnabled && OperatingSystem.IsWindows())
                {
                    try
                    {
                        // Default chord: Ctrl+Alt+P — brings Phantom Obscura to the foreground.
                        _globalHotkey = new PhantomVault.UI.Services.GlobalHotkeyService(
                            PhantomVault.UI.Services.GlobalHotkeyService.MOD_CONTROL | PhantomVault.UI.Services.GlobalHotkeyService.MOD_ALT,
                            0x50); // VK 'P'
                        _globalHotkey.HotkeyPressed += () =>
                            Avalonia.Threading.Dispatcher.UIThread.Post(ActivateMainWindow);
                        _globalHotkey.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Failed to start global hotkey: {ex.Message}");
                    }
                }

            }

            base.OnFrameworkInitializationCompleted();
        }

        private void InitializePrivilegedBroker()
        {
            if (!OperatingSystem.IsWindows() || _serviceProvider is null)
                return;

            try
            {
                // Already elevated (e.g. a deliberate "Run as administrator"): run the
                // privileged primitives in-process and skip the broker entirely.
                if (PhantomVault.Core.Services.Privileged.PrivilegedExecution.IsProcessElevated())
                {
                    PhantomVault.Core.Services.Privileged.PrivilegedExecution.ForceInProcess = true;
                    return;
                }

                // Non-elevated: route privileged primitives to the elevated helper.
                var client = _serviceProvider.GetRequiredService<PhantomVault.UI.Services.Privileged.NamedPipeBrokerClient>();
                var controller = _serviceProvider.GetRequiredService<PhantomVault.UI.Services.Privileged.BrokerServiceController>();
                var dialogService = _serviceProvider.GetRequiredService<DialogService>();

                // First launch shows the "Enable privileged helper" dialog once.
                // The user's answer is stored in settings so subsequent launches
                // either install silently (consented) or skip the prompt entirely
                // (declined). Toggleable under Settings → Security.
                client.EnsureAvailableAsync = async () =>
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        // A previously installed helper may have been stopped by an
                        // update, reboot race, or service-management action. Recover it
                        // directly; first-install consent must not suppress restart.
                        if (controller.IsInstalled())
                            return await controller.StartInstalledAsync();

                        var current = SettingsService.Load();

                        Func<Task<bool>> confirm;
                        if (current.PrivilegedBrokerAutoInstall)
                        {
                            // Consent already given — install silently.
                            confirm = () => Task.FromResult(true);
                        }
                        else if (current.PrivilegedBrokerDeclined)
                        {
                            // User previously declined — never re-prompt.
                            confirm = () => Task.FromResult(false);
                        }
                        else
                        {
                            // First time — prompt, then persist the answer.
                            confirm = async () =>
                            {
                                var granted = await dialogService.ShowConfirmationAsync(
                                    "Enable privileged helper",
                                    "This action needs the Phantom Obscura privileged helper. Install it once to continue — you won't be asked again.",
                                    confirmText: "Enable",
                                    cancelText: "Cancel");

                                var s = SettingsService.Load();
                                if (granted) s.PrivilegedBrokerAutoInstall = true;
                                else s.PrivilegedBrokerDeclined = true;
                                SettingsService.Save(s);
                                return granted;
                            };
                        }

                        return await controller.PromptAndInstallAsync(confirm);
                    });

                PhantomVault.Core.Services.Privileged.PrivilegedExecution.Broker = client;

                // Deliberately no eager install here. This used to fire a raw Windows
                // UAC elevation prompt in the background on every non-elevated launch —
                // with no app-level context and no memory of a decline, so cancelling it
                // just meant getting it again on the very next launch. Installing the
                // helper is now purely reactive: EnsureAvailableAsync (wired above) shows
                // an in-app confirmation dialog the moment a privileged feature actually
                // needs it, and only then triggers the single UAC prompt.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] InitializePrivilegedBroker failed: {ex.Message}");
            }
        }

        private static void ApplyTooltipScale(bool largeTooltips)
        {
            try
            {
                if (Application.Current == null) return;
                Application.Current.Resources["ToolTipFontSize"] = largeTooltips ? 16.0 : 12.0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to apply tooltip scale: {ex.Message}");
            }
        }

        private static void ApplyScreenReaderOptimizations(bool enabled)
        {
            try
            {
                if (Application.Current == null) return;
                // Screen-reader / motor-accessibility optimisation: keep scrollbars permanently
                // visible (no auto-hide) so they are reliably discoverable. The OS screen reader
                // itself reads the AutomationProperties exposed throughout the app unconditionally.
                Application.Current.Resources["ScrollBarAllowAutoHide"] = !enabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to apply screen reader optimizations: {ex.Message}");
            }
        }

        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            try
            {
                Debug.WriteLine("[App] Shutdown requested - cleaning up resources");
#if DEBUG
                Console.WriteLine("[App] Shutdown requested - cleaning up resources");
#endif

                _serviceProvider?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during shutdown: {ex.Message}");
            }
        }

        private void ActivateMainWindow()
        {
            try
            {
                var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (window == null) return;
                if (!window.IsVisible) window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ActivateMainWindow failed: {ex.Message}");
            }
        }

        private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            try
            {
                _globalHotkey?.Dispose();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to dispose the global hotkey during shutdown");
            }

            try
            {
                Debug.WriteLine("[App] Application exiting - cleaning up spawned processes");
#if DEBUG
                Console.WriteLine("[App] Application exiting - cleaning up spawned processes");
#endif

                try
                {
                    var sysController = _serviceProvider?.GetService<ISystemSecurityController>();
                    sysController?.ClearClipboardAsync().Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error clearing clipboard on exit: {ex.Message}");
                }

                try
                {
                    SpawnedProcessTracker.Instance.TerminateAllTrackedProcesses();
                    Debug.WriteLine("[App] Spawned processes cleaned up successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error cleaning up spawned processes: {ex.Message}");
                }

                Environment.Exit(e.ApplicationExitCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during exit: {ex.Message}");
                Environment.Exit(-1);
            }
        }
    }
}
