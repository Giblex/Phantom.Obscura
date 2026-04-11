using System;
using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Giblex.Controls;

/// <summary>
/// Liquid physics easing functions for organic motion
/// </summary>
public static class LiquidEase
{
    /// <summary>
    /// Elastic ease-out with overshoot and settle
    /// </summary>
    public static double EaseOutElastic(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        const double p = 0.3;
        return Math.Pow(2, -10 * t) * Math.Sin((t - p / 4) * (2 * Math.PI) / p) + 1;
    }

    /// <summary>
    /// Spring-like ease with configurable damping
    /// </summary>
    public static double EaseOutSpring(double t, double damping = 0.6)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return 1 - Math.Pow(Math.E, -6 * t) * Math.Cos(12 * t * damping);
    }

    /// <summary>
    /// Smooth interpolation with slight overshoot
    /// </summary>
    public static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}

public partial class LiquidGlassButton : UserControl
{
    // Content properties
    public static readonly StyledProperty<object?> ButtonContentProperty =
        AvaloniaProperty.Register<LiquidGlassButton, object?>(nameof(ButtonContent));

    public object? ButtonContent
    {
        get => GetValue(ButtonContentProperty);
        set => SetValue(ButtonContentProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ButtonContentTemplateProperty =
        AvaloniaProperty.Register<LiquidGlassButton, IDataTemplate?>(nameof(ButtonContentTemplate));

    public IDataTemplate? ButtonContentTemplate
    {
        get => GetValue(ButtonContentTemplateProperty);
        set => SetValue(ButtonContentTemplateProperty, value);
    }

    public static readonly StyledProperty<bool> IsPrimaryProperty =
        AvaloniaProperty.Register<LiquidGlassButton, bool>(nameof(IsPrimary));

    public bool IsPrimary
    {
        get => GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<LiquidGlassButton, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<LiquidGlassButton, object?>(nameof(CommandParameter));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public static readonly StyledProperty<double> WidthOverrideProperty =
        AvaloniaProperty.Register<LiquidGlassButton, double>(nameof(WidthOverride), double.NaN);

    public double WidthOverride
    {
        get => GetValue(WidthOverrideProperty);
        set => SetValue(WidthOverrideProperty, value);
    }

    public static readonly StyledProperty<double> HeightOverrideProperty =
        AvaloniaProperty.Register<LiquidGlassButton, double>(nameof(HeightOverride), double.NaN);

    public double HeightOverride
    {
        get => GetValue(HeightOverrideProperty);
        set => SetValue(HeightOverrideProperty, value);
    }

    public static readonly StyledProperty<double> CornerRadiusValueProperty =
        AvaloniaProperty.Register<LiquidGlassButton, double>(nameof(CornerRadiusValue), 10);

    public double CornerRadiusValue
    {
        get => GetValue(CornerRadiusValueProperty);
        set => SetValue(CornerRadiusValueProperty, value);
    }

    public static readonly StyledProperty<Thickness> PaddingOverrideProperty =
        AvaloniaProperty.Register<LiquidGlassButton, Thickness>(nameof(PaddingOverride), new Thickness(18, 10));

    public Thickness PaddingOverride
    {
        get => GetValue(PaddingOverrideProperty);
        set => SetValue(PaddingOverrideProperty, value);
    }

    public static readonly StyledProperty<string?> ToolTipTextProperty =
        AvaloniaProperty.Register<LiquidGlassButton, string?>(nameof(ToolTipText));

    public string? ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? Click;

    // Sheen position (for XAML binding)
    public static readonly AttachedProperty<double> SheenXProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassButton, Control, double>("SheenX");

    public static readonly AttachedProperty<double> SheenYProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassButton, Control, double>("SheenY");

    public static readonly AttachedProperty<double> BorderHighlightAngleProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassButton, Control, double>("BorderHighlightAngle");

    public static readonly AttachedProperty<double> BorderHighlightOpacityProperty =
        AvaloniaProperty.RegisterAttached<LiquidGlassButton, Control, double>("BorderHighlightOpacity", 0.7);

    public static double GetSheenX(AvaloniaObject o) => o.GetValue(SheenXProperty);
    public static void SetSheenX(AvaloniaObject o, double v) => o.SetValue(SheenXProperty, v);
    public static double GetSheenY(AvaloniaObject o) => o.GetValue(SheenYProperty);
    public static void SetSheenY(AvaloniaObject o, double v) => o.SetValue(SheenYProperty, v);
    public static double GetBorderHighlightAngle(AvaloniaObject o) => o.GetValue(BorderHighlightAngleProperty);
    public static void SetBorderHighlightAngle(AvaloniaObject o, double v) => o.SetValue(BorderHighlightAngleProperty, v);
    public static double GetBorderHighlightOpacity(AvaloniaObject o) => o.GetValue(BorderHighlightOpacityProperty);
    public static void SetBorderHighlightOpacity(AvaloniaObject o, double v) => o.SetValue(BorderHighlightOpacityProperty, v);

    // ═══════════════════════════════════════════════════════════════════
    // LIQUID PHYSICS STATE
    // ═══════════════════════════════════════════════════════════════════

    private Button? _btn;
    private ScaleTransform? _scaleTransform;
    private DispatcherTimer? _physicsTimer;
    private readonly CompositeDisposable _subscriptions = new();

    // Sheen inertia (velocity-based smoothing)
    private double _sheenVx, _sheenVy;
    private double _sheenX, _sheenY;
    private double _targetSheenX, _targetSheenY;

    // Scale animation state
    private double _currentScale = 1.0;
    private double _targetScale = 1.0;
    private double _scaleVelocity = 0;

    // Wobble state (micro-oscillation while hovered)
    private double _wobbleTime = 0;
    private bool _isHovered = false;
    private bool _isPressed = false;

    // Border/glow animation
    private double _currentBorderOpacity = 0.5;
    private double _targetBorderOpacity = 0.5;
    private double _borderHighlightAngle;
    private double _targetBorderHighlightAngle;
    private double _borderHighlightAngularVelocity;
    private double _borderHighlightOpacity = 0.72;
    private double _targetBorderHighlightOpacity = 0.72;

    // Physics profiles: Default for full motion, Reduced for accessibility
    private record PhysicsProfile(
        double SheenStiffness, double SheenDamping,
        double ScaleStiffness, double ScaleDamping,
        double WobbleAmplitude, double WobbleFrequency);

    private static readonly PhysicsProfile DefaultPhysics = new(
        SheenStiffness: 0.12, SheenDamping: 0.78,
        ScaleStiffness: 0.18, ScaleDamping: 0.65,
        WobbleAmplitude: 0.008, WobbleFrequency: 3.5);

    private static readonly PhysicsProfile ReducedPhysics = new(
        SheenStiffness: 0.25, SheenDamping: 0.90,
        ScaleStiffness: 0.30, ScaleDamping: 0.85,
        WobbleAmplitude: 0.0, WobbleFrequency: 0.0);

    private PhysicsProfile _physics = DefaultPhysics;

    // Settling threshold — when all velocities/deltas are below this, stop the timer
    private const double SettleEpsilon = 0.001;

    public LiquidGlassButton()
    {
        InitializeComponent();

        _btn = this.FindControl<Button>("PART_Button");
        if (_btn == null) return;

        // Setup scale transform for liquid deformation
        _scaleTransform = new ScaleTransform(1, 1);
        _btn.RenderTransform = _scaleTransform;
        _btn.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        // Click handler
        _btn.Click += (_, e) =>
        {
            Click?.Invoke(this, e);
            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
        };

        // Forward properties to inner button
        SetupPropertyBindings();

        // Pointer events for liquid physics
        _btn.PointerEntered += OnPointerEntered;
        _btn.PointerExited += OnPointerExited;
        _btn.PointerMoved += OnPointerMoved;
        _btn.PointerPressed += OnPointerPressed;
        _btn.PointerReleased += OnPointerReleased;

        // Create physics simulation timer (~60fps) but don't start until needed.
        // When ReduceMotion is preferred, skip the physics timer entirely.
        _physicsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _physicsTimer.Tick += OnPhysicsTick;
        // Timer starts on-demand via EnsurePhysicsRunning() when pointer enters
    }

    /// <summary>
    /// Starts the physics timer if it's not already running (and ReduceMotion is off).
    /// </summary>
    private void EnsurePhysicsRunning()
    {
        if (ShouldReduceMotion()) return;
        if (_physicsTimer is { IsEnabled: false })
            _physicsTimer.Start();
    }

    private void SetupPropertyBindings()
    {
        if (_btn == null) return;

        this.GetObservable(ButtonContentProperty)
            .Subscribe(content => _btn!.Content = content)
            .DisposeWith(_subscriptions);
        this.GetObservable(ButtonContentTemplateProperty)
            .Subscribe(template => _btn!.ContentTemplate = template)
            .DisposeWith(_subscriptions);
        this.GetObservable(IsPrimaryProperty)
            .Subscribe(isPrimary =>
            {
                if (isPrimary) _btn!.Classes.Add("primary");
                else _btn!.Classes.Remove("primary");
            })
            .DisposeWith(_subscriptions);
        this.GetObservable(WidthOverrideProperty)
            .Subscribe(w => _btn!.Width = double.IsNaN(w) ? double.NaN : w)
            .DisposeWith(_subscriptions);
        this.GetObservable(HeightOverrideProperty)
            .Subscribe(h => _btn!.Height = double.IsNaN(h) ? double.NaN : h)
            .DisposeWith(_subscriptions);
        this.GetObservable(CornerRadiusValueProperty)
            .Subscribe(cr => _btn!.CornerRadius = new CornerRadius(cr))
            .DisposeWith(_subscriptions);
        this.GetObservable(PaddingOverrideProperty)
            .Subscribe(p => _btn!.Padding = p)
            .DisposeWith(_subscriptions);
        this.GetObservable(ToolTipTextProperty)
            .Subscribe(tip => ToolTip.SetTip(_btn!, tip))
            .DisposeWith(_subscriptions);
    }

    // ═══════════════════════════════════════════════════════════════════
    // POINTER EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════════════

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isHovered = true;
        _wobbleTime = 0;

        // Micro-inflate on hover (1.0 → 1.03)
        _targetScale = 1.03;
        _targetBorderOpacity = 0.65;
        _targetBorderHighlightOpacity = 0.9;

        if (ShouldReduceMotion())
        {
            ApplyScaleImmediate(_targetScale);
            if (_btn != null)
            {
                SetBorderHighlightOpacity(_btn, _targetBorderHighlightOpacity);
            }
            return;
        }
        EnsurePhysicsRunning();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isHovered = false;
        _isPressed = false;

        // Return to rest
        _targetScale = 1.0;
        _targetSheenX = 0;
        _targetSheenY = 0;
        _targetBorderOpacity = 0.5;
        _targetBorderHighlightAngle = 0;
        _targetBorderHighlightOpacity = 0.72;

        if (ShouldReduceMotion())
        {
            ApplyScaleImmediate(_targetScale);
            if (_btn != null)
            {
                SetBorderHighlightAngle(_btn, 0);
                SetBorderHighlightOpacity(_btn, _targetBorderHighlightOpacity);
            }
            return;
        }
        EnsurePhysicsRunning();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (ShouldReduceMotion()) return;
        if (_btn == null || _btn.Bounds.Width <= 1 || _btn.Bounds.Height <= 1) return;

        var p = e.GetPosition(_btn);

        // Map pointer to sheen offset range (±12px max)
        _targetSheenX = ((p.X / _btn.Bounds.Width) - 0.5) * 24;
        _targetSheenY = ((p.Y / _btn.Bounds.Height) - 0.5) * 16;

        var normalizedX = ((p.X / _btn.Bounds.Width) - 0.5) * 2;
        var normalizedY = ((p.Y / _btn.Bounds.Height) - 0.5) * 2;
        var radialDistance = Math.Clamp(Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY)), 0, 1.25);

        // Rotate the bright border segment around the button based on pointer position.
        _targetBorderHighlightAngle = (Math.Atan2(normalizedY, normalizedX) * (180.0 / Math.PI) + 45.0) * 1.18;
        _targetBorderHighlightOpacity = 0.72 + (Math.Min(radialDistance, 1.0) * 0.45);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPressed = true;

        // Compress on press (1.03 → 0.97)
        _targetScale = 0.97;
        _targetBorderOpacity = 0.4;
        _targetBorderHighlightOpacity = 1.0;

        if (ShouldReduceMotion())
        {
            ApplyScaleImmediate(_targetScale);
            return;
        }

        // Add velocity impulse for snappy response
        _scaleVelocity = -0.08;
        EnsurePhysicsRunning();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPressed = false;

        if (_isHovered)
        {
            // Overshoot on release (0.97 → 1.04 → 1.03)
            _targetScale = 1.03;
            _scaleVelocity = 0.04; // Upward impulse for overshoot
        }
        else
        {
            _targetScale = 1.0;
        }
        _targetBorderOpacity = _isHovered ? 0.65 : 0.5;
        _targetBorderHighlightOpacity = _isHovered ? 0.9 : 0.72;

        if (ShouldReduceMotion())
        {
            ApplyScaleImmediate(_targetScale);
            if (_btn != null)
            {
                SetBorderHighlightOpacity(_btn, _targetBorderHighlightOpacity);
            }
            return;
        }
        EnsurePhysicsRunning();
    }

    /// <summary>
    /// Instantly set scale without physics — used when ReduceMotion is preferred.
    /// </summary>
    private void ApplyScaleImmediate(double scale)
    {
        if (_scaleTransform == null) return;
        _currentScale = scale;
        _scaleVelocity = 0;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHYSICS SIMULATION (60fps tick)
    // ═══════════════════════════════════════════════════════════════════

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        if (_btn == null || _scaleTransform == null) return;

        // ─── Sheen inertia (spring-damper system) ───
        // The sheen "floats" and catches up like liquid under glass
        double sheenDx = _targetSheenX - _sheenX;
        double sheenDy = _targetSheenY - _sheenY;

        _sheenVx = (_sheenVx + sheenDx * _physics.SheenStiffness) * _physics.SheenDamping;
        _sheenVy = (_sheenVy + sheenDy * _physics.SheenStiffness) * _physics.SheenDamping;

        _sheenX += _sheenVx;
        _sheenY += _sheenVy;

        SetSheenX(_btn, _sheenX);
        SetSheenY(_btn, _sheenY);

        // ─── Scale spring animation ───
        double scaleDelta = _targetScale - _currentScale;
        _scaleVelocity = (_scaleVelocity + scaleDelta * _physics.ScaleStiffness) * _physics.ScaleDamping;
        _currentScale += _scaleVelocity;

        // Clamp to ±3% as per guidelines
        _currentScale = Math.Clamp(_currentScale, 0.97, 1.04);

        _scaleTransform.ScaleX = _currentScale;
        _scaleTransform.ScaleY = _currentScale;

        // ─── Micro-wobble while hovered (living surface effect) ───
        if (_isHovered && !_isPressed)
        {
            _wobbleTime += 0.016; // ~16ms per frame
            double wobble = Math.Sin(_wobbleTime * _physics.WobbleFrequency * 2 * Math.PI) * _physics.WobbleAmplitude;

            // Apply wobble as tiny scale variation
            _scaleTransform.ScaleX = _currentScale + wobble;
            _scaleTransform.ScaleY = _currentScale - wobble * 0.5; // Asymmetric for organic feel
        }

        // ─── Border opacity animation ───
        _currentBorderOpacity += (_targetBorderOpacity - _currentBorderOpacity) * 0.15;

        // Note: Border opacity would be applied via attached property if needed
        // For now the XAML handles this via :pointerover states

        // ─── Border highlight orbit ───
        var angularDelta = NormalizeAngleDelta(_targetBorderHighlightAngle - _borderHighlightAngle);
        _borderHighlightAngularVelocity = (_borderHighlightAngularVelocity + angularDelta * 0.24) * 0.76;
        _borderHighlightAngle = NormalizeAngle(_borderHighlightAngle + _borderHighlightAngularVelocity);
        _borderHighlightOpacity += (_targetBorderHighlightOpacity - _borderHighlightOpacity) * 0.24;

        SetBorderHighlightAngle(_btn, _borderHighlightAngle);
        SetBorderHighlightOpacity(_btn, _borderHighlightOpacity);

        // ─── Idle settling: stop timer when physics are at rest ───
        if (!_isHovered && !_isPressed)
        {
            bool settled = Math.Abs(_sheenVx) < SettleEpsilon
                        && Math.Abs(_sheenVy) < SettleEpsilon
                        && Math.Abs(_scaleVelocity) < SettleEpsilon
                        && Math.Abs(_targetScale - _currentScale) < SettleEpsilon
                        && Math.Abs(_targetSheenX - _sheenX) < SettleEpsilon
                        && Math.Abs(_targetSheenY - _sheenY) < SettleEpsilon
                        && Math.Abs(NormalizeAngleDelta(_targetBorderHighlightAngle - _borderHighlightAngle)) < 0.05
                        && Math.Abs(_targetBorderHighlightOpacity - _borderHighlightOpacity) < 0.01;
            if (settled)
            {
                // Snap to exact rest values and stop ticking
                _currentScale = _targetScale;
                _sheenX = _targetSheenX;
                _sheenY = _targetSheenY;
                _scaleVelocity = 0;
                _sheenVx = 0;
                _sheenVy = 0;
                _borderHighlightAngularVelocity = 0;
                if (_scaleTransform != null)
                {
                    _scaleTransform.ScaleX = _currentScale;
                    _scaleTransform.ScaleY = _currentScale;
                }
                SetBorderHighlightAngle(_btn, _targetBorderHighlightAngle);
                SetBorderHighlightOpacity(_btn, _targetBorderHighlightOpacity);
                _physicsTimer?.Stop();
            }
        }
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180)
        {
            angle -= 360;
        }

        while (angle < -180)
        {
            angle += 360;
        }

        return angle;
    }

    private static double NormalizeAngleDelta(double delta)
    {
        while (delta > 180)
        {
            delta -= 360;
        }

        while (delta < -180)
        {
            delta += 360;
        }

        return delta;
    }

    /// <summary>
    /// Check if reduced motion is preferred (accessibility)
    /// </summary>
    private static bool ShouldReduceMotion()
    {
        // Check global accessibility service for reduce motion preference
        return PhantomVault.UI.Services.AccessibilityService.Instance.ReduceMotion;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Dispose observable subscriptions
        _subscriptions.Dispose();

        // Stop and detach physics timer
        if (_physicsTimer != null)
        {
            _physicsTimer.Tick -= OnPhysicsTick;
            _physicsTimer.Stop();
            _physicsTimer = null;
        }

        // Unsubscribe pointer events
        if (_btn != null)
        {
            _btn.PointerEntered -= OnPointerEntered;
            _btn.PointerExited -= OnPointerExited;
            _btn.PointerMoved -= OnPointerMoved;
            _btn.PointerPressed -= OnPointerPressed;
            _btn.PointerReleased -= OnPointerReleased;
        }
    }
}
