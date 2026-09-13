using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Motion shared by every AutoFill surface, so they open, list and close the same way:
    /// a soft single-spring open, rows that fade up one after another, a quick fade-and-shrink
    /// close, and eased hover changes. Honours the app's Reduce Animations setting.
    /// </summary>
    internal static class AutofillMotion
    {
        private static bool Reduced =>
            Application.Current?.TryGetResource("ReduceAnimations", null, out var reduce) == true && reduce is true;

        /// <summary>Springs a card in: grows from 94% while sliding <paramref name="fromY"/> px into place, fading in.</summary>
        public static async Task EnterAsync(Visual shell, double fromY = -8, double fromScale = 0.94, int durationMs = 520)
        {
            if (Reduced)
            {
                shell.Opacity = 1;
                shell.RenderTransform = null;
                return;
            }

            shell.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(fromScale, fromScale), new TranslateTransform(0, fromY) }
            };
            shell.Opacity = 0;

            var spring = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Easing = new SpringOutEasing { Overshoot = 1.1 },
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, fromScale),
                            new Setter(ScaleTransform.ScaleYProperty, fromScale),
                            new Setter(TranslateTransform.YProperty, fromY)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0),
                            new Setter(TranslateTransform.YProperty, 0.0)
                        }
                    }
                }
            };

            try
            {
                await Task.WhenAll(spring.RunAsync(shell), Fade(0, 1, 200, new CubicEaseOut()).RunAsync(shell));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutofillMotion] Enter failed: {ex.Message}");
            }
            finally
            {
                shell.Opacity = 1;
                shell.RenderTransform = null;
            }
        }

        /// <summary>Fades and slightly shrinks a card before it closes.</summary>
        public static async Task ExitAsync(Visual shell, int durationMs = 150)
        {
            if (Reduced) return;

            shell.RenderTransform = new ScaleTransform(1, 1);
            var shrink = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(ScaleTransform.ScaleXProperty, 0.97), new Setter(ScaleTransform.ScaleYProperty, 0.97) }
                    }
                }
            };

            try
            {
                await Task.WhenAll(shrink.RunAsync(shell), Fade(1, 0, durationMs, new CubicEaseIn()).RunAsync(shell));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutofillMotion] Exit failed: {ex.Message}");
            }
        }

        /// <summary>Rows fade up into place one after another, just behind the card's open.</summary>
        public static void StaggerIn(IEnumerable<Visual> items, int firstDelayMs = 90, int stepMs = 45)
        {
            if (Reduced) return;

            int index = 0;
            foreach (var item in items)
            {
                var visual = item;
                visual.Opacity = 0;
                visual.RenderTransform = new TranslateTransform(0, 8);
                var delay = TimeSpan.FromMilliseconds(firstDelayMs + stepMs * index++);
                DispatcherTimer.RunOnce(() => _ = RiseAsync(visual), delay);
            }
        }

        private static async Task RiseAsync(Visual visual)
        {
            var rise = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, 8.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0.0) } }
                }
            };

            try
            {
                await Task.WhenAll(rise.RunAsync(visual), Fade(0, 1, 260, new CubicEaseOut()).RunAsync(visual));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutofillMotion] Row rise failed: {ex.Message}");
            }
            finally
            {
                visual.Opacity = 1;
                visual.RenderTransform = null;
            }
        }

        /// <summary>Eased background changes for hover and keyboard selection.</summary>
        public static Transitions BackgroundTransitions() => new()
        {
            new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }
        };

        /// <summary>Eased fades for pieces that appear on hover (accent bar, action strip).</summary>
        public static Transitions OpacityTransitions() => new()
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }
        };

        private static Animation Fade(double from, double to, int durationMs, Easing easing) => new()
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } }
            }
        };
    }
}
