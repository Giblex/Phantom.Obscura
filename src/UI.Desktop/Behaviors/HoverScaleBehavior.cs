using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace PhantomVault.UI.Behaviors
{
    /// <summary>
    /// Behavior that scales up a control slightly on hover.
    /// Respects ReduceMotion accessibility settings.
    /// Uses DispatcherTimer interpolation because Avalonia's
    /// Animation.RunAsync() cannot target ScaleTransform (it is not a Visual).
    /// </summary>
    public class HoverScaleBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> HoverScaleProperty =
            AvaloniaProperty.Register<HoverScaleBehavior, double>(nameof(HoverScale), 1.02);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<HoverScaleBehavior, double>(nameof(Duration), 0.2);

        private ScaleTransform? _transform;
        private bool _isHovering;
        private DispatcherTimer? _animTimer;
        private double _animFromScale;
        private double _animToScale;
        private DateTime _animStart;
        private double _animDuration; // seconds
        private readonly CubicEaseOut _easing = new();

        /// <summary>
        /// Scale factor on hover (default: 1.02 = 102%)
        /// </summary>
        public double HoverScale
        {
            get => GetValue(HoverScaleProperty);
            set => SetValue(HoverScaleProperty, value);
        }

        /// <summary>
        /// Animation duration in seconds (default: 0.2s)
        /// </summary>
        public double Duration
        {
            get => GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject != null)
            {
                _transform = new ScaleTransform();
                AssociatedObject.RenderTransform = _transform;
                AssociatedObject.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

                AssociatedObject.PointerEntered += OnPointerEntered;
                AssociatedObject.PointerExited += OnPointerExited;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            StopAnimation();

            if (AssociatedObject != null)
            {
                AssociatedObject.PointerEntered -= OnPointerEntered;
                AssociatedObject.PointerExited -= OnPointerExited;
            }
        }

        private void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_isHovering || _transform == null) return;
            _isHovering = true;

            AnimateScale(_transform.ScaleX, HoverScale);
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (!_isHovering || _transform == null) return;
            _isHovering = false;

            AnimateScale(_transform.ScaleX, 1.0);
        }

        private void AnimateScale(double fromScale, double toScale)
        {
            if (_transform == null || AssociatedObject == null) return;

            // Check ReduceMotion — if enabled, snap immediately
            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;
            if (reduceMotion)
            {
                StopAnimation();
                _transform.ScaleX = toScale;
                _transform.ScaleY = toScale;
                return;
            }

            // Set up timer-driven interpolation (~60 fps)
            _animFromScale = fromScale;
            _animToScale = toScale;
            _animDuration = AnimationHelper.GetDuration(AnimationTiming.Fast);
            _animStart = DateTime.UtcNow;

            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _animTimer.Tick += OnAnimTick;
            }

            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            if (_transform == null)
            {
                StopAnimation();
                return;
            }

            var elapsed = (DateTime.UtcNow - _animStart).TotalSeconds;
            var t = Math.Clamp(elapsed / _animDuration, 0.0, 1.0);

            // Apply cubic-ease-out
            var easedT = _easing.Ease(t);
            var val = _animFromScale + (_animToScale - _animFromScale) * easedT;
            _transform.ScaleX = val;
            _transform.ScaleY = val;

            if (t >= 1.0)
            {
                _transform.ScaleX = _animToScale;
                _transform.ScaleY = _animToScale;
                _animTimer?.Stop();
            }
        }

        private void StopAnimation()
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimTick;
                _animTimer = null;
            }
        }
    }
}
