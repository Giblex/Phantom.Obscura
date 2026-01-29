using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

namespace PhantomVault.UI.Behaviors
{
    /// <summary>
    /// Behavior that lifts a control slightly upward on hover.
    /// Respects ReduceMotion accessibility settings.
    /// </summary>
    public class HoverLiftBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> LiftDistanceProperty =
            AvaloniaProperty.Register<HoverLiftBehavior, double>(nameof(LiftDistance), -2.0);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<HoverLiftBehavior, double>(nameof(Duration), 0.2);

        private TranslateTransform? _transform;
        private bool _isHovering;

        /// <summary>
        /// Distance to lift in pixels (negative = up, default: -2px)
        /// </summary>
        public double LiftDistance
        {
            get => GetValue(LiftDistanceProperty);
            set => SetValue(LiftDistanceProperty, value);
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
                _transform = new TranslateTransform();
                AssociatedObject.RenderTransform = _transform;

                AssociatedObject.PointerEntered += OnPointerEntered;
                AssociatedObject.PointerExited += OnPointerExited;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

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

            AnimateTransform(0, LiftDistance);
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (!_isHovering || _transform == null) return;
            _isHovering = false;

            AnimateTransform(LiftDistance, 0);
        }

        private async void AnimateTransform(double fromY, double toY)
        {
            if (_transform == null || AssociatedObject == null) return;

            // Check ReduceMotion - if enabled, use instant transition
            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;
            if (reduceMotion)
            {
                _transform.Y = toY;
                return;
            }

            // Smooth animation
            var duration = AnimationHelper.GetDuration(AnimationTiming.Fast);
            var easing = AnimationHelper.GetEasing();

            var animation = new Avalonia.Animation.Animation
            {
                Duration = TimeSpan.FromSeconds(duration),
                Easing = easing,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters = { new Setter(TranslateTransform.YProperty, fromY) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters = { new Setter(TranslateTransform.YProperty, toY) }
                    }
                }
            };

            await animation.RunAsync(_transform);
        }
    }
}
