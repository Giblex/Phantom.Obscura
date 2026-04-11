using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Giblex.Controls;

public sealed class BorderEnergyTrace : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(Progress));

    public static readonly StyledProperty<double> TraceOpacityProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(TraceOpacity));

    public static readonly StyledProperty<double> TraceThicknessProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(TraceThickness), 0.85);

    public static readonly StyledProperty<double> DirectionProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(Direction), 1.0);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, CornerRadius>(nameof(CornerRadius), new CornerRadius(10));

    public static readonly StyledProperty<Color> TraceColorProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, Color>(nameof(TraceColor), Colors.White);

    public static readonly StyledProperty<double> SecondaryProgressProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(SecondaryProgress));

    public static readonly StyledProperty<Color> CounterTraceColorProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, Color>(nameof(CounterTraceColor), Color.FromArgb(255, 16, 26, 38));

    public static readonly StyledProperty<double> CounterTraceOpacityProperty =
        AvaloniaProperty.Register<BorderEnergyTrace, double>(nameof(CounterTraceOpacity), 0.32);

    static BorderEnergyTrace()
    {
        AffectsRender<BorderEnergyTrace>(
            ProgressProperty,
            TraceOpacityProperty,
            TraceThicknessProperty,
            DirectionProperty,
            CornerRadiusProperty,
            TraceColorProperty,
            SecondaryProgressProperty,
            CounterTraceColorProperty,
            CounterTraceOpacityProperty);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double TraceOpacity
    {
        get => GetValue(TraceOpacityProperty);
        set => SetValue(TraceOpacityProperty, value);
    }

    public double TraceThickness
    {
        get => GetValue(TraceThicknessProperty);
        set => SetValue(TraceThicknessProperty, value);
    }

    public double Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color TraceColor
    {
        get => GetValue(TraceColorProperty);
        set => SetValue(TraceColorProperty, value);
    }

    public double SecondaryProgress
    {
        get => GetValue(SecondaryProgressProperty);
        set => SetValue(SecondaryProgressProperty, value);
    }

    public Color CounterTraceColor
    {
        get => GetValue(CounterTraceColorProperty);
        set => SetValue(CounterTraceColorProperty, value);
    }

    public double CounterTraceOpacity
    {
        get => GetValue(CounterTraceOpacityProperty);
        set => SetValue(CounterTraceOpacityProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 4 || Bounds.Height <= 4 || TraceOpacity <= 0.01 || TraceThickness <= 0.1)
        {
            return;
        }

        var inset = (TraceThickness / 2.0) + 0.75;
        var rect = new Rect(inset, inset, Math.Max(1, Bounds.Width - (inset * 2.0)), Math.Max(1, Bounds.Height - (inset * 2.0)));
        var radius = Math.Min(Math.Min(CornerRadius.TopLeft, rect.Width / 2.0), rect.Height / 2.0);
        var perimeter = GetPerimeter(rect, radius);
        if (perimeter <= 1)
        {
            return;
        }

        var direction = Direction >= 0 ? 1.0 : -1.0;
        DrawCounterTrace(context, rect, radius, perimeter, Normalize(Progress, perimeter), direction, 1.0);
        DrawCounterTrace(context, rect, radius, perimeter, Normalize(SecondaryProgress, perimeter), direction, 0.82);
        DrawTrace(context, rect, radius, perimeter, Normalize(Progress, perimeter), direction, 1.0, TraceColor);
        DrawTrace(context, rect, radius, perimeter, Normalize(SecondaryProgress, perimeter), direction, 0.78, TraceColor);
    }

    private void DrawTrace(DrawingContext context, Rect rect, double radius, double perimeter, double head, double direction, double opacityScale, Color colorBase)
    {
        var length = Math.Clamp(perimeter * 0.34, 96, 188);
        DrawProfiledTrace(
            context,
            rect,
            radius,
            perimeter,
            head,
            direction,
            length,
            colorBase,
            TraceOpacity * opacityScale * 0.82,
            TraceThickness + 0.02,
            TraceThickness + 0.46,
            108);
    }

    private void DrawCounterTrace(DrawingContext context, Rect rect, double radius, double perimeter, double head, double direction, double opacityScale)
    {
        var counterHead = Normalize(head, perimeter);
        var length = Math.Clamp(perimeter * 0.34, 96, 188);
        DrawProfiledTrace(
            context,
            rect,
            radius,
            perimeter,
            counterHead,
            direction,
            length,
            CounterTraceColor,
            TraceOpacity * CounterTraceOpacity * opacityScale * 0.58,
            TraceThickness,
            TraceThickness + 0.32,
            96);
    }

    private static void DrawProfiledTrace(
        DrawingContext context,
        Rect rect,
        double radius,
        double perimeter,
        double center,
        double direction,
        double length,
        Color colorBase,
        double peakOpacity,
        double minThickness,
        double maxThickness,
        int sampleCount)
    {
        var halfLength = length / 2.0;
        var start = center - (direction * halfLength);
        var step = length / sampleCount;

        for (var i = 0; i < sampleCount; i++)
        {
            var mid = ((i + 0.5) / sampleCount) * 2.0 - 1.0;
            var profile = Math.Exp(-(mid * mid) / 0.18);
            var opacity = peakOpacity * profile;
            var thickness = minThickness + ((maxThickness - minThickness) * profile);

            var from = Normalize(start + (direction * i * step), perimeter);
            var to = Normalize(start + (direction * (i + 1.15) * step), perimeter);
            var p1 = GetPointAt(rect, radius, from);
            var p2 = GetPointAt(rect, radius, to);
            var pen = new Pen(new SolidColorBrush(ApplyAlpha(colorBase, opacity)), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(pen, p1, p2);
        }
    }

    private void DrawContinuousSegment(
        DrawingContext context,
        Rect rect,
        double radius,
        double perimeter,
        double start,
        double end,
        double direction,
        Color color,
        double thickness,
        int sampleCount)
    {
        var geometry = new StreamGeometry();
        using var geometryContext = geometry.Open();
        var signedDistance = end - start;
        if (direction >= 0 && signedDistance < 0)
        {
            signedDistance += perimeter;
        }
        else if (direction < 0 && signedDistance > 0)
        {
            signedDistance -= perimeter;
        }

        var startPoint = GetPointAt(rect, radius, Normalize(start, perimeter));
        geometryContext.BeginFigure(startPoint, false);
        for (var i = 1; i <= sampleCount; i++)
        {
            var t = i / (double)sampleCount;
            var distance = Normalize(start + (signedDistance * t), perimeter);
            var point = GetPointAt(rect, radius, distance);
            geometryContext.LineTo(point);
        }

        var pen = new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private static double GetPerimeter(Rect rect, double radius)
    {
        var horizontal = Math.Max(0, rect.Width - (radius * 2.0));
        var vertical = Math.Max(0, rect.Height - (radius * 2.0));
        return (horizontal * 2.0) + (vertical * 2.0) + (2.0 * Math.PI * radius);
    }

    private static Point GetPointAt(Rect rect, double radius, double distance)
    {
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;
        var horizontal = Math.Max(0, rect.Width - (radius * 2.0));
        var vertical = Math.Max(0, rect.Height - (radius * 2.0));
        var quarterArc = Math.PI * radius / 2.0;

        if (distance < horizontal)
        {
            return new Point(left + radius + distance, top);
        }

        distance -= horizontal;
        if (distance < quarterArc)
        {
            return PointOnArc(right - radius, top + radius, radius, -Math.PI / 2.0, distance / radius);
        }

        distance -= quarterArc;
        if (distance < vertical)
        {
            return new Point(right, top + radius + distance);
        }

        distance -= vertical;
        if (distance < quarterArc)
        {
            return PointOnArc(right - radius, bottom - radius, radius, 0, distance / radius);
        }

        distance -= quarterArc;
        if (distance < horizontal)
        {
            return new Point(right - radius - distance, bottom);
        }

        distance -= horizontal;
        if (distance < quarterArc)
        {
            return PointOnArc(left + radius, bottom - radius, radius, Math.PI / 2.0, distance / radius);
        }

        distance -= quarterArc;
        if (distance < vertical)
        {
            return new Point(left, bottom - radius - distance);
        }

        distance -= vertical;
        return PointOnArc(left + radius, top + radius, radius, Math.PI, distance / radius);
    }

    private static Point PointOnArc(double cx, double cy, double radius, double startAngle, double angleDelta)
    {
        var angle = startAngle + angleDelta;
        return new Point(
            cx + (Math.Cos(angle) * radius),
            cy + (Math.Sin(angle) * radius));
    }

    private static double Normalize(double value, double range)
    {
        if (range <= 0)
        {
            return 0;
        }

        while (value < 0)
        {
            value += range;
        }

        while (value >= range)
        {
            value -= range;
        }

        return value;
    }

    private static Color ApplyAlpha(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(opacity * 255.0), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
