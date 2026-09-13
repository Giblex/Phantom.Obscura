using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PhantomVault.UI.Views;

public partial class CredentialListView : UserControl
{

    // Grid view targets 3 columns by default. We only drop to 2 (then 1) once the
    // per-tile slot would fall below MinTileWidth — that way the panel keeps 3 columns
    // even when it's narrower than 3×MinTileWidth as long as tiles can still render.
    private const double MinTileWidth = 132;
    private const int TargetColumns = 3;

    // Double-click: both presses on the SAME tile, close in time and position. Avalonia's
    // ClickCount counts any two presses inside the system double-click time, including two
    // single clicks on different tiles, which opened the editor by accident.
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(400);
    private const double DoubleClickSlop = 6;

    // Tile press: a light squeeze, then a short, gentle release with only a hint of spring.
    // (0.95 with a 1.6 overshoot over 480ms read as too bouncy.)
    private const double PressScale = 0.97;
    private const double ReleaseOvershoot = 0.5;
    private static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(320);

    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// Width of each grid tile. Every grid WrapPanel (the plain grid and each category section
    /// of the grouped grid) binds its ItemWidth here, so sizing is one property set on resize
    /// rather than a search of the visual tree on every layout pass.
    /// </summary>
    public static readonly StyledProperty<double> GridItemWidthProperty =
        AvaloniaProperty.Register<CredentialListView, double>(nameof(GridItemWidth), 176);

    public double GridItemWidth
    {
        get => GetValue(GridItemWidthProperty);
        set => SetValue(GridItemWidthProperty, value);
    }

    private readonly ConditionalWeakTable<Button, CancellationTokenSource> _bounces = new();
    private WeakReference<Button>? _lastPressTile;
    private DateTime _lastPressTime;
    private Point _lastPressPoint;

    public CredentialListView()
    {
        AvaloniaXamlLoader.Load(this);

        // Tunnel + handledEventsToo: the tile Button captures and handles the press, so a
        // bubbling handler (or DoubleTapped) never reaches this view.
        AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The bounce plays on release, after the Button has decided the click. Scaling the
        // tile while the button was held shrank its bounds, so a release near the edge landed
        // outside it and the click was lost (tiles felt unresponsive).
        AddHandler(PointerReleasedEvent, OnTilePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        var tile = TileFrom(e.Source);
        if (tile != null)
            _ = BounceAsync(tile);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _scrollViewer = this.FindControl<ScrollViewer>("CredentialScrollViewer");

        // Tile width only depends on the scroll area's width, so resizing is the only trigger.
        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }

        Dispatcher.UIThread.Post(UpdateGridTileWidth, DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_scrollViewer != null)
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;

        base.OnUnloaded(e);
    }

    // ---- tiles: click bounce + double-click to edit ------------------------------------

    /// <summary>
    /// The list/grid tile (not one of its inner buttons, such as favourite or edit) that an
    /// event came from, or null.
    /// </summary>
    private static Button? TileFrom(object? source)
    {
        var button = (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        return button != null && (button.Classes.Contains("tile-button") || button.Classes.Contains("tile-button-grid"))
            ? button
            : null;
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var tile = TileFrom(e.Source);
        if (tile == null) return;

        // One click selects (TileClickCommand shows the card); only a real double-click on
        // the same tile opens the editor.
        var now = DateTime.UtcNow;
        var point = e.GetPosition(this);
        var isDoubleClick =
            _lastPressTile != null &&
            _lastPressTile.TryGetTarget(out var previousTile) &&
            ReferenceEquals(previousTile, tile) &&
            now - _lastPressTime <= DoubleClickWindow &&
            Math.Abs(point.X - _lastPressPoint.X) <= DoubleClickSlop &&
            Math.Abs(point.Y - _lastPressPoint.Y) <= DoubleClickSlop;

        if (isDoubleClick)
        {
            // Reset so a third quick click is not another double-click.
            var gap = now - _lastPressTime;
            _lastPressTile = null;
            OpenEditor(tile, gap);
            return;
        }

        _lastPressTile = new WeakReference<Button>(tile);
        _lastPressTime = now;
        _lastPressPoint = point;
    }

    /// <summary>
    /// Opens the double-clicked entry in edit mode; the editor pops out over the display
    /// column's card for that entry (see VaultWindow.RunEditPanelOpenAnimation).
    /// </summary>
    private void OpenEditor(Button tile, TimeSpan gap)
    {
        if (tile.DataContext is not CredentialViewModel credential) return;

        var vault = DataContext as VaultViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as VaultViewModel;
        if (vault == null)
        {
            Serilog.Log.Warning("[CredentialList] Double-click could not find the vault view model.");
            return;
        }

        // Logged so an editor that opens without this line can be traced to another path
        // (the tile's pencil button, the detail card's Edit button).
        Serilog.Log.Debug("[CredentialList] Double-click on '{Title}' ({Gap:0} ms apart): opening editor.",
            credential.Title, gap.TotalMilliseconds);
        vault.EditCredentialCommand.Execute(credential).Subscribe(_ => { }, ex =>
            Serilog.Log.Warning(ex, "[CredentialList] Failed to open edit from double-click."));
    }

    private async System.Threading.Tasks.Task BounceAsync(Button tile)
    {
        if (Application.Current?.TryGetResource("ReduceAnimations", null, out var reduce) == true && reduce is true)
            return;

        // A second press (e.g. the double-click) restarts the bounce instead of stacking it.
        if (_bounces.TryGetValue(tile, out var previous))
        {
            previous.Cancel();
            _bounces.Remove(tile);
        }
        var cts = new CancellationTokenSource();
        _bounces.Add(tile, cts);

        tile.RenderTransformOrigin = RelativePoint.Center;
        if (tile.RenderTransform is not ScaleTransform)
            tile.RenderTransform = new ScaleTransform(1, 1);
        var start = ((ScaleTransform)tile.RenderTransform).ScaleX;

        var press = new Animation
        {
            Duration = PressDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children = { Key(0, start), Key(1, PressScale) }
        };

        // One spring back: a single small overshoot, then settle (no second bounce).
        var release = new Animation
        {
            Duration = ReleaseDuration,
            Easing = new SpringOutEasing { Overshoot = ReleaseOvershoot },
            FillMode = FillMode.Forward,
            Children = { Key(0, PressScale), Key(1, 1.0) }
        };

        try
        {
            // Run on the Button itself: the transform animator applies ScaleTransform setters
            // to its RenderTransform (a bare transform is not a valid target).
            await press.RunAsync(tile, cts.Token);
            if (!cts.IsCancellationRequested)
                await release.RunAsync(tile, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_bounces.TryGetValue(tile, out var current) && ReferenceEquals(current, cts))
            {
                _bounces.Remove(tile);
                tile.RenderTransform = null;
            }
            cts.Dispose();
        }

        static KeyFrame Key(double cue, double scale) => new()
        {
            Cue = new Cue(cue),
            Setters =
            {
                new Setter(ScaleTransform.ScaleXProperty, scale),
                new Setter(ScaleTransform.ScaleYProperty, scale)
            }
        };
    }

    // ---- grid tile sizing ----------------------------------------------------------------

    private void OnScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateGridTileWidth();
    }

    private void UpdateGridTileWidth()
    {
        if (_scrollViewer == null) return;

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

        if (Math.Abs(GridItemWidth - itemWidth) > 1)
        {
            GridItemWidth = itemWidth;
        }
    }
}
