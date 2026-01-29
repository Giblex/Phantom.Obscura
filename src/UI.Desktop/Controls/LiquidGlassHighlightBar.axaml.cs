using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace PhantomVault.UI.Controls;

public partial class LiquidGlassHighlightBar : UserControl
{
    private const double MaxOffsetX = 10;
    private const double MaxOffsetY = 6;

    public static readonly StyledProperty<double> BarWidthProperty =
        AvaloniaProperty.Register<LiquidGlassHighlightBar, double>(nameof(BarWidth), 240.0);

    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<LiquidGlassHighlightBar, double>(nameof(BarHeight), 52.0);

    public static readonly StyledProperty<double> BarCornerRadiusProperty =
        AvaloniaProperty.Register<LiquidGlassHighlightBar, double>(nameof(BarCornerRadius), 14.0);

    private Grid? _parallaxLayer;
    private TranslateTransform? _parallaxTransform;

    public double BarWidth
    {
        get => GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public double BarCornerRadius
    {
        get => GetValue(BarCornerRadiusProperty);
        set => SetValue(BarCornerRadiusProperty, value);
    }

    public LiquidGlassHighlightBar()
    {
        InitializeComponent();

        _parallaxLayer = this.FindControl<Grid>("ParallaxLayer");
        _parallaxTransform = _parallaxLayer?.RenderTransform as TranslateTransform;

        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_parallaxTransform is null)
        {
            return;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var position = e.GetPosition(this);
        var normalizedX = Math.Clamp(position.X / bounds.Width, 0d, 1d);
        var normalizedY = Math.Clamp(position.Y / bounds.Height, 0d, 1d);

        // Nudge the highlight layers toward the pointer for a subtle parallax effect.
        _parallaxTransform.X = (normalizedX - 0.5) * 2 * MaxOffsetX;
        _parallaxTransform.Y = (normalizedY - 0.5) * 2 * MaxOffsetY;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => ResetParallax();

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetParallax();

    private void ResetParallax()
    {
        if (_parallaxTransform is null)
        {
            return;
        }

        _parallaxTransform.X = 0;
        _parallaxTransform.Y = 0;
    }
}
