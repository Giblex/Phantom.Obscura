using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

namespace PhantomVault.UI.Behaviors
{

    public class FadeInBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<FadeInBehavior, double>(nameof(Duration), 0.3);

        public static readonly StyledProperty<double> DelayProperty =
            AvaloniaProperty.Register<FadeInBehavior, double>(nameof(Delay), 0.0);

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

            var duration = AnimationHelper.GetAnimationDuration();
            var easing = AnimationHelper.GetEasing();

            if (Delay > 0)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(Delay));
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
                            new Setter(Control.OpacityProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Control.OpacityProperty, 1.0)
                        }
                    }
                }
            };

            await animation.RunAsync(AssociatedObject);
        }
    }
}

