using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Shared "you clicked it, it copied" feedback for click-to-copy surfaces: a short press-and-
/// bounce on the clicked element, then a small "Copied to clipboard" flyout above it that
/// dismisses itself. Show the flyout only after the copy actually succeeded.
/// </summary>
public static class CopyFeedback
{
    private static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan FlyoutLifetime = TimeSpan.FromMilliseconds(1300);

    /// <summary>Press-and-bounce on <paramref name="target"/>. Skipped under Reduce Motion.</summary>
    public static void Bounce(Control target)
    {
        if (AccessibilityService.Instance.ReduceMotion) return;

        if (target.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            target.RenderTransform = scale;
            target.RenderTransformOrigin = RelativePoint.Center;
        }

        SetScaleTransitions(scale, PressDuration, new CubicEaseOut());
        scale.ScaleX = 0.95;
        scale.ScaleY = 0.95;

        DispatcherTimer.RunOnce(() =>
        {
            // BackEaseOut overshoots slightly past 1.0 and settles: the "bounce".
            SetScaleTransitions(scale, ReleaseDuration, new BackEaseOut());
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }, PressDuration);
    }

    /// <summary>Shows a self-dismissing "Copied to clipboard" flyout above <paramref name="target"/>.</summary>
    public static void ShowCopied(Control target, string message = "Copied to clipboard")
    {
        // A roomier bubble with the tick and message centred in it, so it reads at a glance.
        var content = new Border
        {
            MinWidth = 190,
            MinHeight = 38,
            Padding = new Thickness(16, 9),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "✓",
                        FontSize = 15,
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        var flyout = new Flyout
        {
            Content = content,
            Placement = PlacementMode.Top,
            // Transient: shown without taking focus or adding a light-dismiss layer, so the
            // next click on another field works straight away.
            ShowMode = FlyoutShowMode.Transient
        };

        flyout.ShowAt(target);
        DispatcherTimer.RunOnce(flyout.Hide, FlyoutLifetime);
    }

    private static void SetScaleTransitions(ScaleTransform scale, TimeSpan duration, Easing easing)
    {
        scale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = duration, Easing = easing },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = duration, Easing = easing }
        };
    }
}
