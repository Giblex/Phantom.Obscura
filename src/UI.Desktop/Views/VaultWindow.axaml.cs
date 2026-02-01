using System;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Controls;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Avalonia.Media;

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
        private VaultViewModel? _currentVaultViewModel;
        private AddEditCredentialViewModel? _currentEditViewModel;

        public VaultWindow()
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("VaultWindow constructor start");
#endif
            InitializeComponent();

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

            // Debug instrumentation: find the Add toggle and popup, log their presence and hook toggle events
            try
            {
                var debugLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "VaultWindow_debug.log");
                var addToggle = this.FindControl<ToggleButton>("AddToggle");
                var addPopup = this.FindControl<Avalonia.Controls.Primitives.Popup>("AddPopup");
                System.IO.File.AppendAllText(debugLogPath, $"[{DateTime.Now:O}] VaultWindow constructed. Found AddToggle={addToggle != null}, AddPopup={addPopup != null}\n");

                if (addToggle is not null)
                {
                    // Use property observable to avoid depending on a specific event signature
                    try
                    {
                        addToggle.GetObservable(ToggleButton.IsCheckedProperty).Subscribe(isChecked =>
                        {
                            try
                            {
                                var line = $"[{DateTime.Now:O}] AddToggle.IsChecked -> IsChecked={isChecked}, PopupIsOpen={addPopup?.IsOpen}\n";
                                System.IO.File.AppendAllText(debugLogPath, line);
                            }
                            catch { }
                        });
                    }
                    catch (Exception)
                    {
                        // ignore observable subscription failures
                    }
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "VaultWindow_debug.log"), $"[{DateTime.Now:O}] VaultWindow instrumentation error: {ex}\n"); } catch { }
            }

            // Wire up HeaderView events
            var headerView = this.FindControl<Views.HeaderView>("HeaderView");
            if (headerView != null)
            {
                headerView.PasswordGeneratorRequested += (s, e) => OpenPasswordGenerator_Click(s, e);
                headerView.SettingsRequested += (s, e) => OpenSettings_Click(s, e);
            }

            // Enable screenshot protection when window is opened
            this.Opened += VaultWindow_Opened;

            // Wire up RecoveryPanel close event
            var recoveryPanel = this.FindControl<Views.RecoveryPanel>("RecoveryPanelControl");
            if (recoveryPanel != null)
            {
                recoveryPanel.CloseRequested += RecoveryPanel_CloseRequested;
            }

            // Watch for DataContext changes so we can respond when the EditViewModel is set/updated
            this.DataContextChanged += VaultWindow_DataContextChanged;
            
            // Watch for window size changes to auto-collapse sidebar on small screens
            this.GetObservable(BoundsProperty).Subscribe(bounds =>
            {
                if (DataContext is VaultViewModel vm)
                {
                    vm.UpdateSidebarVisibility(bounds.Width);
                }
            });
#if DEBUG
            System.Diagnostics.Debug.WriteLine("VaultWindow constructor end");
#endif
        }

        private void VaultWindow_Opened(object? sender, EventArgs e)
        {
            // Apply screenshot protection to prevent screen capture of vault contents
            // This makes the window appear black in screenshots, screen recordings, and screen sharing
            // Check user settings and apply the appropriate state
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
                            // Explicitly disable if user has it turned off
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
                _ = ex; // Suppress unused variable warning
#endif
            }
        }

        private void VaultWindow_DataContextChanged(object? sender, EventArgs e)
        {
            if (_currentVaultViewModel != null)
            {
                _currentVaultViewModel.PropertyChanged -= VaultViewModel_PropertyChanged;
            }

            if (DataContext is ViewModels.VaultViewModel vm)
            {
                _currentVaultViewModel = vm;
                _currentVaultViewModel.PropertyChanged += VaultViewModel_PropertyChanged;
                HandleEditViewModelChanged(vm.EditViewModel);

                try
                {
                    var debugLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "VaultWindow_debug.log");
                    var filtered = vm.FilteredCredentials?.Count ?? 0;
                    var sel = vm.SelectedSettingsContent?.ToString() ?? "<null>";
                    System.IO.File.AppendAllText(debugLogPath, $"[{DateTime.Now:O}] DataContext attached. Filtered={filtered}, SelectedSettingsContent={sel}\n");
                    
                    // (FilteredCount changes will be logged via PropertyChanged handler)
                }
#pragma warning disable CA1031 // Catch general exception - debug logging should not crash UI
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

        private void VaultViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is ViewModels.VaultViewModel vm && e.PropertyName == nameof(vm.EditViewModel))
            {
                HandleEditViewModelChanged(vm.EditViewModel);
            }

            // Handle screenshot protection toggle
            if (sender is ViewModels.VaultViewModel vm3 && e.PropertyName == nameof(vm3.EnableScreenshotProtection))
            {
                // Capture the value immediately to avoid re-reading from disk
                var enableProtection = vm3.EnableScreenshotProtection;
                var msg = $"[VaultWindow] PropertyChanged: EnableScreenshotProtection = {enableProtection}";
                Console.WriteLine(msg);
                System.IO.File.AppendAllText("O:\\screenshot_log.txt", msg + "\n");
                // Ensure UI thread execution
                Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateScreenshotProtection(enableProtection));
            }

            // Log when FilteredCount updates so we can see when ApplyFilters populates the UI
            try
            {
                if (sender is ViewModels.VaultViewModel vm2 && e.PropertyName == nameof(vm2.FilteredCount))
                {
                    var debugLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "VaultWindow_debug.log");
                    System.IO.File.AppendAllText(debugLogPath, $"[{DateTime.Now:O}] PropertyChanged: FilteredCount -> {vm2.FilteredCount}\n");
                }
            }
#pragma warning disable CA1031 // Catch general exception - debug logging should not crash UI
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
                // When an Is*Entry flag becomes true, scroll its panel into view
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

                // Run on the UI thread at Render priority so the control has been measured/arranged.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // Wait briefly (up to ~200ms) for the panel to become visible/measured
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
                        // First, request the standard BringIntoView so layout can perform any built-in adjustments
                        panel.BringIntoView();

                        // Additionally attempt a small manual vertical offset so the panel appears slightly below the top
                        // of the edit viewport (makes it more centered/comfortable to read).
                        try
                        {
                            if (pt.HasValue && _editScrollViewer is not null)
                            {
                                // desiredTopMargin: how many pixels from the top of the viewport we want the panel to appear
                                const double desiredTopMargin = 80.0;

                                // Current scroll offset (Y) - use Offset if available
                                double currentOffsetY = 0;
                                try
                                {
                                    currentOffsetY = _editScrollViewer.Offset.Y;
                                }
                                catch { /* ignore if Offset not available */ }

                                var desired = currentOffsetY + pt.Value.Y - desiredTopMargin;
                                if (desired < 0) desired = 0;

                                try
                                {
                                    // Try setting the ScrollViewer offset directly (some Avalonia versions expose Offset setter).
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
                                        _ = exSet; // suppress unused variable warning in Release
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

                        // Try to focus a known named control for this panel (more reliable than searching descendants)
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

                            // Fallback: find first focusable descendant
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

                                // Flash the surrounding border if present to provide a visible confirmation
                                try
                                {
                                    var borderName = panel.Name + "Border";
                                    var border = this.FindControl<Border>(borderName);
                                    if (border != null)
                                    {
                                        var original = border.BorderBrush;
                                        border.BorderBrush = new SolidColorBrush(Colors.Orange);
                                        await System.Threading.Tasks.Task.Delay(400).ConfigureAwait(false);
                                        // restore on UI thread
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
            // Simply close this window
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

            // Open settings panel overlay instead of window
            vaultVm.OpenSettingsPanelCommand.Execute(Unit.Default).Subscribe();
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

                // Update status after generator closed
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
                // Open ThemeSettings with ThemeSettingsViewModel for runtime theme selection
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

        /// <summary>
        /// Safely handles async event handlers with proper error handling.
        /// Prevents exceptions from crashing the application.
        /// </summary>
        private async System.Threading.Tasks.Task HandleEventAsync(Func<System.Threading.Tasks.Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error in async event handler");

                // Show error to user
                var dialogService = new DialogService();
                try
                {
                    await dialogService.ShowErrorAsync("Error", $"An error occurred: {ex.Message}", this);
                }
                catch
                {
                    // If we can't show dialog, at least log it
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

        private void CloseSecurityDashboard_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.ToggleSecurityDashboardCommand.Execute().Subscribe();
            }
        }

        private void CloseMobileDetailOverlay_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                // Clear the selection to close the mobile overlay
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
            // Open the flyout when button is clicked
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

        private void RecoveryPanel_CloseRequested(object? sender, EventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                // Directly close the recovery panel
                vm.CloseRecoveryPanel();
            }
        }
    }
}
