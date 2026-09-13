using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Lays detail fields side by side, sharing the width equally among the visible ones. An entry
/// missing one of a pair (say, no card type) lets the other take the whole row instead of
/// leaving a hole, which a fixed two-column grid would.
/// </summary>
public sealed class FieldRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FieldRow, double>(nameof(Spacing), 12);

    static FieldRow()
    {
        AffectsMeasure<FieldRow>(SpacingProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (visible.Count == 0)
            return default;

        var totalSpacing = Spacing * (visible.Count - 1);
        var slot = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, (availableSize.Width - totalSpacing) / visible.Count);

        double height = 0, width = 0;
        foreach (var child in visible)
        {
            child.Measure(new Size(slot, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += child.DesiredSize.Width;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width + totalSpacing : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (visible.Count == 0)
            return finalSize;

        var slot = Math.Max(0, (finalSize.Width - Spacing * (visible.Count - 1)) / visible.Count);
        double x = 0;
        foreach (var child in visible)
        {
            child.Arrange(new Rect(x, 0, slot, finalSize.Height));
            x += slot + Spacing;
        }

        return finalSize;
    }
}
