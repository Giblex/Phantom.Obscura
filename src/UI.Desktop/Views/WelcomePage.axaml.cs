using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Serilog;

namespace PhantomVault.UI.Views
{
    public partial class WelcomePage : ThemeAwareWindow
    {
        private WelcomePageViewModel? _currentViewModel;
        private ScrollViewer? _mainScrollViewer;
        private Control? _vaultListAnchor;
        private Control? _detectedVaultCard;
        private Control? _setupChoiceCard;
        private Control? _actionFooterCard;
        private Control? _detectionStatusCard;
        private Canvas? _detectionTraceCanvas;
        private Border? _detectionTraceLine;
        private Border? _traceTopEdge;
        private Border? _traceLeftEdge;
        private Border? _traceBottomEdge;
        private Border? _traceRightEdge;
        private bool _hasAutoScrolledToUsbActions;
        private bool _hasAutoScrolledToVaultPicker;
        private bool _hasAutoScrolledToSetupChoice;
        private DispatcherTimer? _spinTimer;
        private ConicGradientBrush? _spinRingBrush;

        public WelcomePage()
        {

            ThemeScope.SetIsThemed(this, false);

            InitializeComponent();
            _mainScrollViewer = this.FindControl<ScrollViewer>("MainScrollViewer");
            _vaultListAnchor = this.FindControl<Control>("VaultListAnchor");
            _detectedVaultCard = this.FindControl<Control>("DetectedVaultCard");
            _setupChoiceCard = this.FindControl<Control>("SetupChoiceCard");
            _actionFooterCard = this.FindControl<Control>("ActionFooterCard");
            _detectionStatusCard = this.FindControl<Control>("DetectionStatusCard");
            _detectionTraceCanvas = this.FindControl<Canvas>("DetectionTraceCanvas");
            _detectionTraceLine = this.FindControl<Border>("DetectionTraceLine");
            _traceTopEdge = this.FindControl<Border>("TraceTopEdge");
            _traceLeftEdge = this.FindControl<Border>("TraceLeftEdge");
            _traceBottomEdge = this.FindControl<Border>("TraceBottomEdge");
            _traceRightEdge = this.FindControl<Border>("TraceRightEdge");
            _spinRingBrush = this.FindControl<Border>("SpinRingOuter")?.BorderBrush as ConicGradientBrush;

            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_currentViewModel != null)
            {
                _currentViewModel.NavigateToUsbSetup -= OnNavigateToUsbSetup;
                _currentViewModel.NavigateToSecurityCheck -= OnNavigateToSecurityCheck;
                _currentViewModel.NavigateToSetupWizard -= OnNavigateToSetupWizard;
                _currentViewModel.NavigateToQuickSetup -= OnNavigateToQuickSetup;
                _currentViewModel.DeveloperBypassRequested -= OnDeveloperBypass;
                _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is WelcomePageViewModel viewModel)
            {

                viewModel.SetOwnerWindow(this);

                viewModel.NavigateToUsbSetup += OnNavigateToUsbSetup;
                viewModel.NavigateToSecurityCheck += OnNavigateToSecurityCheck;
                viewModel.NavigateToSetupWizard += OnNavigateToSetupWizard;
                viewModel.NavigateToQuickSetup += OnNavigateToQuickSetup;
                viewModel.DeveloperBypassRequested += OnDeveloperBypass;
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _currentViewModel = viewModel;
                _hasAutoScrolledToUsbActions = false;
                _hasAutoScrolledToVaultPicker = false;
                _hasAutoScrolledToSetupChoice = false;
                UpdateSpinTimer(viewModel.IsDeviceDetectionActive);
            }
            else
            {
                _currentViewModel = null;
                UpdateSpinTimer(false);
            }
        }

        private void UpdateSpinTimer(bool active)
        {
            if (active)
            {
                if (_spinTimer == null)
                {
                    _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    _spinTimer.Tick += OnSpinTimerTick;
                }
                _spinTimer.Start();
            }
            else
            {
                _spinTimer?.Stop();
            }
        }

        private void OnSpinTimerTick(object? sender, EventArgs e)
        {
            if (_spinRingBrush == null)
                return;

            var angle = _spinRingBrush.Angle + 360.0 / 1400.0 * 16.0;
            if (angle >= 360)
                angle -= 360;
            _spinRingBrush.Angle = angle;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_spinTimer != null)
            {
                _spinTimer.Stop();
                _spinTimer.Tick -= OnSpinTimerTick;
                _spinTimer = null;
            }
            base.OnClosed(e);
        }

        /// <summary>
        /// Marshals to the UI thread before doing anything.
        ///
        /// The view model raises PropertyChanged from the USB detection worker, so this
        /// handler — and every scroll animation it starts — used to run on a background
        /// thread. Reading ScrollViewer.Offset from there throws "Call from invalid
        /// thread", which is why the vault-picker auto-scroll silently failed at
        /// startup. Dispatching here fixes all three animations at once, and because
        /// the continuations then capture Avalonia's synchronisation context, the
        /// awaits inside them come back on the UI thread too.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, e));
                return;
            }

            if (_currentViewModel == null)
                return;

            if (e.PropertyName == nameof(WelcomePageViewModel.HasUsbDevice))
            {
                if (_currentViewModel.HasUsbDevice && !_hasAutoScrolledToUsbActions)
                {
                    _hasAutoScrolledToUsbActions = true;
                    _ = AnimateScrollToActionAreaAsync();
                }
                else if (!_currentViewModel.HasUsbDevice)
                {
                    _hasAutoScrolledToUsbActions = false;
                    _hasAutoScrolledToVaultPicker = false;
                }
            }

            if (e.PropertyName == nameof(WelcomePageViewModel.ShowVaultPicker))
            {
                if (_currentViewModel.ShowVaultPicker && !_hasAutoScrolledToVaultPicker)
                {
                    _hasAutoScrolledToVaultPicker = true;
                    _ = AnimateScrollVaultPickerIntoViewAsync();
                }
                else if (!_currentViewModel.ShowVaultPicker)
                {
                    _hasAutoScrolledToVaultPicker = false;
                }
            }

            if (e.PropertyName == nameof(WelcomePageViewModel.IsDeviceDetectionActive))
            {
                UpdateSpinTimer(_currentViewModel.IsDeviceDetectionActive);
            }

            if (e.PropertyName == nameof(WelcomePageViewModel.IsSetupChoiceVisible))
            {
                if (_currentViewModel.IsSetupChoiceVisible && !_hasAutoScrolledToSetupChoice)
                {
                    _hasAutoScrolledToSetupChoice = true;
                    _ = AnimateScrollSetupChoiceIntoViewAsync();
                }
                else if (!_currentViewModel.IsSetupChoiceVisible)
                {
                    _hasAutoScrolledToSetupChoice = false;
                }
            }
        }

        private async Task AnimateScrollToActionAreaAsync()
        {
            try
            {
                if (_mainScrollViewer == null)
                    return;

                var startOffsetY = _mainScrollViewer.Offset.Y;
                await Task.Delay(380);

                var maxOffsetY = Math.Max(0, _mainScrollViewer.Extent.Height - _mainScrollViewer.Viewport.Height);
                var footerLift = Math.Min(96, Math.Max(24, _mainScrollViewer.Viewport.Height * 0.16));
                var targetOffsetY = Math.Max(0, maxOffsetY - footerLift);

                if (_actionFooterCard != null)
                {
                    var footerPoint = _actionFooterCard.TranslatePoint(new Point(0, 0), this);
                    if (footerPoint.HasValue && footerPoint.Value.Y < Bounds.Height - 120)
                    {
                        targetOffsetY = Math.Min(maxOffsetY, startOffsetY + 56);
                    }
                }

                if (targetOffsetY <= startOffsetY + 12)
                    return;

                await AnimateScrollAsync(startOffsetY, targetOffsetY, 900);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-scroll welcome page to action area");
            }
        }

        private async Task AnimateScrollVaultPickerIntoViewAsync()
        {
            try
            {
                if (_mainScrollViewer == null || _detectedVaultCard == null)
                    return;

                var startOffsetY = _mainScrollViewer.Offset.Y;
                await Task.Delay(420);

                var targetPoint = _detectedVaultCard.TranslatePoint(new Point(0, 0), _mainScrollViewer);
                var desiredViewportY = targetPoint?.Y ?? (_mainScrollViewer.Viewport.Height * 0.58);
                var targetOffsetY = Math.Max(0, startOffsetY + desiredViewportY - 190);
                var maxOffsetY = Math.Max(0, _mainScrollViewer.Extent.Height - _mainScrollViewer.Viewport.Height);
                targetOffsetY = Math.Min(maxOffsetY, targetOffsetY);

                if (targetOffsetY <= startOffsetY + 12)
                {
                    await AnimateDetectionTraceAsync();
                    return;
                }

                _ = AnimateDetectionTraceAsync();

                await AnimateScrollAsync(startOffsetY, targetOffsetY, 2200);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-scroll detected vault list into view");
            }
        }

        private async Task AnimateScrollSetupChoiceIntoViewAsync()
        {
            try
            {
                if (_mainScrollViewer == null || _setupChoiceCard == null)
                    return;

                var startOffsetY = _mainScrollViewer.Offset.Y;
                // Allow the card to become visible and lay out before measuring.
                await Task.Delay(220);

                var targetPoint = _setupChoiceCard.TranslatePoint(new Point(0, 0), _mainScrollViewer);
                var desiredViewportY = targetPoint?.Y ?? (_mainScrollViewer.Viewport.Height * 0.5);
                // Scroll a bit further so the Default/Advanced option buttons are
                // comfortably in view, not just the card header.
                var targetOffsetY = Math.Max(0, startOffsetY + desiredViewportY - 24);
                var maxOffsetY = Math.Max(0, _mainScrollViewer.Extent.Height - _mainScrollViewer.Viewport.Height);
                targetOffsetY = Math.Min(maxOffsetY, targetOffsetY);

                if (targetOffsetY <= startOffsetY + 12)
                    return;

                await AnimateScrollAsync(startOffsetY, targetOffsetY, 900);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-scroll welcome page to setup choice area");
            }
        }

        private async Task AnimateScrollAsync(double startOffsetY, double targetOffsetY, int durationMs)
        {
            if (_mainScrollViewer == null)
                return;

            const int frameDelayMs = 16;
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < durationMs)
            {
                var t = stopwatch.ElapsedMilliseconds / (double)durationMs;
                var eased = t < 0.5
                    ? 4d * t * t * t
                    : 1d - Math.Pow(-2d * t + 2d, 3d) / 2d;
                var currentOffsetY = startOffsetY + ((targetOffsetY - startOffsetY) * eased);
                _mainScrollViewer.Offset = new Vector(_mainScrollViewer.Offset.X, currentOffsetY);
                await Task.Delay(frameDelayMs);
            }

            _mainScrollViewer.Offset = new Vector(_mainScrollViewer.Offset.X, targetOffsetY);
        }

        private async Task AnimateDetectionTraceAsync()
        {
            try
            {
                if (_detectionTraceCanvas == null ||
                    _detectionTraceLine == null ||
                    _traceTopEdge == null ||
                    _traceLeftEdge == null ||
                    _traceBottomEdge == null ||
                    _traceRightEdge == null)
                {
                    return;
                }

                _detectionTraceCanvas.IsVisible = true;
                _detectionTraceLine.Height = 0;
                _detectionTraceLine.Opacity = 0.95;
                _traceTopEdge.Width = 0;
                _traceLeftEdge.Height = 0;
                _traceBottomEdge.Width = 0;
                _traceRightEdge.Height = 0;
                _traceTopEdge.Opacity = 0;
                _traceLeftEdge.Opacity = 0;
                _traceBottomEdge.Opacity = 0;
                _traceRightEdge.Opacity = 0;

                Canvas.SetLeft(_detectionTraceLine, 378);
                Canvas.SetTop(_detectionTraceLine, 0);
                Canvas.SetLeft(_traceTopEdge, 110);
                Canvas.SetTop(_traceTopEdge, 92);
                Canvas.SetLeft(_traceLeftEdge, 110);
                Canvas.SetTop(_traceLeftEdge, 92);
                Canvas.SetLeft(_traceBottomEdge, 110);
                Canvas.SetTop(_traceBottomEdge, 127);
                Canvas.SetLeft(_traceRightEdge, 647);
                Canvas.SetTop(_traceRightEdge, 92);

                await AnimateDimensionAsync(_detectionTraceLine, true, 0, 94, 1150);
                await AnimateDimensionAsync(_traceTopEdge, false, 0, 540, 420, 1);
                await AnimateDimensionAsync(_traceLeftEdge, true, 0, 38, 190, 1);
                await AnimateDimensionAsync(_traceBottomEdge, false, 0, 540, 420, 1);
                await AnimateDimensionAsync(_traceRightEdge, true, 0, 38, 190, 1);
                await Task.Delay(260);

                const int fadeDurationMs = 360;
                const int fadeFrameMs = 16;
                var fade = Stopwatch.StartNew();
                while (fade.ElapsedMilliseconds < fadeDurationMs)
                {
                    var opacity = 1d - (fade.ElapsedMilliseconds / (double)fadeDurationMs);
                    _detectionTraceLine.Opacity = opacity;
                    _traceTopEdge.Opacity = opacity;
                    _traceLeftEdge.Opacity = opacity;
                    _traceBottomEdge.Opacity = opacity;
                    _traceRightEdge.Opacity = opacity;
                    await Task.Delay(fadeFrameMs);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to animate vault detection trace");
            }
            finally
            {
                if (_detectionTraceCanvas != null)
                    _detectionTraceCanvas.IsVisible = false;
            }
        }

        private static async Task AnimateDimensionAsync(Border border, bool animateHeight, double from, double to, int durationMs, double opacity = 0.95)
        {
            const int frameDelayMs = 16;
            var stopwatch = Stopwatch.StartNew();
            border.Opacity = opacity;

            while (stopwatch.ElapsedMilliseconds < durationMs)
            {
                var t = stopwatch.ElapsedMilliseconds / (double)durationMs;
                var eased = 1d - Math.Pow(1d - t, 3d);
                var current = from + ((to - from) * eased);

                if (animateHeight)
                {
                    border.Height = current;
                }
                else
                {
                    border.Width = current;
                }

                await Task.Delay(frameDelayMs);
            }

            if (animateHeight)
            {
                border.Height = to;
            }
            else
            {
                border.Width = to;
            }
        }

        private void DetectedVaultChoice_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DetectedVaultLaunchRequest launchRequest)
                return;

            if (DataContext is WelcomePageViewModel viewModel)
            {
                viewModel.SelectDetectedVault(launchRequest);
                // Fire the same command the "Open" button uses so clicking the
                // active slot directly enters the vault-open flow.
                viewModel.OpenExistingVaultCommand.Execute().Subscribe();
            }
        }

        private void OnNavigateToSetupWizard(object? sender, EventArgs e)
        {
            try
            {
                var setupWizard = new SetupWizardWindow();
                var wizardVm = setupWizard.DataContext as SetupWizardViewModel;

                if (wizardVm != null)
                {
                    wizardVm.VaultReadyForCreation += async (s, _) =>
                    {
                        try
                        {

                            var loadingWindow = new VaultCreationLoadingWindow();
                            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                                dt.MainWindow = loadingWindow;
                            loadingWindow.Show();
                            setupWizard.Hide();

                            EventHandler? completedHandler = null;
                            completedHandler = (lw, _) =>
                            {
                                loadingWindow.CreationCompleted -= completedHandler;
                                try
                                {
                                    loadingWindow.Close();
                                    setupWizard.Close();
                                    if (!string.IsNullOrWhiteSpace(wizardVm.SelectedUsbPath))
                                    {
                                        OnNavigateToSecurityCheck(this, new DetectedVaultLaunchRequest
                                        {
                                            UsbPath = NormalizeDriveRoot(wizardVm.SelectedUsbPath),
                                            UsbDisplayName = wizardVm.SelectedUsbPath,
                                            VaultPath = NormalizeDriveRoot(wizardVm.SelectedUsbPath),
                                            DisplayName = "Newly created vault"
                                        });
                                    }
                                    else
                                    {
                                        OpenVaultWindowWithPreferencePrompt();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Failed to transition after vault creation");
                                    this.Show();
                                }
                            };
                            loadingWindow.CreationCompleted += completedHandler;
                            loadingWindow.CreationCancelled += (_, _) =>
                            {
                                loadingWindow.Close();
                                setupWizard.Close();
                                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                                    desktop.MainWindow = this;
                                this.Show();
                            };

                            await loadingWindow.RunCreationAsync(wizardVm);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Vault creation loading window failed");
                            setupWizard.Show();
                        }
                    };
                }

                bool vaultCreationStarted = false;

                if (wizardVm != null)
                    wizardVm.VaultReadyForCreation += (_, __) => vaultCreationStarted = true;

                setupWizard.Closed += (s, args) =>
                {
                    if (!vaultCreationStarted)
                    {
                        this.Show();
                    }

                };

                setupWizard.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open setup wizard");
                var dialogService = new DialogService();
                _ = dialogService.ShowErrorAsync(
                    "Setup Wizard Error",
                    "The setup wizard could not be opened. Try again.",
                    this);
            }
        }

        private void OnNavigateToQuickSetup(object? sender, EventArgs e)
        {
            try
            {
                var quickVm = new SetupWizardViewModel();
                quickVm.ConfigureForQuickSetup();
                var quickWindow = new QuickSetupWindow(quickVm);
                bool vaultCreationStarted = false;

                quickVm.VaultReadyForCreation += async (s, _) =>
                {
                    vaultCreationStarted = true;
                    try
                    {
                        var loadingWindow = new VaultCreationLoadingWindow();
                        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                            dt.MainWindow = loadingWindow;

                        loadingWindow.Show();
                        quickWindow.Hide();

                        loadingWindow.CreationCompleted += (lw, _) =>
                        {
                            try
                            {
                                loadingWindow.Close();
                                quickWindow.Close();
                                if (quickVm.SelectedStorageLocation == "USB" && !string.IsNullOrWhiteSpace(quickVm.SelectedUsbPath))
                                {
                                    OnNavigateToSecurityCheck(this, new DetectedVaultLaunchRequest
                                    {
                                        UsbPath = NormalizeDriveRoot(quickVm.SelectedUsbPath),
                                        UsbDisplayName = quickVm.SelectedUsbPath,
                                        VaultPath = NormalizeDriveRoot(quickVm.SelectedUsbPath),
                                        DisplayName = quickVm.EffectiveVaultName
                                    });
                                }
                                else
                                {
                                    OpenVaultWindowWithPreferencePrompt();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Failed to transition after quick setup vault creation");
                                this.Show();
                            }
                        };
                        loadingWindow.CreationCancelled += (_, _) =>
                        {
                            loadingWindow.Close();
                            quickWindow.Close();
                            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                                desktop.MainWindow = this;
                            this.Show();
                        };

                        await loadingWindow.RunCreationAsync(quickVm);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Quick setup loading flow failed");
                        quickWindow.Show();
                    }
                };

                quickWindow.Closed += (_, _) =>
                {
                    if (!vaultCreationStarted)
                    {
                        this.Show();
                    }
                };

                quickWindow.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open quick setup");
                var dialogService = new DialogService();
                _ = dialogService.ShowErrorAsync(
                    "Default Setup Error",
                    "Default setup could not be opened. Try again.",
                    this);
            }
        }

        private async void OpenVaultWindowWithPreferencePrompt()
        {
            try
            {
                if (Application.Current is not App app || app.Services == null)
                    return;

                var services = app.Services;

                var vaultViewModel = services.GetRequiredService<VaultViewModel>();
                var vaultWindow = new VaultWindow { DataContext = vaultViewModel };

                var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                vaultLockDurationService.SetActiveSession(null);

                // Hiding this page (below) doesn't stop its background USB scan timer —
                // it'd keep polling every 2 seconds behind the vault window indefinitely.
                if (DataContext is WelcomePageViewModel welcomeViewModel)
                {
                    welcomeViewModel.StopScanning();
                }

                vaultViewModel.SetOwnerWindow(vaultWindow);
                vaultWindow.Show();

                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                    dt.MainWindow = vaultWindow;

                this.Hide();

                var settings = SettingsService.Load();

                if (string.IsNullOrEmpty(settings.VaultUnlockPreference))
                {

                    await Task.Delay(600);

                    var preferenceDialog = new VaultUnlockPreferenceDialog();
                    var result = await preferenceDialog.ShowDialog<string?>(vaultWindow);

                    if (!string.IsNullOrEmpty(result))
                    {
                        settings.VaultUnlockPreference = result;

                        switch (result)
                        {
                            case "Pin":

                                settings.DefaultUsePasskey = false;
                                break;
                            case "WindowsHello":
                                settings.DefaultUsePasskey = true;
                                settings.EnablePinLock = false;
                                break;
                            case "Automatic":
                                settings.DefaultUsePasskey = false;
                                settings.EnablePinLock = false;
                                break;
                        }

                        SettingsService.Save(settings);

                        if (result == "Pin")
                        {
                            var pinDialog = new PhantomVault.UI.Views.Dialogs.PinSetupDialog();
                            await pinDialog.ShowDialog(vaultWindow);
                            if (pinDialog.DataContext is PhantomVault.UI.ViewModels.Dialogs.PinSetupDialogViewModel pinVm
                                && pinVm.Success)
                            {
                                var pinSettings = SettingsService.Load();
                                pinSettings.EnablePinLock = true;
                                SettingsService.Save(pinSettings);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open vault window after creation");
                this.Show();
            }
        }

        private static string NormalizeDriveRoot(string usbPathDisplay)
        {
            if (string.IsNullOrWhiteSpace(usbPathDisplay))
                return string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(
                usbPathDisplay, @"\(([A-Za-z]:)\)");
            if (match.Success)
            {
                var driveLetter = match.Groups[1].Value;
                if (!driveLetter.EndsWith("\\", StringComparison.Ordinal))
                    driveLetter += "\\";
                return driveLetter;
            }

            if (usbPathDisplay.Contains(" - ", StringComparison.Ordinal))
            {
                var legacyRoot = usbPathDisplay.Split(" - ")[0].Trim();
                if (!legacyRoot.EndsWith("\\", StringComparison.Ordinal))
                    legacyRoot += "\\";
                return legacyRoot;
            }

            var fallback = usbPathDisplay.Length >= 2 ? usbPathDisplay.Substring(0, 3) : usbPathDisplay;
            if (!fallback.EndsWith("\\", StringComparison.Ordinal))
                fallback += "\\";
            return fallback;
        }

        private void OnNavigateToUsbSetup(object? sender, EventArgs e)
        {

            var usbDetector = new UsbDetector();
            var usbSetupViewModel = new UsbSetupViewModel(usbDetector);

            usbSetupViewModel.NavigateBack += (s, args) =>
            {

                this.Show();
                if (s is UsbSetupViewModel vm && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        if (window is UsbSetupWindow)
                        {
                            window.Close();
                            break;
                        }
                    }
                }
            };

            usbSetupViewModel.NavigateToContinue += (s, usbPath) =>
            {

                var app = (App)Application.Current!;
                var provisionViewModel = ActivatorUtilities.CreateInstance<ProvisionViewModel>(app.Services!, usbPath);

                provisionViewModel.NavigateToVault += (s, usbDrivePath) =>
                {

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {

                        OnNavigateToSecurityCheck(s, new DetectedVaultLaunchRequest
                        {
                            UsbPath = usbDrivePath,
                            UsbDisplayName = usbDrivePath,
                            VaultPath = usbDrivePath,
                            DisplayName = "Provisioned vault"
                        });

                        foreach (var window in Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop2 ? desktop2.Windows : Array.Empty<Window>())
                        {
                            if (window is ProvisionWindow)
                            {
                                window.Close();
                                break;
                            }
                        }
                    });
                };

                var provisionWindow = new ProvisionWindow
                {
                    DataContext = provisionViewModel
                };

                provisionViewModel.SetOwnerWindow(provisionWindow);
                provisionWindow.Show();

                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        if (window is UsbSetupWindow)
                        {
                            window.Close();
                            break;
                        }
                    }
                }
            };

            var usbSetupWindow = new UsbSetupWindow
            {
                DataContext = usbSetupViewModel
            };

            usbSetupViewModel.SetOwnerWindow(usbSetupWindow);

            usbSetupWindow.Show();
            this.Hide();
        }

        private void OnNavigateToSecurityCheck(object? sender, DetectedVaultLaunchRequest request)
        {

            var encryptionService = new EncryptionService();
            var containerService = new PhantomContainerService(encryptionService);
            var manifestService = new ManifestService(encryptionService, containerService);

            BlackSecureRawVolumeService? rawVolumeService = null;
            if (Application.Current is App app && app.Services != null)
                rawVolumeService = app.Services.GetService<BlackSecureRawVolumeService>();

            var securityCheckService = new SecurityCheckService(manifestService, yubiKeyService: null, rawVolumeService: rawVolumeService);

            var securityCheckScreen = new SecurityCheckScreen(securityCheckService, request);
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                dt.MainWindow = securityCheckScreen;
            securityCheckScreen.Show();
            this.Hide();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnDeveloperBypass(object? sender, EventArgs e)
        {
#if DEBUG
            try
            {
                if (Application.Current is not App app || app.Services == null)
                {
                    return;
                }

                var services = app.Services;
                var vaultViewModel = services.GetRequiredService<VaultViewModel>();
                var vaultWindow = new VaultWindow
                {
                    DataContext = vaultViewModel
                };

                var vaultLockDurationService = services.GetRequiredService<VaultLockDurationService>();
                vaultLockDurationService.SetActiveSession(null);

                // Hiding this page (below) doesn't stop its background USB scan timer —
                // it'd keep polling every 2 seconds behind the vault window indefinitely.
                if (DataContext is WelcomePageViewModel welcomeViewModel)
                {
                    welcomeViewModel.StopScanning();
                }

                vaultViewModel.SetOwnerWindow(vaultWindow);
                vaultViewModel.SetDeveloperBypassMode(true);
                vaultViewModel.VaultName = "Developer Vault";
                vaultViewModel.StatusMessage = "Developer bypass active (no vault mounted).";

                // Dev bypass skips real vault provisioning, so there's no license token to
                // verify — default to Premium locally so premium-gated UI is testable without
                // going through checkout every time.
                vaultViewModel.EntitlementService?.ForceStatus(
                    PhantomVault.Core.Models.Licensing.LicenseStatus.Premium());

                // No vault is mounted, so seed one sample entry of every credential type
                // (password, WiFi, identity, API key, contact, credit card, bank account,
                // TOTP, PIN) so the UI has something to look at while testing.
                vaultViewModel.LoadSampleCredentials();

                vaultWindow.Closed += (_, _) => this.Show();

                vaultWindow.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                try
                {
                    var dumpPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PhantomVault_DevException.txt");
                    System.IO.File.WriteAllText(dumpPath, ex.ToString());
                }
                catch (Exception dumpEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WelcomePage] Failed to write exception dump: {dumpEx.Message}");
                }

                var dialogService = new DialogService();
                _ = dialogService.ShowErrorAsync(
                    "Developer Bypass Failed",
                    $"Unable to open the vault window for development testing:\n{ex}\n\nFull exception written to your Desktop as 'PhantomVault_DevException.txt' (if possible).",
                    this);
            }
#endif
        }

        private async void About_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var about = new AboutWindow();
                await about.ShowDialog(this);
            });
        }

        private void Settings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var window = new VaultSettingsWindow
            {
                DataContext = new VaultSettingsViewModel()
            };
            if (window.DataContext is VaultSettingsViewModel vm) vm.SetOwnerWindow(window);
            window.Show();
        }

        private async void Import_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var importViewModel = new ImportViewModel();
                var import = new ImportWindow(importViewModel);
                import.ImportCompleted += async (_, credentials) =>
                {
                    PendingImportStagingService.SavePendingImports(credentials);
                    await new DialogService().ShowInfoAsync(
                        "Imports Staged",
                        $"{credentials.Count} credential(s) were staged and will be imported into your vault after the next successful unlock.",
                        this);
                };
                await import.ShowDialog(this);
            });
        }

        private async void Export_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var export = new ExportWindow();
                await export.ShowDialog(this);
            });
        }

        private async void Recover_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleEventAsync(async () =>
            {
                var recover = new Dialogs.RecoverKeyfileDialog();
                await recover.ShowDialog(this);
            });
        }

        private async Task HandleEventAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in async event handler");
                var dialogService = new DialogService();
                try
                {
                    Log.Error(ex, "[WelcomePage] Command failed unexpectedly.");
                    await dialogService.ShowErrorAsync("Error", "The operation could not be completed. Try again.", this);
                }
                catch
                {
                    Log.Fatal(ex, "Failed to show error dialog");
                }
            }
        }
    }
}

