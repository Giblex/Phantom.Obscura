using System;
using Avalonia.Animation.Easings;

namespace PhantomVault.UI.Behaviors
{

    public static class AnimationHelper
    {
        private const double DefaultDuration = 0.3;
        private const double ReducedMotionDuration = 0.1;

        public static double GetAnimationDuration()
        {
            return Services.AccessibilityService.Instance.ReduceMotion
                ? ReducedMotionDuration
                : DefaultDuration;
        }

        public static Easing GetEasing()
        {
            return Services.AccessibilityService.Instance.ReduceMotion
                ? new LinearEasing()
                : new CubicEaseOut();
        }

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

    public enum AnimationTiming
    {
        Instant,
        Fast,
        Normal,
        Slow,
        VerySlow
    }
}

