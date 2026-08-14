using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Controls;
using PhantomVault.UI.Desktop.Controls;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Serilog;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System.IO;
using ReactiveUI;

namespace PhantomVault.UI.Views
{
    public partial class VaultWindow : ThemeAwareWindow
    {
        private const double HoverHighlightHorizontalPadding = 32;
        private const double HoverHighlightVerticalPadding = 12;

        private Grid? _credentialListHost;
        private ListBox? _credentialListBox;
        private LiquidGlassHighlightBar? _credentialHoverHighlight;
        private ScrollViewer? _editScrollViewer;
        private StackPanel? _editFormStack;
        private Control? _creditCardPanel;
        private Control? _identityPanel;
        private Control? _wifiPanel;
        private Control? _apiKeyPanel;
        private Border? _creditCardPanelBorder;
        private Border? _identityPanelBorder;
        private Border? _wifiPanelBorder;
        private Border? _apiKeyPanelBorder;
        private Border? _detailPanel;
        private Border? _detailCard;
        private Control? _credentialList;
        private Border? _editPanelOverlay;
        private Border? _editPanelContainer;
        private VaultViewModel? _currentVaultViewModel;
        private AddEditCredentialViewModel? _currentEditViewModel;

        private IDisposable? _commandPaletteSubscription;
        private CommandPaletteWindow? _openPalette;
        private bool _autoLockInProgress;
        private bool _allowCloseAfterSecureTrashPrompt;
        private readonly DispatcherTimer _sessionPolicyTimer;
        private DateTimeOffset? _sessionStartedUtc;
        private DateTimeOffset? _lastUserActivityUtc;
        private readonly System.Collections.Generic.List<KeyBinding> _savedWindowKeyBindings = new();

        public VaultWindow()
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("VaultWindow constructor start");
#endif
            InitializeComponent();

            ApplyKeyboardShortcutSetting();
            SettingsService.SettingsChanged += OnKeyboardShortcutSettingChanged;

            _credentialListHost = this.FindControl<Grid>("CredentialListHost");
            _credentialListBox = this.FindControl<ListBox>("CredentialListBox");
            _credentialHoverHighlight = this.FindControl<LiquidGlassHighlightBar>("CredentialHoverHighlight");
            _editScrollViewer = this.FindControl<ScrollViewer>("EditFormScrollViewer");
            _editFormStack = this.FindControl<StackPanel>("EditFormStack");
            _creditCardPanel = this.FindControl<Control>("CreditCardPanel");
            _identityPanel = this.FindControl<Control>("IdentityPanel");
            _wifiPanel = this.FindControl<Control>("WiFiPanel");
            _apiKeyPanel = this.FindControl<Control>("ApiKeyPanel");
            _creditCardPanelBorder = this.FindControl<Border>("CreditCardPanelBorder");
            _identityPanelBorder = this.FindControl<Border>("IdentityPanelBorder");
            _wifiPanelBorder = this.FindControl<Border>("WiFiPanelBorder");
            _apiKeyPanelBorder = this.FindControl<Border>("ApiKeyPanelBorder");
            _detailPanel = this.FindControl<Border>("DetailPanel");
            _detailCard = this.FindControl<Border>("DetailCard");
            _credentialList = this.FindControl<Control>("CredentialList");
            _editPanelOverlay = this.FindControl<Border>("EditPanelOverlay");
            _editPanelContainer = this.FindControl<Border>("EditPanelContainer");

            AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

            var addToggle = this.FindControl<ToggleButton>("AddToggle");
            var addPopup = this.FindControl<Avalonia.Controls.Primitives.Popup>("AddPopup");
            Log.Debug("[VaultWindow] Constructed. AddToggle={HasToggle}, AddPopup={HasPopup}", addToggle != null, addPopup != null);

            try
            {
                if (addToggle is not null)
                {
                    addToggle.GetObservable(ToggleButton.IsCheckedProperty).Subscribe(isChecked =>
                    {
                        Log.Debug("[VaultWindow] AddToggle.IsChecked -> {IsChecked}, PopupIsOpen={IsOpen}", isChecked, addPopup?.IsOpen);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[VaultWindow] AddToggle instrumentation failed");
            }

            var headerView = this.FindControl<Views.HeaderView>("HeaderView");
            if (headerView != null)
            {
                headerView.PasswordGeneratorRequested += (s, e) => OpenPasswordGenerator_Click(s, e);
                headerView.SettingsRequested += (s, e) => OpenSettings_Click(s, e);
            }

            var gripPeek = this.FindControl<Panel>("DashboardGripPeek");
            if (gripPeek != null)
            {
                gripPeek.PointerPressed += DashboardGripPeek_PointerPressed;
            }

            this.Opened += VaultWindow_Opened;
            SettingsService.SettingsChanged += OnSettingsChanged;

            var recoveryPanel = this.FindControl<Views.RecoveryPanel>("RecoveryPanelControl");
            if (recoveryPanel != null)
            {
                recoveryPanel.CloseRequested += RecoveryPanel_CloseRequested;
                recoveryPanel.LaunchRequested += RecoveryPanel_LaunchRequested;
            }

            WireUpSecurityDashboard();

            this.DataContextChanged += VaultWindow_DataContextChanged;

            this.GetObservable(BoundsProperty).Subscribe(bounds =>
            {
                if (DataContext is VaultViewModel vm)
                {
                    vm.UpdateSidebarVisibility(bounds.Width);
                }
            });

            this.GetObservable(WindowStateProperty).Subscribe(OnWindowStateChanged);
            this.Closing += VaultWindow_Closing;
            this.Closed += VaultWindow_Closed;

            try
            {
                SystemEvents.SessionSwitch += OnSystemSessionSwitch;
            }
            catch
            {

            }

            _sessionPolicyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _sessionPolicyTimer.Tick += SessionPolicyTimer_Tick;
#if DEBUG
            System.Diagnostics.Debug.WriteLine("VaultWindow constructor end");
#endif
        }

        private async void VaultWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowCloseAfterSecureTrashPrompt)
            {
                return;
            }

            // AutoFill Mode: user-initiated X close should hide-to-tray, preserving the
            // unlocked session so the tray icon can reveal it again. Real shutdowns
            // (Exit from tray menu, OS logoff) still close normally.
            try
            {
                if (e.CloseReason == WindowCloseReason.WindowClosing
                    && SettingsService.Load().AutoFillModeEnabled)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
            }
            catch {  }

            try
            {
                await RunAutomatedBackupOnCloseAsync();

                var settings = SettingsService.Load();
                if (!settings.SecureTrashAutoEmptyOnClose)
                {
                    return;
                }

                var app = Application.Current as App;
                var secureTrashService = app?.Services?.GetService(typeof(SecureTrashService)) as SecureTrashService;
                if (secureTrashService == null || secureTrashService.Records.Count == 0)
                {
                    return;
                }

                var dialogService = app?.Services?.GetService(typeof(DialogService)) as DialogService ?? new DialogService();

                if (settings.SecureTrashPromptBeforeDeletion)
                {
                    e.Cancel = true;

                    var confirmed = await dialogService.ShowConfirmationAsync(
                        "Empty Secure Rubbish Bin",
                        "Auto-empty on close is enabled. Closing this vault window will securely purge all items currently in the secure rubbish bin. Continue?",
                        this);

                    if (!confirmed)
                    {
                        return;
                    }

                    secureTrashService.SecurelyPurgeAll();
                    _allowCloseAfterSecureTrashPrompt = true;
                    Close();
                    return;
                }

                secureTrashService.SecurelyPurgeAll();
            }
            catch
            {

            }
        }

        private async System.Threading.Tasks.Task RunAutomatedBackupOnCloseAsync()
        {
            try
            {
                if (_currentVaultViewModel == null)
                {
                    return;
                }

                if (!_currentVaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
                {
                    return;
                }

                var services = (Application.Current as App)?.Services;
                var backupService = services?.GetService(typeof(BackupService)) as BackupService;
                if (backupService == null)
                {
                    return;
                }

                var fallbackBackupDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "PhantomVault",
                    "Backups");

                await BackupAutomationService.RunAutomatedBackupIfDueAsync(
                    backupService,
                    manifestPath,
                    passphrase,
                    keyfilePath,
                    fallbackBackupDirectory);
            }
            catch
            {

            }
        }

        private void DashboardGripPeek_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_currentVaultViewModel != null && !_currentVaultViewModel.IsShowingDashboard)
            {
                _currentVaultViewModel.IsShowingDashboard = true;
                e.Handled = true;
            }
        }

        private void VaultWindow_Opened(object? sender, EventArgs e)
        {

            try
            {
                var settings = PhantomVault.UI.Services.SettingsService.Load();
                if (WindowProtectionService.IsSupported())
                {
                    var platformHandle = this.TryGetPlatformHandle();
                    if (platformHandle != null)
                    {
                        var hwnd = platformHandle.Handle;

                        if (settings.EnableScreenshotProtection)
                        {
                            if (WindowProtectionService.EnableScreenshotProtection(hwnd))
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine("Screenshot protection ENABLED on window open");
#endif
                            }
                            else
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine("Failed to enable screenshot protection on window open");
#endif
                            }
                        }
                        else
                        {

                            if (WindowProtectionService.DisableScreenshotProtection(hwnd))
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine("Screenshot protection DISABLED on window open (per user settings)");
#endif
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Error applying screenshot protection: {ex.Message}");
#else
                _ = ex;
#endif
            }

            if (DataContext is VaultViewModel vm)
            {
                var settings = SettingsService.Load();
                if (settings.RequireUnlockToShow && !vm.IsLockscreenVisible)
                {
                    TriggerAutoLock(vm, "show");
                }
            }
        }

        private void VaultWindow_Closed(object? sender, EventArgs e)
        {
            StopSessionPolicyTracking();
            SettingsService.SettingsChanged -= OnSettingsChanged;

            try
            {
                SystemEvents.SessionSwitch -= OnSystemSessionSwitch;
            }
            catch
            {

            }
        }

        private void VaultWindow_DataContextChanged(object? sender, EventArgs e)
        {
            if (_currentVaultViewModel != null)
            {
                _currentVaultViewModel.PropertyChanged -= VaultViewModel_PropertyChanged;
            }

            _commandPaletteSubscription?.Dispose();
            _commandPaletteSubscription = null;

            if (DataContext is ViewModels.VaultViewModel vm)
            {
                _currentVaultViewModel = vm;
                _currentVaultViewModel.PropertyChanged += VaultViewModel_PropertyChanged;
                _commandPaletteSubscription = vm.OpenCommandPaletteCommand.Subscribe(_ => ShowCommandPalette(vm));
                HandleEditViewModelChanged(vm.EditViewModel);
                StartSessionPolicyTracking(restartSession: true);

                try
                {
                    Log.Debug("[VaultWindow] DataContext attached. FilteredCount={Filtered}, SelectedSettingsContent={Sel}",
                        vm.FilteredCredentials?.Count ?? 0,
                        vm.SelectedSettingsContent?.ToString() ?? "<null>");
                }
#pragma warning disable CA1031
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VaultWindow] Debug logging failed: {ex.Message}");
                }
#pragma warning restore CA1031
            }
            else
            {
                _currentVaultViewModel = null;
                HandleEditViewModelChanged(null);
            }
        }

        private void ShowCommandPalette(ViewModels.VaultViewModel vm)
        {

            if (_openPalette is { IsVisible: true })
            {
                _openPalette.Activate();
                return;
            }

            try
            {
                var actions = vm.BuildCommandPaletteActions();
                var paletteVm = new ViewModels.CommandPaletteViewModel(actions);
                var window = new CommandPaletteWindow(paletteVm);
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_openPalette, window))
                    {
                        _openPalette = null;
                    }
                };
                _openPalette = window;
                window.ShowDialog(this);
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"[VaultWindow] Command palette failed to open: {ex.Message}");
                _openPalette = null;
            }
        }

        private void VaultViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is ViewModels.VaultViewModel vm && e.PropertyName == nameof(vm.EditViewModel))
            {
                HandleEditViewModelChanged(vm.EditViewModel);
            }

            if (sender is ViewModels.VaultViewModel vmEdit && e.PropertyName == nameof(vmEdit.IsEditPanelVisible))
            {
                if (vmEdit.IsEditPanelVisible)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = RunEditPanelOpenAnimation();
                    }, Avalonia.Threading.DispatcherPriority.Loaded);
                }
                else
                {

                    if (_editPanelOverlay != null)
                        _editPanelOverlay.Opacity = 1;
                    if (_editPanelContainer != null)
                    {
                        _editPanelContainer.Opacity = 1;
                        _editPanelContainer.RenderTransform = null;
                    }
                }
            }

            if (sender is ViewModels.VaultViewModel vmLock && e.PropertyName == nameof(vmLock.IsLockscreenVisible))
            {
                if (vmLock.IsLockscreenVisible)
                {
                    StopSessionPolicyTracking();

                    var settings = SettingsService.Load();
                    if (settings.ClearClipboardOnLock)
                    {
                        _ = ClearClipboardAsync();
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var pinPad = this.FindControl<Views.Controls.PinEntryControl>("LockscreenPinPad");
                        pinPad?.Clear();
                        pinPad?.FocusPinInput();
                    }, Avalonia.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    StartSessionPolicyTracking(restartSession: true);
                }
            }

            if (sender is ViewModels.VaultViewModel vm3 && e.PropertyName == nameof(vm3.EnableScreenshotProtection))
            {
                var enableProtection = vm3.EnableScreenshotProtection;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateScreenshotProtection(enableProtection));
            }

            try
            {
                if (sender is ViewModels.VaultViewModel vm2 && e.PropertyName == nameof(vm2.FilteredCount))
                {
                    Log.Debug("[VaultWindow] PropertyChanged: FilteredCount -> {Count}", vm2.FilteredCount);
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultWindow] PropertyChanged debug logging failed: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private void EditViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is ViewModels.AddEditCredentialViewModel editVm)
            {

                if (e.PropertyName == nameof(editVm.IsCreditCardEntry) && editVm.IsCreditCardEntry)
                    ScrollPanelIntoView(_creditCardPanel);

                if (e.PropertyName == nameof(editVm.IsIdentityEntry) && editVm.IsIdentityEntry)
                    ScrollPanelIntoView(_identityPanel);

                if (e.PropertyName == nameof(editVm.IsWiFiEntry) && editVm.IsWiFiEntry)
                    ScrollPanelIntoView(_wifiPanel);

                if (e.PropertyName == nameof(editVm.IsApiKeyEntry) && editVm.IsApiKeyEntry)
                    ScrollPanelIntoView(_apiKeyPanel);
            }
        }

        private void OnWindowStateChanged(WindowState state)
        {
            if (state != WindowState.Minimized || DataContext is not VaultViewModel vm)
            {
                return;
            }

            var settings = SettingsService.Load();
            if (settings.AutoLockOnMinimize)
            {
                TriggerAutoLock(vm, "minimize");
            }
        }

        private void OnSystemSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionLock || DataContext is not VaultViewModel vm)
            {
                return;
            }

            var settings = SettingsService.Load();
            if (settings.AutoLockOnScreenLock)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => TriggerAutoLock(vm, "screen lock"));
            }
        }

        private void TriggerAutoLock(VaultViewModel vm, string reason)
        {
            if (_autoLockInProgress || vm.IsLockscreenVisible)
            {
                return;
            }

            try
            {
                // Never auto-lock behind a PIN prompt unless a PIN really exists;
                // SyncPinFlags also clears any stale enable flags it finds.
                if (!PinLockService.SyncPinFlags(vm.ManifestPath))
                {
                    return;
                }
            }
            catch
            {

                return;
            }

            _autoLockInProgress = true;
            StopSessionPolicyTracking();
            vm.LockCommand.Execute(Unit.Default).Subscribe(
                _ => { },
                _ => _autoLockInProgress = false,
                () => _autoLockInProgress = false);

            vm.StatusMessage = $"Vault locked on {reason}";
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            RegisterUserActivity();
        }

        private void OnKeyboardShortcutSettingChanged(object? sender, UserSettingsChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => ApplyKeyboardShortcutSetting(e.Settings.EnableKeyboardShortcuts));
        }

        private void ApplyKeyboardShortcutSetting()
        {
            bool enabled = true;
            try { enabled = SettingsService.Load().EnableKeyboardShortcuts; }
            catch { }
            ApplyKeyboardShortcutSetting(enabled);
        }

        private void ApplyKeyboardShortcutSetting(bool enabled)
        {
            if (!enabled)
            {
                if (KeyBindings.Count > 0)
                {
                    _savedWindowKeyBindings.Clear();
                    _savedWindowKeyBindings.AddRange(KeyBindings);
                    KeyBindings.Clear();
                }
            }
            else if (KeyBindings.Count == 0 && _savedWindowKeyBindings.Count > 0)
            {
                foreach (var binding in _savedWindowKeyBindings)
                {
                    KeyBindings.Add(binding);
                }
                _savedWindowKeyBindings.Clear();
            }
        }

        private void SessionPolicyTimer_Tick(object? sender, EventArgs e)
        {
            CheckSessionPolicies();
        }

        private void StartSessionPolicyTracking(bool restartSession)
        {
            if (DataContext is not VaultViewModel vm || vm.IsLockscreenVisible)
            {
                StopSessionPolicyTracking();
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (restartSession || _sessionStartedUtc is null)
            {
                _sessionStartedUtc = now;
            }

            _lastUserActivityUtc ??= now;

            if (!_sessionPolicyTimer.IsEnabled)
            {
                _sessionPolicyTimer.Start();
            }
        }

        private void StopSessionPolicyTracking()
        {
            if (_sessionPolicyTimer.IsEnabled)
            {
                _sessionPolicyTimer.Stop();
            }
        }

        private void RegisterUserActivity()
        {
            if (DataContext is not VaultViewModel vm || vm.IsLockscreenVisible)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _sessionStartedUtc ??= now;
            _lastUserActivityUtc = now;

            // Keep the app-scope idle watchdog in step with this window's activity clock.
            // Without this the watchdog only reset on a handful of specific vault events,
            // so it behaved as a fixed countdown and could wipe keys mid-session.
            try
            {
                (Avalonia.Application.Current as App)?.Services?
                    .GetService<PhantomVault.Core.Services.IdleLockService>()?.Reset();
            }
            catch
            {
                // Activity tracking must never take down the UI thread.
            }
        }

        private void CheckSessionPolicies()
        {
            if (DataContext is not VaultViewModel vm || vm.IsLockscreenVisible)
            {
                StopSessionPolicyTracking();
                return;
            }

            var settings = SettingsService.Load();
            var now = DateTimeOffset.UtcNow;

            _sessionStartedUtc ??= now;
            _lastUserActivityUtc ??= now;

            if (settings.SessionTimeoutMinutes > 0
                && now - _sessionStartedUtc.Value >= TimeSpan.FromMinutes(settings.SessionTimeoutMinutes))
            {
                TriggerAutoLock(vm, "session timeout");
                return;
            }

            if (settings.IdleTimeoutMinutes > 0
                && now - _lastUserActivityUtc.Value >= TimeSpan.FromMinutes(settings.IdleTimeoutMinutes))
            {
                TriggerAutoLock(vm, "inactivity");
            }
        }

        private async System.Threading.Tasks.Task ClearClipboardAsync()
        {
            try
            {
                if (Clipboard is not null)
                {
                    await Clipboard.SetTextAsync(string.Empty);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Failed to clear clipboard on lock: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        private async System.Threading.Tasks.Task RunEditPanelOpenAnimation()
        {
            if (_editPanelOverlay == null || _editPanelContainer == null)
                return;

            var translate = new TranslateTransform { X = 0, Y = 0 };
            _editPanelContainer.RenderTransform = translate;
            _editPanelOverlay.Opacity = 0;

            var slideDistance = _editPanelContainer.Bounds.Width > 0
                ? _editPanelContainer.Bounds.Width
                : 500;

            var overlayFade = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } }
                }
            };

            var slideAnim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, slideDistance) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, 0.0) } }
                }
            };

            await System.Threading.Tasks.Task.WhenAll(
                overlayFade.RunAsync(_editPanelOverlay),
                slideAnim.RunAsync(translate));

            _editPanelOverlay.Opacity = 1;
            _editPanelContainer.RenderTransform = null;
        }

        private void HandleEditViewModelChanged(AddEditCredentialViewModel? newViewModel)
        {
            if (_currentEditViewModel != null)
            {
                _currentEditViewModel.PropertyChanged -= EditViewModel_PropertyChanged;
            }

            _currentEditViewModel = newViewModel;

            if (_currentEditViewModel != null)
            {
                _currentEditViewModel.PropertyChanged += EditViewModel_PropertyChanged;
                _currentEditViewModel.SetOwnerWindow(this);
            }

            if (_editFormStack != null)
            {
                _editFormStack.DataContext = _currentEditViewModel;
            }
        }

        private async void ScrollPanelIntoView(Control? panel)
        {
            try
            {
                if (panel is null || _editScrollViewer is null) return;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {

                        var attempts = 0;
                        while (!panel.IsVisible && attempts < 8)
                        {
                            await System.Threading.Tasks.Task.Delay(25).ConfigureAwait(false);
                            attempts++;
                        }

                        if (!panel.IsVisible)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"Panel '{panel.Name}' did not become visible after waiting; proceeding anyway.");
#endif
                        }

                        var pt = panel.TranslatePoint(new Avalonia.Point(0, 0), _editScrollViewer);
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[Render] Bringing panel '{panel.Name}' into view. Translated point: {pt}");
#endif

                        panel.BringIntoView();

                        try
                        {
                            if (pt.HasValue && _editScrollViewer is not null)
                            {

                                const double desiredTopMargin = 80.0;

                                double currentOffsetY = 0;
                                try
                                {
                                    currentOffsetY = _editScrollViewer.Offset.Y;
                                }
                                catch {  }

                                var desired = currentOffsetY + pt.Value.Y - desiredTopMargin;
                                if (desired < 0) desired = 0;

                                try
                                {

                                    try
                                    {
                                        var cur = _editScrollViewer.Offset;
                                        _editScrollViewer.Offset = new Avalonia.Vector(cur.X, desired);
#if DEBUG
                                        System.Diagnostics.Debug.WriteLine($"[Render] Set edit viewer Offset to: {desired}");
#endif
                                    }
                                    catch (Exception exSet)
                                    {
#if DEBUG
                                        System.Diagnostics.Debug.WriteLine($"[Render] Setting Offset failed: {exSet.Message}");
#else
                                        _ = exSet;
#endif
                                    }
                                }
                                catch (Exception exScroll)
                                {
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"[Render] Manual offset handling failed: {exScroll.Message}");
#else
                                    _ = exScroll;
#endif
                                }
                            }
                        }
                        catch (Exception exScrollOuter)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[Render] Error while attempting manual scroll offset: {exScrollOuter.Message}");
#else
                            _ = exScrollOuter;
#endif
                        }

                        try
                        {
                            string? targetName = panel.Name switch
                            {
                                "CreditCardPanel" => "CardNumberTextBox",
                                "IdentityPanel" => "IdNumberTextBox",
                                "WiFiPanel" => "WiFiSSIDTextBox",
                                "ApiKeyPanel" => "ApiKeyValueTextBox",
                                _ => null
                            };

                            Control? toFocus = null;
                            if (!string.IsNullOrEmpty(targetName))
                            {
                                try
                                {
                                    toFocus = this.FindControl<Control>(targetName);
                                }
#pragma warning disable CA1031
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VaultWindow] FindControl failed for {targetName}: {ex.Message}");
                                }
#pragma warning restore CA1031
                            }

                            if (toFocus is null)
                            {
                                var focusable = panel.GetSelfAndVisualDescendants()
                                    .OfType<IInputElement>()
                                    .FirstOrDefault(x => x.Focusable);

                                if (focusable is Control cf)
                                {
                                    toFocus = cf;
                                }
                            }

                            if (toFocus != null)
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine($"[Render] Focusing control: {toFocus.GetType().Name} Name='{toFocus.Name}'");
#endif
                                toFocus.Focus();

                                try
                                {
                                    var borderName = panel.Name + "Border";
                                    var border = this.FindControl<Border>(borderName);
                                    if (border != null)
                                    {
                                        var original = border.BorderBrush;
                                        border.BorderBrush = new SolidColorBrush(Colors.Orange);
                                        await System.Threading.Tasks.Task.Delay(400).ConfigureAwait(false);

                                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { border.BorderBrush = original; }, Avalonia.Threading.DispatcherPriority.Render);
                                    }
                                }
#pragma warning disable CA1031
                                catch (Exception exFlash)
                                {
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"[Render] Error flashing border: {exFlash.Message}");
#else
                                    _ = exFlash;
#endif
                                }
#pragma warning restore CA1031
                            }
                            else
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine("[Render] No focusable descendant found to focus after BringIntoView().");
#endif
                            }
                        }
#pragma warning disable CA1031
                        catch (Exception exFocus)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[Render] Error while trying to focus panel child: {exFocus.Message}");
#else
                            _ = exFocus;
#endif
                        }
#pragma warning restore CA1031
                    }
#pragma warning disable CA1031
                    catch (Exception exInner)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[Render] ScrollPanelIntoView inner error: {exInner.Message}");
#else
                        _ = exInner;
#endif
                    }
#pragma warning restore CA1031
                }, Avalonia.Threading.DispatcherPriority.Render).ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"ScrollPanelIntoView scheduling error: {ex.Message}");
#else
                _ = ex;
#endif
            }
#pragma warning restore CA1031
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GoBack_Click(object? sender, RoutedEventArgs e)
        {

            Close();
        }

        private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is VaultViewModel vm && e.AddedItems.Count > 0)
            {
                var selected = e.AddedItems[0]?.ToString();
                if (!string.IsNullOrEmpty(selected))
                {
                    vm.OpenItemCommand.Execute(selected);
                }
            }
        }

        private void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not VaultViewModel vaultVm) return;

            if (vaultVm.IsSettingsPanelVisible)
            {
                vaultVm.CloseSettingsPanelCommand.Execute(Unit.Default).Subscribe();
            }
            else
            {
                vaultVm.OpenSettingsPanelCommand.Execute(Unit.Default).Subscribe();
            }
        }

        private void OnCategoryManagerOverlayBackgroundPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.VaultViewModel vm && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                vm.DismissCategoryManagerPanel();
                e.Handled = true;
            }
        }

        private void OnIconManagerOverlayBackgroundPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.VaultViewModel vm && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                vm.CloseIconManagerCommand.Execute(Unit.Default).Subscribe();
                e.Handled = true;
            }
        }

        private void OnFlaggedOverlayBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultViewModel vm && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                vm.DismissFlaggedPasswordsPanel();
                e.Handled = true;
            }
        }

        private async void OpenPasswordGenerator_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var viewModel = new PasswordGeneratorViewModel();
                var window = new PasswordGeneratorWindow
                {
                    DataContext = viewModel
                };

                await window.ShowDialog(this);

                if (DataContext is VaultViewModel vm)
                {
                    vm.StatusMessage = "Password generator closed";
                }
            });
        }

        private async void OpenThemeSettings_Click(object? sender, RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {

                var app = (App)Application.Current!;
                var themeManager = app.Services!.GetService<ThemeManagerService>();
                var runtimeThemeService = app.Services!.GetService<IRuntimeThemeService>();
                var win = new ThemeSettingsWindow
                {
                    DataContext = new PhantomVault.UI.ViewModels.Settings.ThemeSettingsViewModel(themeManager, runtimeThemeService)
                };
                await win.ShowDialog(this);
            });
        }

        private async System.Threading.Tasks.Task HandleEventAsync(Func<System.Threading.Tasks.Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error in async event handler");

                var dialogService = new DialogService();
                try
                {
                    await dialogService.ShowErrorAsync("Error", $"An error occurred: {ex.Message}", this);
                }
                catch
                {

                    Serilog.Log.Fatal(ex, "Failed to show error dialog");
                }
            }
        }

        private void CloseEditPanel_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseEditPanel();
            }
        }

        private static void LogIconPicker(string msg)
            => Log.Debug("[VaultWindow][IconPicker] {Msg}", msg);

        private async void IconPickerButton_Click(object? sender, RoutedEventArgs e)
        {

            LogIconPicker("IconPickerButton_Click FIRED (Click handler)");
            await OpenIconPickerFromCodeBehind("Click");
        }

        private async void IconSectionBorder_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {

            LogIconPicker("IconSectionBorder_PointerReleased FIRED");

            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
            {
                e.Handled = true;
                await OpenIconPickerFromCodeBehind("PointerReleased");
            }
        }

        private async System.Threading.Tasks.Task OpenIconPickerFromCodeBehind(string source)
        {
            try
            {
                LogIconPicker($"OpenIconPickerFromCodeBehind called from: {source}");

                var editVm = _currentEditViewModel;
                if (editVm == null)
                {
                    LogIconPicker("ERROR: _currentEditViewModel is null!");
                    return;
                }

                LogIconPicker($"Creating IconPickerViewModel with Icon='{editVm.Icon}', Color='{editVm.SelectedIconColor}'");
                var viewModel = new ViewModels.IconPickerViewModel(
                    editVm.Icon, editVm.SelectedIconColor);

                LogIconPicker("Creating IconPickerWindow...");
                var window = new IconPickerWindow
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                viewModel.SetOwnerWindow(window);

                LogIconPicker("Calling ShowDialog...");
                string? result = null;
                try
                {
                    result = await window.ShowDialog<string?>(this);
                }
                catch (Exception showEx)
                {
                    LogIconPicker($"ShowDialog THREW: {showEx.GetType().Name}: {showEx.Message}\n{showEx.StackTrace}");

                    LogIconPicker("Trying Show() as non-modal fallback...");
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
                    window.Closed += (_, _) => tcs.TrySetResult(viewModel.SelectedIcon);
                    window.Show();
                    result = await tcs.Task;
                }

                LogIconPicker($"Dialog result: '{result ?? "null"}'");

                if (!string.IsNullOrEmpty(result))
                {
                    editVm.Icon = result;
                    editVm.SelectedIconColor = viewModel.SelectedIconColor;
                    LogIconPicker($"Icon set to: '{result}'");
                }
            }
            catch (Exception ex)
            {
                LogIconPicker($"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CloseSecurityDashboard_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.ToggleSecurityDashboardCommand.Execute().Subscribe();
            }
        }

        private void WireUpSecurityDashboard()
        {
            var securityDashboard = this.FindControl<SecurityDashboard>("SecurityDashboardControl");
            if (securityDashboard == null || DataContext is not VaultViewModel vm)
                return;

            securityDashboard.Bind(SecurityDashboard.SecurityScoreProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.SecurityScore) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.TotalCredentialsProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.TotalCount) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.WeakPasswordsProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.WeakCredentials.Count) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.BreachedPasswordsProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.BreachedPasswordCount) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.ReusedPasswordsProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.ReusedPasswordCount) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.ExpiringSoonProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.ExpiringCredentialCount) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.TwoFactorEnabledProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.TwoFactorEnabledCount) ?? Observable.Empty<int>()));

            securityDashboard.Bind(SecurityDashboard.LastBreachCheckProperty,
                this.GetObservable(DataContextProperty)
                    .SelectMany(dc => (dc as VaultViewModel)?.WhenAnyValue(v => v.LastBreachCheckTime) ?? Observable.Empty<DateTime?>()));

            securityDashboard.EditCredentialCommand = new SimpleCommand(item =>
            {
                if (item is WeakCredentialItem weakItem)
                {

                    var credentialVm = vm.FilteredCredentials.FirstOrDefault(c => c.Title == weakItem.Title);
                    if (credentialVm != null)
                    {
                        vm.EditCredentialCommand.Execute(credentialVm).Subscribe();
                    }
                }
            });

            var refreshButton = securityDashboard.FindControl<Button>("RefreshButton");
            if (refreshButton != null)
            {
                refreshButton.Click += (s, e) => RefreshSecurityDashboard_Click();
            }
        }

        private void RefreshSecurityDashboard_Click()
        {
            if (DataContext is VaultViewModel vm)
            {

                var wasVisible = vm.IsSecurityDashboardVisible;
                vm.ToggleSecurityDashboardCommand.Execute().Subscribe();
                if (wasVisible)
                {
                    vm.ToggleSecurityDashboardCommand.Execute().Subscribe();
                }
            }
        }

        private void CloseMobileDetailOverlay_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {

                vm.ClearSelectedCredentialCommand.Execute().Subscribe();
            }
        }

        private void CredentialListBox_PointerMoved(object? sender, PointerEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("PointerMoved handler invoked");
#endif
            if (_credentialListBox is null || _credentialHoverHighlight is null || _credentialListHost is null)
            {
                return;
            }

            var pointerPosition = e.GetPosition(_credentialListBox);
            var hitVisual = (_credentialListBox.InputHitTest(pointerPosition) as Visual) ?? (e.Source as Visual);
            var listBoxItem = hitVisual?
                .GetSelfAndVisualAncestors()
                .OfType<ListBoxItem>()
                .FirstOrDefault();

            if (listBoxItem is null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("No list item under pointer");
#endif
                HideCredentialHoverHighlight();
                return;
            }

            var topLeft = listBoxItem.TranslatePoint(new Point(0, 0), _credentialListHost);
            if (topLeft is null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("Unable to translate item position");
#endif
                HideCredentialHoverHighlight();
                return;
            }

            var bounds = listBoxItem.Bounds;

            var barWidth = Math.Max(0, bounds.Width + HoverHighlightHorizontalPadding);
            var barHeight = Math.Max(0, bounds.Height + HoverHighlightVerticalPadding);
            var halfHorizontalPad = HoverHighlightHorizontalPadding / 2;
            var halfVerticalPad = HoverHighlightVerticalPadding / 2;

            var left = Math.Max(topLeft.Value.X - halfHorizontalPad, -halfHorizontalPad);
            var top = Math.Max(topLeft.Value.Y - halfVerticalPad, -halfVerticalPad);

            _credentialHoverHighlight.Width = barWidth;
            _credentialHoverHighlight.Height = barHeight;
            _credentialHoverHighlight.BarWidth = barWidth;
            _credentialHoverHighlight.BarHeight = barHeight;
            _credentialHoverHighlight.Margin = new Thickness(left, top, 0, 0);
            _credentialHoverHighlight.Opacity = 1;
        }

        private void CredentialListBox_PointerExited(object? sender, PointerEventArgs e) => HideCredentialHoverHighlight();

        private void HideCredentialHoverHighlight()
        {
            if (_credentialHoverHighlight is null)
            {
                return;
            }

            _credentialHoverHighlight.Opacity = 0;
        }

        private void OnSettingsPanelOverlayBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseSettingsPanelCommand?.Execute(Unit.Default);
            }
        }

        private void AddEntryButton_Click(object? sender, RoutedEventArgs e)
        {

            if (sender is Button button && button.Flyout != null)
            {
                button.Flyout.ShowAt(button);
            }
        }

        private void UpdateScreenshotProtection(bool enable)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"UpdateScreenshotProtection called with enable={enable}");
#endif
            try
            {
                var platformHandle = this.TryGetPlatformHandle();
                if (platformHandle != null)
                {
                    var hwnd = platformHandle.Handle;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"Window handle obtained: {hwnd}");
                    var wasEnabled = WindowProtectionService.IsScreenshotProtectionEnabled(hwnd);
                    System.Diagnostics.Debug.WriteLine($"Current state BEFORE change: {(wasEnabled ? "PROTECTED" : "UNPROTECTED")}");
#endif
                    if (enable)
                    {
                        if (WindowProtectionService.EnableScreenshotProtection(hwnd))
                        {
#if DEBUG
                            var isEnabled = WindowProtectionService.IsScreenshotProtectionEnabled(hwnd);
                            System.Diagnostics.Debug.WriteLine($"✓ Screenshot protection ENABLED successfully. Verified state: {(isEnabled ? "PROTECTED" : "UNPROTECTED")}");
#endif
                        }
                        else
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine("✗ Failed to enable screenshot protection");
#endif
                        }
                    }
                    else
                    {
                        if (WindowProtectionService.DisableScreenshotProtection(hwnd))
                        {
#if DEBUG
                            var isEnabled = WindowProtectionService.IsScreenshotProtectionEnabled(hwnd);
                            System.Diagnostics.Debug.WriteLine($"✓ Screenshot protection DISABLED successfully. Verified state: {(isEnabled ? "PROTECTED" : "UNPROTECTED")}");
#endif
                        }
                        else
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine("✗ Failed to disable screenshot protection");
#endif
                        }
                    }
                }
                else
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("✗ Could not get platform handle");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Error updating screenshot protection: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        private void OnSettingsChanged(object? sender, UserSettingsChangedEventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateScreenshotProtection(e.Settings.EnableScreenshotProtection));
        }

        private void RecoveryPanel_CloseRequested(object? sender, EventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {

                vm.CloseRecoveryPanel();
            }
        }

        private void RecoveryOverlayBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseRecoveryPanel();
            }
        }

        private void RecoveryPanel_LaunchRequested(object? sender, EventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {

                vm.OpenPhantomRecoveryCommand?.Execute().Subscribe();
            }
        }

        private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not VaultViewModel vm) return;

            RegisterUserActivity();

            if (vm.IsEditPanelVisible) return;

            if (vm.SelectedCredential is null) return;

            var source = e.Source as Visual;
            while (source is not null)
            {

                if (source == _detailCard)
                    return;

                if (source is Button btn && btn.DataContext is PhantomVault.UI.ViewModels.CredentialViewModel)
                    return;

                source = source.GetVisualParent() as Visual;
            }

            vm.SelectedCredential = null;
        }
    }

#pragma warning disable CS0067
    public class SimpleCommand : System.Windows.Input.ICommand
    {
        private readonly Action<object?> _execute;

        public event EventHandler? CanExecuteChanged;

        public SimpleCommand(Action<object?> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
    }
#pragma warning restore CS0067
}

