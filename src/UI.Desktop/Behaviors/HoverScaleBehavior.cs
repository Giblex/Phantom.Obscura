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
    /// Behavior that scales up a control slightly on hover.
    /// Respects ReduceMotion accessibility settings.
    /// </summary>
    public class HoverScaleBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> HoverScaleProperty =
            AvaloniaProperty.Register<HoverScaleBehavior, double>(nameof(HoverScale), 1.02);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<HoverScaleBehavior, double>(nameof(Duration), 0.2);

        private ScaleTransform? _transform;
        private bool _isHovering;

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

            AnimateScale(1.0, HoverScale);
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (!_isHovering || _transform == null) return;
            _isHovering = false;

            AnimateScale(HoverScale, 1.0);
        }

        private async void AnimateScale(double fromScale, double toScale)
        {
            if (_transform == null || AssociatedObject == null) return;

            // Check ReduceMotion - if enabled, use instant transition
            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;
            if (reduceMotion)
            {
                _transform.ScaleX = toScale;
                _transform.ScaleY = toScale;
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
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, fromScale),
                            new Setter(ScaleTransform.ScaleYProperty, fromScale)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, toScale),
                            new Setter(ScaleTransform.ScaleYProperty, toScale)
                        }
                    }
                }
            };

            await animation.RunAsync(_transform);
        }
    }
}
