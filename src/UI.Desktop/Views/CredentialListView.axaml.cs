using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace PhantomVault.UI.Views;

public partial class CredentialListView : UserControl
{

    // Grid view targets 3 columns by default. We only drop to 2 (then 1) once the
    // per-tile slot would fall below MinTileWidth — that way the panel keeps 3 columns
    // even when it's narrower than 3×MinTileWidth as long as tiles can still render.
    private const double MinTileWidth = 132;
    private const int TargetColumns = 3;

    private ScrollViewer? _scrollViewer;
    private ItemsControl? _gridItemsControl;
    private WrapPanel? _gridWrapPanel;

    public CredentialListView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _scrollViewer = this.FindControl<ScrollViewer>("CredentialScrollViewer");
        _gridItemsControl = this.FindControl<ItemsControl>("GridItemsControl");

        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }

        if (_gridItemsControl != null)
        {
            _gridItemsControl.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty && _gridItemsControl.IsVisible)
                {
                    _gridWrapPanel = null;
                    Dispatcher.UIThread.Post(UpdateGridTileWidth, DispatcherPriority.Render);
                }
            };
        }

        LayoutUpdated += OnLayoutUpdated;

        Dispatcher.UIThread.Post(UpdateGridTileWidth, DispatcherPriority.Loaded);
    }

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

    private void UpdateGridTileWidth()
    {
        if (_scrollViewer == null) return;

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

        available -= 8;

        available -= 18;

        // Prefer TargetColumns; step down only if that would shrink each tile below
        // MinTileWidth. Below 1 column just clamps to 1.
        int columns = TargetColumns;
        while (columns > 1 && (available / columns) < MinTileWidth)
        {
            columns--;
        }

        double itemWidth = Math.Floor(available / columns);

        if (Math.Abs(_gridWrapPanel.ItemWidth - itemWidth) > 1)
        {
            _gridWrapPanel.ItemWidth = itemWidth;
        }
    }
}

