using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using System.ComponentModel;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Linq;

namespace PhantomVault.UI.Views
{
    public partial class IconManagerWindow : ThemeAwareWindow
    {
        /// <summary>Tracks the last Button clicked whose DataContext is an IconFileEntryViewModel.</summary>
        private Button? _lastClickedIconButton;

        public IconManagerWindow()
        {
            InitializeComponent();

            // Capture every Button.Click that bubbles through the window so we know
            // exactly which button was pressed (for popup anchoring).
            AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);

            DataContextChanged += (_, _) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[ICON-MGR-WIN] DataContextChanged");
#endif
                if (DataContext is IconManagerViewModel vm)
                {
                    if (!vm.HasOwnerWindow)
                    {
                        vm.SetOwnerWindow(this);
                    }

                    vm.PropertyChanged -= Vm_PropertyChanged;
                    vm.PropertyChanged += Vm_PropertyChanged;
                }
            };

            // Close variant popup on Escape
            this.KeyDown += IconManagerWindow_KeyDown;

            Opened += (_, _) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[ICON-MGR-WIN] Opened");
#endif
                if (this.FindControl<TextBox>("SearchBox") is { } searchBox)
                {
                    searchBox.Focus();
                }
            };

            Closed += (_, _) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[ICON-MGR-WIN] Closed");
#endif
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>Captures every button click so we can reliably anchor the popup.</summary>
        private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button btn && btn.DataContext is IconFileEntryViewModel)
            {
                _lastClickedIconButton = btn;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not IconManagerViewModel vm) return;

            if (e.PropertyName == nameof(IconManagerViewModel.IsVariantPopupOpen))
            {
                try
                {
                    var popup = this.FindControl<Popup>("VariantPopup");
                    if (popup == null) return;

                    if (vm.IsVariantPopupOpen)
                    {
                        // Find the item container for the clicked icon across all ItemsControls
                        Control? container = null;
                        if (vm.VariantOwnerIcon != null)
                        {
                            container = FindOwnerContainer(vm.VariantOwnerIcon);
                        }

                        if (container != null)
                        {
                            popup.PlacementTarget = container;
                            popup.Placement = PlacementMode.Bottom;
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[ICON-MGR] VariantPopup anchored to: {vm.VariantOwnerIcon?.Name ?? "(unknown)"}");
#endif
                        }

                        popup.IsOpen = true;
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[ICON-MGR] VariantPopup opened: {vm.VariantOwnerIcon?.Name ?? "(unknown)"}");
#endif
                        popup.Closed -= Popup_Closed;
                        popup.Closed += Popup_Closed;
                    }
                    else
                    {
                        popup.IsOpen = false;
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("[ICON-MGR] VariantPopup closed");
#endif
                    }
                }
#pragma warning disable CA1031
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ICON-MGR] PropertyChanged handler error: {ex.Message}");
                }
#pragma warning restore CA1031
            }
        }

        /// <summary>
        /// Returns the Button that was just clicked for the target icon.
        /// Uses the tracked _lastClickedIconButton for reliable anchoring,
        /// regardless of whether the click came from TopIconsList or the grid.
        /// </summary>
        private Control? FindOwnerContainer(IconFileEntryViewModel target)
        {
            // Best: use the button we captured from the Click event
            if (_lastClickedIconButton != null && _lastClickedIconButton.DataContext == target)
                return _lastClickedIconButton;

            // Fallback: search the visual tree
            return FindButtonByDataContextInTree(this, target);
        }

        private static Control? FindButtonByDataContextInTree(Visual root, object target)
        {
            foreach (var child in root.GetVisualChildren())
            {
                if (child is Button btn && btn.DataContext == target)
                    return btn;
                if (child is Visual v)
                {
                    var found = FindButtonByDataContextInTree(v, target);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void Popup_Closed(object? sender, EventArgs e)
        {
            if (DataContext is IconManagerViewModel vm)
            {
                if (vm.IsVariantPopupOpen)
                {
                    vm.IsVariantPopupOpen = false;
                }
            }
        }

        private void IconManagerWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                if (DataContext is IconManagerViewModel vm && vm.IsVariantPopupOpen)
                {
                    vm.IsVariantPopupOpen = false;
                    e.Handled = true;
                }
            }

            // Handle keyboard navigation for variant popup
            try
            {
                if (DataContext is IconManagerViewModel vm && vm.IsVariantPopupOpen)
                {
                    var popup = this.FindControl<Popup>("VariantPopup");
                    if (popup != null && popup.IsOpen)
                    {
                        if (e.Key == Avalonia.Input.Key.Left)
                        {
                            vm.SelectPreviousVariant();
                            FocusVariantButton(vm.SelectedVariantIndex);
                            e.Handled = true;
                            return;
                        }
                        if (e.Key == Avalonia.Input.Key.Right)
                        {
                            vm.SelectNextVariant();
                            FocusVariantButton(vm.SelectedVariantIndex);
                            e.Handled = true;
                            return;
                        }
                        if (e.Key == Avalonia.Input.Key.Enter)
                        {
                            // Apply currently selected variant
                            _ = vm.ApplySelectedVariantAsync();
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconManager] KeyDown handler error: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private Control? FindContainerByDataContext(ItemsControl list, object dataContext)
        {
            foreach (var child in list.GetLogicalChildren())
            {
                if (child is Control c && c.DataContext == dataContext) return c;
                if (child is ILogical il)
                {
                    foreach (var gc in il.GetLogicalChildren())
                    {
                        if (gc is Control gcControl && gcControl.DataContext == dataContext) return gcControl;
                    }
                }
            }
            return null;
        }

        private void FocusVariantButton(int index)
        {
            try
            {
                var variants = this.FindControl<ItemsControl>("VariantItemsControl");
                if (variants == null) return;
                var items = variants.Items?.Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
                if (index < 0 || index >= items.Count) return;
                Control? container = null;
                try { container = variants.ContainerFromIndex(index) as Control; }
#pragma warning disable CA1031
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IconManager] ContainerFromIndex failed: {ex.Message}");
                    container = null;
                }
#pragma warning restore CA1031
                if (container == null)
                {
                    container = FindContainerByDataContext(variants, items[index]);
                }

                if (container != null)
                {
                    // Find a Button descendant and focus it
                    var btn = FindButtonInContainer(container);
                    btn?.Focus();
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ICON-MGR] FocusVariantButton error: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private Button? FindButtonInContainer(Control node)
        {
            foreach (var v in node.GetVisualChildren())
            {
                if (v is Button b) return b;
                if (v is Control c)
                {
                    var nested = FindButtonInContainer(c);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
    }
}
