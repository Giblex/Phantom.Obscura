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

    public enum SlideDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

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

        public SlideDirection Direction
        {
            get => GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        public double Distance
        {
            get => GetValue(DistanceProperty);
            set => SetValue(DistanceProperty, value);
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

            var duration = AnimationHelper.GetDuration(AnimationTiming.Slow);
            var easing = AnimationHelper.GetEasing();

            if (Delay > 0)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(Delay));
            }

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

            if (AssociatedObject.RenderTransform is TranslateTransform transform)
            {
                transform.X = startX;
                transform.Y = startY;
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

