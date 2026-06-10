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

    public class HoverLiftBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> LiftDistanceProperty =
            AvaloniaProperty.Register<HoverLiftBehavior, double>(nameof(LiftDistance), -2.0);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<HoverLiftBehavior, double>(nameof(Duration), 0.2);

        private TranslateTransform? _transform;
        private bool _isHovering;
        private DispatcherTimer? _animTimer;
        private double _animFrom;
        private double _animTo;
        private DateTime _animStart;
        private double _animDuration;
        private readonly CubicEaseOut _easing = new();

        public double LiftDistance
        {
            get => GetValue(LiftDistanceProperty);
            set => SetValue(LiftDistanceProperty, value);
        }

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
                _transform = new TranslateTransform();
                AssociatedObject.RenderTransform = _transform;

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

            AnimateTransform(_transform.Y, LiftDistance);
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (!_isHovering || _transform == null) return;
            _isHovering = false;

            AnimateTransform(_transform.Y, 0);
        }

        private void AnimateTransform(double fromY, double toY)
        {
            if (_transform == null || AssociatedObject == null) return;

            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;
            if (reduceMotion)
            {
                StopAnimation();
                _transform.Y = toY;
                return;
            }

            _animFrom = fromY;
            _animTo = toY;
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

            var easedT = _easing.Ease(t);
            _transform.Y = _animFrom + (_animTo - _animFrom) * easedT;

            if (t >= 1.0)
            {
                _transform.Y = _animTo;
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

