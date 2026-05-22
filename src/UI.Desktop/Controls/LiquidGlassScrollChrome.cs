using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhantomVault.UI.Services;

namespace Giblex.Controls;

public sealed class LiquidGlassScrollChrome
{
    public static readonly AttachedProperty<double> ScrollSheenXProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("ScrollSheenX");

    public static readonly AttachedProperty<double> ScrollSheenYProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("ScrollSheenY");

    public static readonly AttachedProperty<double> ScrollSheenAngleProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("ScrollSheenAngle");

    public static readonly AttachedProperty<double> BorderTraceProgressProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("BorderTraceProgress");

    public static readonly AttachedProperty<double> BorderTraceDirectionProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("BorderTraceDirection", 1.0);

    public static readonly AttachedProperty<double> SecondaryBorderTraceProgressProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("SecondaryBorderTraceProgress");

    public static readonly AttachedProperty<double> BorderRunnerOpacityProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, double>("BorderRunnerOpacity");

    private static readonly AttachedProperty<int> ScrollPulseVersionProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassScrollChrome, Control, int>("ScrollPulseVersion");

    private static readonly TimeSpan PulseResetDelay = TimeSpan.FromMilliseconds(320);
    private const double RestingBorderOpacity = 0.46;
    private const double HoverBorderOpacity = 0.72;
    private const double PressedBorderOpacity = 0.82;
    // Tuned for a calmer, less twitchy liquid-glass trace:
    //   - smaller influence radius → border only reacts when pointer is nearby
    //   - smaller trajectory fraction → smaller travel distance per pointer motion
    //   - reduced midpoint bounce → softer settle when the pointer stops
    private const double PointerInfluenceRadius = 140.0;
    private const double MaxTrajectoryFraction = 0.08;
    private const double MidpointBounceFactor = 0.55;
    private static readonly DispatcherTimer RunnerTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly Dictionary<Button, BorderRunnerState> RunnerStates = new();
    private static readonly Dictionary<TopLevel, PointerMotionSample> PointerMotionSamples = new();
    private static bool _registered;

    private sealed class BorderRunnerState
    {
        public double AnchorProgress;
        public double Progress;
        public double TargetProgress;
        public double Opacity;
        public double TargetOpacity;
        public double FollowResponse = 0.035;
        public double Direction = 1.0;
        public bool IsHovering;
    }

    private readonly record struct PointerMotionSample(Point Position, long Timestamp);

    private LiquidGlassScrollChrome()
    {
    }

    public static double GetScrollSheenX(AvaloniaObject o) => o.GetValue(ScrollSheenXProperty);
    public static void SetScrollSheenX(AvaloniaObject o, double value) => o.SetValue(ScrollSheenXProperty, value);

    public static double GetScrollSheenY(AvaloniaObject o) => o.GetValue(ScrollSheenYProperty);
    public static void SetScrollSheenY(AvaloniaObject o, double value) => o.SetValue(ScrollSheenYProperty, value);

    public static double GetScrollSheenAngle(AvaloniaObject o) => o.GetValue(ScrollSheenAngleProperty);
    public static void SetScrollSheenAngle(AvaloniaObject o, double value) => o.SetValue(ScrollSheenAngleProperty, value);
    public static double GetBorderTraceProgress(AvaloniaObject o) => o.GetValue(BorderTraceProgressProperty);
    public static void SetBorderTraceProgress(AvaloniaObject o, double value) => o.SetValue(BorderTraceProgressProperty, value);
    public static double GetBorderTraceDirection(AvaloniaObject o) => o.GetValue(BorderTraceDirectionProperty);
    public static void SetBorderTraceDirection(AvaloniaObject o, double value) => o.SetValue(BorderTraceDirectionProperty, value);
    public static double GetSecondaryBorderTraceProgress(AvaloniaObject o) => o.GetValue(SecondaryBorderTraceProgressProperty);
    public static void SetSecondaryBorderTraceProgress(AvaloniaObject o, double value) => o.SetValue(SecondaryBorderTraceProgressProperty, value);
    public static double GetBorderRunnerOpacity(AvaloniaObject o) => o.GetValue(BorderRunnerOpacityProperty);
    public static void SetBorderRunnerOpacity(AvaloniaObject o, double value) => o.SetValue(BorderRunnerOpacityProperty, value);

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(OnPointerWheelChanged);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnTopLevelPointerMoved);
        Control.LoadedEvent.AddClassHandler<Button>(OnButtonLoaded);
        Control.UnloadedEvent.AddClassHandler<Button>(OnButtonUnloaded);
        InputElement.PointerEnteredEvent.AddClassHandler<Button>(OnPointerEntered);
        InputElement.PointerMovedEvent.AddClassHandler<Button>(OnPointerMoved);
        InputElement.PointerExitedEvent.AddClassHandler<Button>(OnPointerExited);
        InputElement.PointerPressedEvent.AddClassHandler<Button>(OnPointerPressed);
        InputElement.PointerReleasedEvent.AddClassHandler<Button>(OnPointerReleased);
        RunnerTimer.Tick += OnRunnerTick;
        _registered = true;
    }

    private static void OnPointerEntered(Button button, PointerEventArgs e)
    {
        if (!IsLiquidGlass(button))
        {
            return;
        }

        var state = GetOrCreateState(button);
        state.IsHovering = true;
        state.TargetOpacity = HoverBorderOpacity;
        EnsureRunnerTimer();
    }

    private static void OnPointerMoved(Button button, PointerEventArgs e)
    {
        if (!IsLiquidGlass(button) || AccessibilityService.Instance.ReduceMotion)
        {
            return;
        }
    }

    private static void OnPointerExited(Button button, PointerEventArgs e)
    {
        if (!IsLiquidGlass(button))
        {
            return;
        }

        if (RunnerStates.TryGetValue(button, out var state))
        {
            state.IsHovering = false;
            state.TargetOpacity = RestingBorderOpacity;
            EnsureRunnerTimer();
        }
    }

    private static void OnTopLevelPointerMoved(TopLevel topLevel, PointerEventArgs e)
    {
        if (AccessibilityService.Instance.ReduceMotion)
        {
            return;
        }

        var pointerInTopLevel = e.GetPosition(topLevel);
        var pointerSpeed = GetPointerSpeed(topLevel, pointerInTopLevel);
        foreach (var button in topLevel.GetVisualDescendants().OfType<Button>().Where(IsLiquidGlass))
        {
            UpdateButtonFromGlobalPointer(button, topLevel, pointerInTopLevel, pointerSpeed);
        }
    }

    private static void OnPointerPressed(Button button, PointerPressedEventArgs e)
    {
        if (!IsLiquidGlass(button))
        {
            return;
        }

        var state = GetOrCreateState(button);
        state.TargetOpacity = PressedBorderOpacity;
        EnsureRunnerTimer();
    }

    private static void OnPointerReleased(Button button, PointerReleasedEventArgs e)
    {
        if (!IsLiquidGlass(button))
        {
            return;
        }

        if (RunnerStates.TryGetValue(button, out var state))
        {
            state.TargetOpacity = HoverBorderOpacity;
            EnsureRunnerTimer();
        }
    }

    private static void OnPointerWheelChanged(TopLevel topLevel, PointerWheelEventArgs e)
    {
        if (AccessibilityService.Instance.ReduceMotion)
        {
            return;
        }

        var deltaY = e.Delta.Y;
        if (Math.Abs(deltaY) < double.Epsilon)
        {
            return;
        }

        PulseButtons(topLevel, deltaY);
    }

    private static void PulseButtons(TopLevel root, double deltaY)
    {
        var direction = deltaY > 0 ? -1.0 : 1.0;
        var intensity = Math.Clamp(Math.Abs(deltaY), 0.75, 3.0);

        foreach (var button in root.GetVisualDescendants().OfType<Button>())
        {
            if (!button.IsVisible || !button.Classes.Contains("liquid-glass"))
            {
                continue;
            }

            ApplyPulse(button, direction, intensity);
        }
    }

    private static void ApplyPulse(Button button, double direction, double intensity)
    {
        var pulseVersion = button.GetValue(ScrollPulseVersionProperty) + 1;
        button.SetValue(ScrollPulseVersionProperty, pulseVersion);

        var angle = 22.0 * direction * intensity;
        var offsetX = 10.0 * direction * intensity;
        var offsetY = -5.0 * direction * intensity;

        SetScrollSheenAngle(button, angle);
        SetScrollSheenX(button, offsetX);
        SetScrollSheenY(button, offsetY);

        DispatcherTimer.RunOnce(() =>
        {
            if (button.GetValue(ScrollPulseVersionProperty) != pulseVersion)
            {
                return;
            }

            SetScrollSheenAngle(button, 0);
            SetScrollSheenX(button, 0);
            SetScrollSheenY(button, 0);
        }, PulseResetDelay);
    }

    private static BorderRunnerState GetOrCreateState(Button button)
    {
        if (!RunnerStates.TryGetValue(button, out var state))
        {
            state = new BorderRunnerState
            {
                AnchorProgress = GetCornerAnchorProgress(button),
                Progress = GetCornerAnchorProgress(button),
                TargetProgress = GetCornerAnchorProgress(button),
                Opacity = RestingBorderOpacity,
                TargetOpacity = RestingBorderOpacity,
                Direction = 1.0,
                IsHovering = false
            };
            RunnerStates[button] = state;
            SetBorderRunnerOpacity(button, RestingBorderOpacity);
        }

        return state;
    }

    private static void EnsureRunnerTimer()
    {
        if (!RunnerTimer.IsEnabled)
        {
            RunnerTimer.Start();
        }
    }

    private static void OnRunnerTick(object? sender, EventArgs e)
    {
        if (RunnerStates.Count == 0)
        {
            RunnerTimer.Stop();
            return;
        }

        foreach (var pair in RunnerStates)
        {
            var button = pair.Key;
            var state = pair.Value;
            if (!button.IsVisible || button.Bounds.Width <= 1 || button.Bounds.Height <= 1)
            {
                continue;
            }

            var delta = GetShortestProgressDelta(state.Progress, state.TargetProgress, button);
            if (Math.Abs(delta) < 0.65)
            {
                state.Progress = state.TargetProgress;
            }
            else
            {
                state.Progress = NormalizeProgress(state.Progress + (delta * state.FollowResponse), GetPerimeter(button));
            }
            state.Opacity += (state.TargetOpacity - state.Opacity) * 0.04;
            UpdateBorderRunnerFromProgress(button, state.Progress, state.Opacity, state.Direction);
        }

        if (RunnerStates.Values.All(s =>
            Math.Abs(s.TargetOpacity - s.Opacity) < 0.01 &&
            Math.Abs(s.TargetProgress - s.Progress) < 0.01))
        {
            RunnerTimer.Stop();
        }
    }

    private static void UpdateBorderRunnerFromProgress(Button button, double progress, double opacity, double direction)
    {
        var width = button.Bounds.Width;
        var height = button.Bounds.Height;
        var cornerInset = Math.Max(8.0, button.CornerRadius.TopLeft * 0.7);
        var trackWidth = Math.Max(1.0, width - (cornerInset * 2.0));
        var trackHeight = Math.Max(1.0, height - (cornerInset * 2.0));
        var quarterArc = Math.PI * cornerInset / 2.0;
        var perimeter = (trackWidth * 2.0) + (trackHeight * 2.0) + (quarterArc * 4.0);
        SetBorderRunnerOpacity(button, opacity);
        var normalized = NormalizeProgress(progress, perimeter);
        SetBorderTraceProgress(button, normalized);
        SetSecondaryBorderTraceProgress(button, NormalizeProgress(normalized + (perimeter / 2.0), perimeter));
        SetBorderTraceDirection(button, direction >= 0 ? 1.0 : -1.0);
    }

    private static double NormalizeProgress(double progress, double perimeter)
    {
        if (perimeter <= 0)
        {
            return 0;
        }

        while (progress < 0)
        {
            progress += perimeter;
        }

        while (progress >= perimeter)
        {
            progress -= perimeter;
        }

        return progress;
    }

    private static bool IsLiquidGlass(Button button)
    {
        return button.IsVisible && button.Classes.Contains("liquid-glass");
    }

    private static void UpdateButtonFromGlobalPointer(Button button, TopLevel topLevel, Point pointerInTopLevel, double pointerSpeed)
    {
        if (button.Bounds.Width <= 1 || button.Bounds.Height <= 1)
        {
            return;
        }

        var buttonOrigin = button.TranslatePoint(default, topLevel);
        if (buttonOrigin is null)
        {
            return;
        }

        var pointer = new Point(pointerInTopLevel.X - buttonOrigin.Value.X, pointerInTopLevel.Y - buttonOrigin.Value.Y);
        var proximity = GetPointerProximity(button, pointer);
        var state = GetOrCreateState(button);

        if (proximity > PointerInfluenceRadius)
        {
            state.IsHovering = false;
            state.TargetOpacity = RestingBorderOpacity;
            EnsureRunnerTimer();
            return;
        }

        var influence = 1.0 - Math.Clamp(proximity / PointerInfluenceRadius, 0.0, 1.0);
        var progress = GetProgressFromPointer(button, pointer);
        var delta = GetShortestProgressDelta(state.Progress, progress, button);
        var perimeter = GetPerimeter(button);
        // Damped follow strength — keeps the trace from snapping to fast pointer
        // motion. Lower coefficients = smoother, slower-moving border highlight.
        var speedFactor = Math.Clamp(pointerSpeed / 4800.0, 0.0, 1.0);
        var followStrength = 0.025 + (influence * 0.035) + (speedFactor * 0.025);
        var maxTrajectory = perimeter * MaxTrajectoryFraction;
        var anchorDelta = GetShortestDeltaOnPerimeter(state.AnchorProgress, progress, perimeter);
        if (Math.Abs(delta) > 0.001)
        {
            state.Direction = Math.Sign(delta);
            var constrainedAnchorDelta = Math.Clamp(anchorDelta, -maxTrajectory, maxTrajectory);
            if (Math.Abs(anchorDelta) > maxTrajectory)
            {
                constrainedAnchorDelta *= MidpointBounceFactor;
            }

            state.TargetProgress = NormalizeProgress(state.AnchorProgress + constrainedAnchorDelta, perimeter);
        }
        state.FollowResponse = followStrength;

        state.IsHovering = true;
        state.TargetOpacity = RestingBorderOpacity + ((HoverBorderOpacity - RestingBorderOpacity) * influence);
        EnsureRunnerTimer();
    }

    private static void OnButtonLoaded(Button button, RoutedEventArgs e)
    {
        if (!IsLiquidGlass(button))
        {
            return;
        }

        GetOrCreateState(button);
        var anchor = GetCornerAnchorProgress(button);
        if (RunnerStates.TryGetValue(button, out var state))
        {
            state.AnchorProgress = anchor;
            state.Progress = anchor;
            state.TargetProgress = anchor;
        }
        UpdateBorderRunnerFromProgress(button, anchor, RestingBorderOpacity, 1.0);
        EnsureRunnerTimer();
    }

    private static void OnButtonUnloaded(Button button, RoutedEventArgs e)
    {
        RunnerStates.Remove(button);
        SetBorderRunnerOpacity(button, 0);
    }

    private static double GetProgressFromPointer(Button button, Point pointer)
    {
        var width = button.Bounds.Width;
        var height = button.Bounds.Height;
        var cornerInset = Math.Max(8.0, button.CornerRadius.TopLeft * 0.7);
        var trackWidth = Math.Max(1.0, width - (cornerInset * 2.0));
        var trackHeight = Math.Max(1.0, height - (cornerInset * 2.0));
        var quarterArc = Math.PI * cornerInset / 2.0;

        var distanceTop = pointer.Y;
        var distanceBottom = Math.Abs(height - pointer.Y);
        var distanceLeft = pointer.X;
        var distanceRight = Math.Abs(width - pointer.X);
        var nearest = Math.Min(Math.Min(distanceTop, distanceBottom), Math.Min(distanceLeft, distanceRight));

        if (nearest == distanceTop)
        {
            return Math.Clamp(pointer.X - cornerInset, 0, trackWidth);
        }

        if (nearest == distanceRight)
        {
            return trackWidth + quarterArc + Math.Clamp(pointer.Y - cornerInset, 0, trackHeight);
        }

        if (nearest == distanceBottom)
        {
            return trackWidth + quarterArc + trackHeight + quarterArc + Math.Clamp(width - cornerInset - pointer.X, 0, trackWidth);
        }

        return trackWidth + quarterArc + trackHeight + quarterArc + trackWidth + quarterArc + Math.Clamp(height - cornerInset - pointer.Y, 0, trackHeight);
    }

    private static double GetShortestProgressDelta(double current, double target, Button button)
    {
        var perimeter = GetPerimeter(button);
        return GetShortestDeltaOnPerimeter(current, target, perimeter);
    }

    private static double GetShortestDeltaOnPerimeter(double current, double target, double perimeter)
    {
        var delta = target - current;
        if (delta > perimeter / 2.0)
        {
            delta -= perimeter;
        }
        else if (delta < -perimeter / 2.0)
        {
            delta += perimeter;
        }

        return delta;
    }

    private static double GetPerimeter(Button button)
    {
        var width = button.Bounds.Width;
        var height = button.Bounds.Height;
        var cornerInset = Math.Max(8.0, button.CornerRadius.TopLeft * 0.7);
        var trackWidth = Math.Max(1.0, width - (cornerInset * 2.0));
        var trackHeight = Math.Max(1.0, height - (cornerInset * 2.0));
        var quarterArc = Math.PI * cornerInset / 2.0;
        return (trackWidth * 2.0) + (trackHeight * 2.0) + (quarterArc * 4.0);
    }

    private static double GetCornerAnchorProgress(Button button)
    {
        var width = button.Bounds.Width;
        var height = button.Bounds.Height;
        var cornerInset = Math.Max(8.0, button.CornerRadius.TopLeft * 0.7);
        var trackWidth = Math.Max(1.0, width - (cornerInset * 2.0));
        var trackHeight = Math.Max(1.0, height - (cornerInset * 2.0));
        var quarterArc = Math.PI * cornerInset / 2.0;
        var perimeter = (trackWidth * 2.0) + (trackHeight * 2.0) + (quarterArc * 4.0);

        // Center the primary anchor on the top-left rounded corner arc.
        var anchor = perimeter - (quarterArc / 2.0);
        return NormalizeProgress(anchor, perimeter);
    }

    private static double GetPointerSpeed(TopLevel topLevel, Point pointer)
    {
        var now = Environment.TickCount64;
        if (PointerMotionSamples.TryGetValue(topLevel, out var sample))
        {
            var elapsedMs = Math.Max(1, now - sample.Timestamp);
            var dx = pointer.X - sample.Position.X;
            var dy = pointer.Y - sample.Position.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            PointerMotionSamples[topLevel] = new PointerMotionSample(pointer, now);
            return distance / elapsedMs * 1000.0;
        }

        PointerMotionSamples[topLevel] = new PointerMotionSample(pointer, now);
        return 0;
    }

    private static double GetPointerProximity(Button button, Point pointer)
    {
        var dx = 0.0;
        if (pointer.X < 0)
        {
            dx = -pointer.X;
        }
        else if (pointer.X > button.Bounds.Width)
        {
            dx = pointer.X - button.Bounds.Width;
        }

        var dy = 0.0;
        if (pointer.Y < 0)
        {
            dy = -pointer.Y;
        }
        else if (pointer.Y > button.Bounds.Height)
        {
            dy = pointer.Y - button.Bounds.Height;
        }

        if (dx == 0.0 && dy == 0.0)
        {
            var left = pointer.X;
            var right = button.Bounds.Width - pointer.X;
            var top = pointer.Y;
            var bottom = button.Bounds.Height - pointer.Y;
            return -Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        }

        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
