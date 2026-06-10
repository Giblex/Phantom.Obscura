using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

namespace PhantomVault.UI.Behaviors
{

    public class ScaleInBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> FromScaleProperty =
            AvaloniaProperty.Register<ScaleInBehavior, double>(nameof(FromScale), 0.95);

        public static readonly StyledProperty<bool> UseBounceProperty =
            AvaloniaProperty.Register<ScaleInBehavior, bool>(nameof(UseBounce), false);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<ScaleInBehavior, double>(nameof(Duration), 0.3);

        public static readonly StyledProperty<double> DelayProperty =
            AvaloniaProperty.Register<ScaleInBehavior, double>(nameof(Delay), 0.0);

        public double FromScale
        {
            get => GetValue(FromScaleProperty);
            set => SetValue(FromScaleProperty, value);
        }

        public bool UseBounce
        {
            get => GetValue(UseBounceProperty);
            set => SetValue(UseBounceProperty, value);
        }

        public double Duration
        {
            get => GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public double Delay
        {
            get => GetValue(DelayProperty);
            set => SetValue(DelayProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject != null)
            {
                AssociatedObject.Opacity = 0;
                AssociatedObject.RenderTransform = new ScaleTransform();
                AssociatedObject.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                AssociatedObject.AttachedToVisualTree += OnAttachedToVisualTree;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            if (AssociatedObject != null)
            {
                AssociatedObject.AttachedToVisualTree -= OnAttachedToVisualTree;
            }
        }

        private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (AssociatedObject == null) return;

            var reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;
            var duration = AnimationHelper.GetDuration(AnimationTiming.Normal);

            Easing easing;
            if (reduceMotion)
            {
                easing = new LinearEasing();
            }
            else
            {
                easing = UseBounce ? new BackEaseOut() : new CubicEaseOut();
            }

            if (Delay > 0)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(Delay));
            }

            if (AssociatedObject.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = FromScale;
                scaleTransform.ScaleY = FromScale;
            }

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
                            new Setter(Control.OpacityProperty, 0.0),
                            new Setter(ScaleTransform.ScaleXProperty, FromScale),
                            new Setter(ScaleTransform.ScaleYProperty, FromScale)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Control.OpacityProperty, 1.0),
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0)
                        }
                    }
                }
            };

            await animation.RunAsync(AssociatedObject);
        }
    }
}

