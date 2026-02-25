using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class CategoryManagerView : UserControl
    {
        private bool _isDragging;
        private bool _pointerPressed;
        private Point _dragStartPoint;
        private int _dragStartIndex = -1;
        private const double DragThreshold = 3.0;
        private Border? _draggedTile;
        private Border? _tileContainer;
        private Point _lastDragPosition;
        private Transitions? _savedTransitions;
        private CategoryItem? _currentFlyoutCategory; // Store category when flyout opens
        private Flyout? _currentFlyout; // Store the flyout reference

        /// <summary>
        /// Set the dragged tile container to a high ZIndex so it renders on top of siblings.
        /// </summary>
        private void ElevateDraggedTile()
        {
            if (_tileContainer == null) return;
            _tileContainer.ZIndex = 9999;
            // The actual child of the StackPanel is the ContentPresenter wrapping TileContainer.
            // We must also elevate it so the Panel respects the ZIndex for rendering order.
            if (_tileContainer.Parent is Avalonia.Controls.Presenters.ContentPresenter cp)
            {
                cp.ZIndex = 9999;
            }
        }

        /// <summary>
        /// Reset all tile container ZIndex values back to 0.
        /// </summary>
        private void ResetAllTileZIndex()
        {
            try
            {
                var ic = this.FindControl<ItemsControl>("CategoryItems");
                if (ic == null) return;

                // Walk the visual tree to find all TileContainer borders
                foreach (var child in ic.GetVisualDescendants())
                {
                    if (child is Border b && b.Name == "TileContainer")
                    {
                        b.ZIndex = 0;
                        if (b.Parent is Avalonia.Controls.Presenters.ContentPresenter cp)
                        {
                            cp.ZIndex = 0;
                        }
                    }
                }
            }
            catch { }
        }

        public CategoryManagerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private CategoryItem? FindCategoryItemFromFlyout(Control control)
        {
            // Walk up the visual tree to find the Flyout's PlacementTarget (the color button)
            var current = control.Parent as Avalonia.StyledElement;
            while (current != null)
            {
                // Check if this is a Popup (the flyout root)
                if (current is PopupRoot popupRoot)
                {
                    if (popupRoot.Parent is Popup popup)
                    {
                        if (popup.PlacementTarget is Control target)
                        {
                            // Now walk up from the placement target to find the CategoryItem DataContext
                            return FindCategoryItemFromControl(target);
                        }
                    }
                    break;
                }
                current = current.Parent as Avalonia.StyledElement;
            }

            return null;
        }

        private CategoryItem? FindCategoryItemFromControl(Control control)
        {
            var current = control as Avalonia.StyledElement;
            while (current != null)
            {
                if (current.DataContext is CategoryItem item)
                {
                    return item;
                }
                current = current.Parent as Avalonia.StyledElement;
            }
            return null;
        }

        private Control? FindPaletteRootFromDescendant(Avalonia.StyledElement element)
        {
            var current = element;
            while (current != null)
            {
                if (current is Control control && control.Name == "ColorFlyoutRoot")
                {
                    return control;
                }

                current = current.Parent as Avalonia.StyledElement;
            }

            return null;
        }

        private void UpdatePaletteSelection(Control? container, string? colorHex)
        {
            if (container == null)
            {
                return;
            }

            var normalized = NormalizeColor(colorHex);

            foreach (var button in container.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("palette-swatch")))
            {
                var swatchValue = NormalizeColor(button.Tag as string);
                var isSelected = swatchValue == normalized;

                ((IPseudoClasses)button.Classes).Set(":selected-color", isSelected);
            }
        }

        private static string NormalizeColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed.ToUpperInvariant() : "#" + trimmed.ToUpperInvariant();
        }

        private void OnColorFlyoutOpened(object? sender, EventArgs e)
        {
            try
            {
                if (sender is not Flyout flyout)
                    return;

                // Store the flyout reference so we can close it later
                _currentFlyout = flyout;

                if (flyout.Content is not Control root)
                    return;

                var category = FindCategoryItemFromFlyout(root);
                if (category == null)
                {
                    // Alternative: try to get from PlacementTarget directly
                    if (flyout.Target is Control target)
                    {
                        category = FindCategoryItemFromControl(target);
                    }
                }

                if (category == null)
                    return;

                // Store the category for use in click handlers
                _currentFlyoutCategory = category;

                UpdatePaletteSelection(root, category.TileColor);

                var hexBox = root.FindControl<TextBox>("CustomHexBox");
                if (hexBox != null)
                {
                    hexBox.Text = NormalizeColor(category.TileColor);
                    hexBox.CaretIndex = hexBox.Text.Length;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnColorFlyoutOpened error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CloseFlyout(Control control)
        {
            try
            {
                // Use the stored flyout reference if available
                if (_currentFlyout != null)
                {
                    _currentFlyout.Hide();
                    _currentFlyout = null;
                    return;
                }

                // Fallback: Walk up the visual tree to find the PopupRoot
                var current = control.Parent as Avalonia.StyledElement;
                while (current != null)
                {
                    if (current is PopupRoot popupRoot)
                    {
                        // The Popup should be the parent of PopupRoot
                        if (popupRoot.Parent is Popup popup)
                        {
                            popup.IsOpen = false;
                            return;
                        }
                        break;
                    }

                    current = current.Parent as Avalonia.StyledElement;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloseFlyout error: {ex.Message}");
            }
        }

        private void OnCategoryPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (sender is not Border dragHandle)
                    return;

                if (dragHandle.Tag is not CategoryItem item)
                    return;

                if (DataContext is not CategoryManagerViewModel vm)
                    return;

                _dragStartIndex = vm.Categories.IndexOf(item);

                Border? tile = null;
                Border? container = null;
                var current = dragHandle.Parent as Avalonia.StyledElement;
                while (current != null && (tile == null || container == null))
                {
                    if (current is Border b)
                    {
                        if (b.Name == "CategoryTile" && tile == null)
                            tile = b;
                        else if (b.Name == "TileContainer" && container == null)
                            container = b;
                    }
                    current = current.Parent as Avalonia.StyledElement;
                }

                _draggedTile = tile;
                _tileContainer = container;

                _dragStartPoint = e.GetPosition(this);
                _lastDragPosition = _dragStartPoint;
                _pointerPressed = true;

                e.Pointer.Capture(dragHandle);
            }
            catch
            {
            }
        }

        private void OnCategoryPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_dragStartIndex < 0 || !_pointerPressed)
            {
                return;
            }

            var currentPos = e.GetPosition(this);

            if (!_isDragging && _dragStartIndex >= 0)
            {
                var delta = currentPos - _dragStartPoint;
                if (System.Math.Abs(delta.Y) > DragThreshold)
                {
                    _isDragging = true;

                    if (sender is Control ctrl)
                    {
                        ctrl.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.DragMove);
                    }

                    if (_tileContainer != null)
                    {
                        _savedTransitions = _tileContainer.Transitions;
                        _tileContainer.Transitions = null;
                        ElevateDraggedTile();
                        _tileContainer.Opacity = 0.92;

                        if (_tileContainer.RenderTransform is TransformGroup transformGroup && transformGroup.Children.Count >= 3)
                        {
                            if (transformGroup.Children[0] is ScaleTransform scale)
                            {
                                scale.ScaleX = 1.04;
                                scale.ScaleY = 1.04;
                            }

                            if (transformGroup.Children[1] is RotateTransform rotate)
                            {
                                rotate.Angle = 1.0; // slight tilt for "lifted" feel
                            }
                        }

                        // Add a drop shadow to the dragged tile
                        if (_draggedTile != null)
                        {
                            _draggedTile.BoxShadow = new Avalonia.Media.BoxShadows(
                                new Avalonia.Media.BoxShadow
                                {
                                    OffsetX = 0,
                                    OffsetY = 4,
                                    Blur = 16,
                                    Spread = 2,
                                    Color = Avalonia.Media.Color.FromArgb(120, 0, 0, 0)
                                });
                        }
                    }
                }
            }

            if (_isDragging && _tileContainer != null)
            {
                var delta = currentPos - _lastDragPosition;

                if (_tileContainer.RenderTransform is TransformGroup transformGroup && transformGroup.Children.Count >= 3)
                {
                    if (transformGroup.Children[2] is TranslateTransform translate)
                    {
                        translate.X += delta.X;
                        translate.Y += delta.Y;
                    }
                }

                _lastDragPosition = currentPos;
            }
        }

        private void OnCategoryPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            _pointerPressed = false;

            if (!_isDragging || _dragStartIndex < 0)
            {
                _isDragging = false;
                _dragStartIndex = -1;
                _draggedTile = null;
                e.Pointer.Capture(null);
                return;
            }

            _isDragging = false;
            try
            {
                if (this.FindControl<ItemsControl>("CategoryItems") is ItemsControl ic && DataContext is CategoryManagerViewModel vm)
                {
                    int targetIndex = -1;
                    var pos = e.GetPosition(ic);
                    var count = vm.Categories.Count;
                    if (count > 0 && ic.Bounds.Height > 0)
                    {
                        double estimatedItemHeight = ic.Bounds.Height / count;
                        if (estimatedItemHeight > 0)
                        {
                            targetIndex = (int)(pos.Y / estimatedItemHeight);
                            if (targetIndex < 0) targetIndex = 0;
                            if (targetIndex >= count) targetIndex = count - 1;
                        }
                    }

                    if (targetIndex >= 0 && targetIndex != _dragStartIndex)
                    {
                        vm.MoveCategory(_dragStartIndex, targetIndex);
                    }
                }

                if (_tileContainer != null)
                {
                    _tileContainer.Transitions = _savedTransitions;
                    _savedTransitions = null;
                    ResetAllTileZIndex();
                    _tileContainer.Opacity = 1.0;

                    // Clear drop shadow
                    if (_draggedTile != null)
                    {
                        _draggedTile.BoxShadow = default;
                    }

                    if (_tileContainer.RenderTransform is TransformGroup transformGroup)
                    {
                        if (transformGroup.Children.Count >= 1 && transformGroup.Children[0] is ScaleTransform scale)
                        {
                            scale.ScaleX = 1.0;
                            scale.ScaleY = 1.0;
                        }
                        if (transformGroup.Children.Count >= 2 && transformGroup.Children[1] is RotateTransform rotate)
                        {
                            rotate.Angle = 0;
                        }
                        if (transformGroup.Children.Count >= 3 && transformGroup.Children[2] is TranslateTransform translate)
                        {
                            translate.X = 0;
                            translate.Y = 0;
                        }
                    }
                }
            }
            catch
            {
            }

            e.Pointer.Capture(null);
            _dragStartIndex = -1;
        }

        private void OnCategoryPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
        {
            _isDragging = false;
            _dragStartIndex = -1;
            _pointerPressed = false;

            if (sender is Control ctrl)
            {
                ctrl.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            }

            if (_tileContainer != null)
            {
                _tileContainer.Transitions = _savedTransitions;
                _savedTransitions = null;
                ResetAllTileZIndex();
                _tileContainer.Opacity = 1.0;

                // Clear drop shadow
                if (_draggedTile != null)
                {
                    _draggedTile.BoxShadow = default;
                }

                if (_tileContainer.RenderTransform is TransformGroup transformGroup)
                {
                    if (transformGroup.Children.Count >= 1 && transformGroup.Children[0] is ScaleTransform scale)
                    {
                        scale.ScaleX = 1.0;
                        scale.ScaleY = 1.0;
                    }
                    if (transformGroup.Children.Count >= 2 && transformGroup.Children[1] is RotateTransform rotate)
                    {
                        rotate.Angle = 0;
                    }
                    if (transformGroup.Children.Count >= 3 && transformGroup.Children[2] is TranslateTransform translate)
                    {
                        translate.X = 0;
                        translate.Y = 0;
                    }
                }

                _draggedTile = null;
                _tileContainer = null;
            }
        }

        private void OnPaletteColorClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button)
                    return;
                if (DataContext is not CategoryManagerViewModel vm)
                    return;

                var normalized = NormalizeColor(button.Tag as string);

                // Use stored category from flyout opened event
                var category = _currentFlyoutCategory;
                if (category == null)
                {
                    category = FindCategoryItemFromFlyout(button);
                }

                if (category == null)
                    return;

                var newColor = string.IsNullOrEmpty(normalized) ? null : normalized;
                category.TileColor = newColor;

                vm.SaveCategoryCommand.Execute(category).Subscribe();

                var paletteRoot = FindPaletteRootFromDescendant(button);
                UpdatePaletteSelection(paletteRoot, newColor);

                var hexBox = paletteRoot?.FindControl<TextBox>("CustomHexBox");
                if (hexBox != null)
                {
                    hexBox.Text = NormalizeColor(newColor);
                }

                // Close the flyout after color selection
                CloseFlyout(button);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnPaletteColorClick error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnPaletteApplyClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not CategoryManagerViewModel vm) return;
                if (sender is not Button applyBtn) return;

                var paletteRoot = FindPaletteRootFromDescendant(applyBtn);
                var hexBox = paletteRoot?.FindControl<TextBox>("CustomHexBox");

                var raw = hexBox?.Text?.Trim();
                if (string.IsNullOrEmpty(raw)) return;

                var normalized = NormalizeColor(raw);
                Color.Parse(normalized);

                // Use stored category from flyout opened event
                var category = _currentFlyoutCategory;
                if (category == null)
                {
                    category = FindCategoryItemFromFlyout(applyBtn);
                }

                if (category != null)
                {
                    var newColor = string.IsNullOrEmpty(normalized) ? null : normalized;
                    category.TileColor = newColor;
                    vm.SaveCategoryCommand.Execute(category).Subscribe();

                    if (hexBox != null)
                    {
                        hexBox.Text = NormalizeColor(newColor);
                    }

                    UpdatePaletteSelection(paletteRoot, newColor);

                    // Close the flyout after applying custom color
                    CloseFlyout(applyBtn);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnPaletteApplyClick error: {ex.Message}");
            }
        }
    }
}
