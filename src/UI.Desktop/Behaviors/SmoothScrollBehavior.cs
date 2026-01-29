using System;
using Avalonia.Animation.Easings;

namespace PhantomVault.UI.Behaviors
{
    /// <summary>
    /// Utility class providing animation timing helpers with ReduceMotion support.
    /// </summary>
    public static class AnimationHelper
    {
        private const double DefaultDuration = 0.3; // 300ms
        private const double ReducedMotionDuration = 0.1; // 100ms (faster for reduced motion)

        /// <summary>
        /// Gets the appropriate animation duration based on ReduceMotion setting.
        /// </summary>
        public static double GetAnimationDuration()
        {
            return Services.AccessibilityService.Instance.ReduceMotion
                ? ReducedMotionDuration
                : DefaultDuration;
        }

        /// <summary>
        /// Gets the appropriate easing function based on ReduceMotion setting.
        /// </summary>
        public static Easing GetEasing()
        {
            return Services.AccessibilityService.Instance.ReduceMotion
                ? new LinearEasing()
                : new CubicEaseOut();
        }

        /// <summary>
        /// Gets animation duration for a specific timing category.
        /// </summary>
        public static double GetDuration(AnimationTiming timing)
        {
            bool reduceMotion = Services.AccessibilityService.Instance.ReduceMotion;

            return timing switch
            {
                AnimationTiming.Instant => reduceMotion ? 0.04 : 0.08,
                AnimationTiming.Fast => reduceMotion ? 0.08 : 0.15,
                AnimationTiming.Normal => reduceMotion ? 0.12 : 0.25,
                AnimationTiming.Slow => reduceMotion ? 0.2 : 0.4,
                AnimationTiming.VerySlow => reduceMotion ? 0.3 : 0.6,
                _ => reduceMotion ? 0.12 : 0.25
            };
        }
    }

    /// <summary>
    /// Standard animation timing categories.
    /// </summary>
    public enum AnimationTiming
    {
        Instant,
        Fast,
        Normal,
        Slow,
        VerySlow
    }
}
