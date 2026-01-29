using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using System.ComponentModel;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.Shapes;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Linq;

namespace PhantomVault.UI.Views
{
    public partial class IconManagerWindow : ThemeAwareWindow
    {
        public IconManagerWindow()
        {
            InitializeComponent();

            DataContextChanged += (_, _) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[ICON-MGR-WIN] DataContextChanged");
#endif
                if (DataContext is IconManagerViewModel vm)
                {
                    // Only set the owner here if the view model hasn't been initialized with an owner yet.
                    if (!vm.HasOwnerWindow)
                    {
                        vm.SetOwnerWindow(this);
                    }

                    // Subscribe to IsVariantPopupOpen changes so we can open the popup anchored to the clicked item
                    vm.PropertyChanged -= Vm_PropertyChanged; // ensure not double-subscribed
                    vm.PropertyChanged += Vm_PropertyChanged;
                }
            };

            // Close variant popup on Escape
            this.KeyDown += IconManagerWindow_KeyDown;
            // Close variant popup when clicking outside
            this.PointerPressed += IconManagerWindow_PointerPressed;

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
                        // Find the item container corresponding to the VariantOwnerIcon
                        var topList = this.FindControl<ItemsControl>("TopIconsList");
                        if (topList != null && vm.VariantOwnerIcon != null)
                        {
                            // Try to get the container directly from the generator
                            var container = null as Control;
                            var items = topList.Items?.Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
                            var idx = items.IndexOf(vm.VariantOwnerIcon);
                            if (idx >= 0)
                            {
                                try
                                {
                                    // Use ItemsControl.ContainerFromIndex to avoid obsolete API
                                    container = topList.ContainerFromIndex(idx) as Control;
                                }
                                catch { container = null; }
                            }

                            if (container == null)
                            {
                                // As a fallback, search the logical tree for a control whose DataContext matches
                                container = FindContainerByDataContext(topList, vm.VariantOwnerIcon);
                            }

                            if (container != null)
                            {
                                popup.PlacementTarget = container;
                                // Default placement below the anchor; we'll open and then measure to decide whether to flip
                                popup.Placement = PlacementMode.Bottom;

                                // Show caret initially as pointing up (popup below anchor)
                                var caretTop = this.FindControl<Polygon>("CaretTop");
                                var caretBottom = this.FindControl<Polygon>("CaretBottom");
                                if (caretTop != null && caretBottom != null)
                                {
                                    caretTop.IsVisible = true;
                                    caretBottom.IsVisible = false;
                                }
#if DEBUG
                                System.Diagnostics.Debug.WriteLine($"[ICON-MGR] VariantPopup opening for: {vm.VariantOwnerIcon?.Name ?? "(unknown)"} tentativePlacement=Bottom");
#endif

                                // Open popup then measure/adjust placement and caret horizontally to center over anchor.
                                // Schedule measurement after layout has a chance to run.
                                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        // Small delay to allow popup content to measure
                                        await Task.Delay(40).ConfigureAwait(false);
                                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                                        var popupChild = popup.Child as Control;
                                        if (popupChild != null && container != null)
                                        {
                                            var popupTopLeft = popupChild.TranslatePoint(new Avalonia.Point(0, 0), this);
                                            var anchorCenter = container.TranslatePoint(new Avalonia.Point(container.Bounds.Width / 2.0, container.Bounds.Height / 2.0), this);
                                            if (popupTopLeft != null && anchorCenter != null)
                                            {
                                                // If popup extends below window when placed bottom, flip to top
                                                var popupBottom = popupTopLeft.Value.Y + popupChild.Bounds.Height;
                                                var windowBottom = this.Bounds.Height - 24; // keep some padding
                                                if (popupBottom > windowBottom)
                                                {
                                                    // Flip to top
                                                    popup.IsOpen = false;
                                                    popup.Placement = PlacementMode.Top;
                                                    // reopen to reflow
                                                    popup.IsOpen = true;
                                                    // allow measure again
                                                    await Task.Delay(20).ConfigureAwait(false);
                                                }

                                                // Recompute measurements after potential flip
                                                popupTopLeft = (popup.Child as Control)?.TranslatePoint(new Avalonia.Point(0, 0), this);
                                                popupChild = popup.Child as Control;
                                                if (popupTopLeft != null && popupChild != null)
                                                {
                                                    var anchorCx = anchorCenter.Value.X;
                                                    var popupLeft = popupTopLeft.Value.X;
                                                    var offset = anchorCx - popupLeft - 8.0; // half caret width (approx 16/2)
                                                    // Bound offset within popup content area
                                                    if (offset < 8) offset = 8;
                                                    if (offset > popupChild.Bounds.Width - 24) offset = Math.Max(8, popupChild.Bounds.Width - 24);

                                                    if (caretTop != null)
                                                    {
                                                        caretTop.Margin = new Avalonia.Thickness(offset, 0, 0, 0);
                                                    }
                                                    if (caretBottom != null)
                                                    {
                                                        caretBottom.Margin = new Avalonia.Thickness(offset, 0, 0, 0);
                                                    }

                                                    // Toggle caret visibility based on final placement
                                                    if (popup.Placement == PlacementMode.Bottom)
                                                    {
                                                        if (caretTop != null) caretTop.IsVisible = true;
                                                        if (caretBottom != null) caretBottom.IsVisible = false;
                                                    }
                                                    else
                                                    {
                                                        if (caretTop != null) caretTop.IsVisible = false;
                                                        if (caretBottom != null) caretBottom.IsVisible = true;
                                                    }

#if DEBUG
                                                    System.Diagnostics.Debug.WriteLine($"[ICON-MGR] VariantPopup measured placement={(popup.Placement)} caretOffset={offset:F1}");
#endif
                                                }
                                            }
                                        }
                                    }
#pragma warning disable CA1031
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[ICON-MGR] Popup measurement error: {ex.Message}");
                                    }
#pragma warning restore CA1031
                                });
                            }
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

        private void IconManagerWindow_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (!(DataContext is IconManagerViewModel vm)) return;

                var popup = this.FindControl<Popup>("VariantPopup");
                if (popup == null || !vm.IsVariantPopupOpen) return;

                // If the pointer event's source is associated with an IconFileEntryViewModel
                // that exists inside the TopIcons or VariantIcons collections, treat it as
                // an interaction inside the popup/top list and don't close the popup.
                var srcControl = e.Source as Control;
                if (srcControl != null)
                {
                    var dc = srcControl.DataContext;
                    if (dc is ViewModels.IconFileEntryViewModel iconVm)
                    {
                        // Avoid closing when clicking a top-icon (to re-open or change selection)
                        if (vm.TopIcons.Contains(iconVm)) return;
                        // Avoid closing when clicking inside the variant popup items
                        if (vm.VariantIcons.Contains(iconVm)) return;
                    }
                }

                // Otherwise close the popup
                vm.IsVariantPopupOpen = false;
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconManager] PointerPressed handler error: {ex.Message}");
            }
#pragma warning restore CA1031
        }
    }
}
