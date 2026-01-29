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
    /// <summary>
    /// Direction for slide-in animation
    /// </summary>
    public enum SlideDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// Behavior that slides in a control from a specified direction.
    /// Respects ReduceMotion accessibility settings.
    /// </summary>
    public class SlideInBehavior : Behavior<Control>
    {
        public static readonly StyledProperty<SlideDirection> DirectionProperty =
            AvaloniaProperty.Register<SlideInBehavior, SlideDirection>(nameof(Direction), SlideDirection.Bottom);

        public static readonly StyledProperty<double> DistanceProperty =
            AvaloniaProperty.Register<SlideInBehavior, double>(nameof(Distance), 20.0);

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<SlideInBehavior, double>(nameof(Duration), 0.4);

        public static readonly StyledProperty<double> DelayProperty =
            AvaloniaProperty.Register<SlideInBehavior, double>(nameof(Delay), 0.0);

        /// <summary>
        /// Direction from which to slide in
        /// </summary>
        public SlideDirection Direction
        {
            get => GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        /// <summary>
        /// Distance to slide in pixels (default: 20px)
        /// </summary>
        public double Distance
        {
            get => GetValue(DistanceProperty);
            set => SetValue(DistanceProperty, value);
        }

        /// <summary>
        /// Animation duration in seconds (default: 0.4s)
        /// </summary>
        public double Duration
        {
            get => GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        /// <summary>
        /// Animation delay in seconds (default: 0s)
        /// </summary>
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
                AssociatedObject.RenderTransform = new TranslateTransform();
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

            // Get duration based on ReduceMotion setting
            var duration = AnimationHelper.GetDuration(AnimationTiming.Slow);
            var easing = AnimationHelper.GetEasing();

            // Apply delay if specified
            if (Delay > 0)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(Delay));
            }

            // Determine start position based on direction
            double startX = 0, startY = 0;
            switch (Direction)
            {
                case SlideDirection.Left:
                    startX = -Distance;
                    break;
                case SlideDirection.Right:
                    startX = Distance;
                    break;
                case SlideDirection.Top:
                    startY = -Distance;
                    break;
                case SlideDirection.Bottom:
                    startY = Distance;
                    break;
            }

            // Set initial transform
            if (AssociatedObject.RenderTransform is TranslateTransform transform)
            {
                transform.X = startX;
                transform.Y = startY;
            }

            // Create slide + fade animation
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
                            new Setter(TranslateTransform.XProperty, startX),
                            new Setter(TranslateTransform.YProperty, startY)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Control.OpacityProperty, 1.0),
                            new Setter(TranslateTransform.XProperty, 0.0),
                            new Setter(TranslateTransform.YProperty, 0.0)
                        }
                    }
                }
            };

            await animation.RunAsync(AssociatedObject);
        }
    }
}
