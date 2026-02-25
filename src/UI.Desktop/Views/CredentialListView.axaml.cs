using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace PhantomVault.UI.Views;

/// <summary>
/// Credential list view with header, sort controls, and list/grid display modes.
/// Automatically resizes grid tiles to fill the available width.
/// </summary>
public partial class CredentialListView : UserControl
{
    /// <summary>
    /// Minimum width for each grid tile. The number of columns is calculated so that
    /// every tile is at least this wide, and remaining space is distributed evenly.
    /// </summary>
    private const double MinTileWidth = 240;

    private ScrollViewer? _scrollViewer;
    private ItemsControl? _gridItemsControl;
    private WrapPanel? _gridWrapPanel;

    public CredentialListView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _scrollViewer = this.FindControl<ScrollViewer>("CredentialScrollViewer");
        _gridItemsControl = this.FindControl<ItemsControl>("GridItemsControl");

        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }

        // When the grid becomes visible the visual tree is rebuilt —
        // defer resolution so the WrapPanel exists when we look for it.
        if (_gridItemsControl != null)
        {
            _gridItemsControl.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty && _gridItemsControl.IsVisible)
                {
                    _gridWrapPanel = null; // force re-resolve
                    Dispatcher.UIThread.Post(UpdateGridTileWidth, DispatcherPriority.Render);
                }
            };
        }

        // Also recalculate whenever the items panel is laid out
        // (handles first load and items-source changes)
        LayoutUpdated += OnLayoutUpdated;

        Dispatcher.UIThread.Post(UpdateGridTileWidth, DispatcherPriority.Loaded);
    }

    /// <inheritdoc/>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_scrollViewer != null)
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;

        LayoutUpdated -= OnLayoutUpdated;

        base.OnUnloaded(e);
    }

    private void OnScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateGridTileWidth();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateGridTileWidth();
    }

    /// <summary>
    /// Calculates the optimal tile width so that grid tiles stretch to fill every row,
    /// then applies it to the WrapPanel's <see cref="WrapPanel.ItemWidth"/>.
    /// </summary>
    private void UpdateGridTileWidth()
    {
        if (_scrollViewer == null) return;

        // Lazily locate the WrapPanel (it is only created when the ItemsControl becomes visible)
        if (_gridWrapPanel == null)
        {
            _gridWrapPanel = _gridItemsControl?
                .GetVisualDescendants()
                .OfType<WrapPanel>()
                .FirstOrDefault();

            if (_gridWrapPanel == null) return;
        }

        double available = _scrollViewer.Bounds.Width;
        if (available <= 0) return;

        // Subtract the ItemsControl horizontal margin (4 left + 4 right)
        available -= 8;

        // Account for the vertical scrollbar width (~18px)
        available -= 18;

        int columns = Math.Max(1, (int)(available / MinTileWidth));
        double itemWidth = Math.Floor(available / columns);

        // Only update if it actually changed (avoid layout thrashing)
        if (Math.Abs(_gridWrapPanel.ItemWidth - itemWidth) > 1)
        {
            _gridWrapPanel.ItemWidth = itemWidth;
        }
    }
}
