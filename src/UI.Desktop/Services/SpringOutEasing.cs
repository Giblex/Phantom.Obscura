using Avalonia.Animation.Easings;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// A single spring open: eases out past the target once and settles back, with no second
    /// dip (a "back out" curve). <see cref="Overshoot"/> sets how far past it goes
    /// (1.0 ≈ 4% of the travel, 1.7 ≈ 10%). Used for the tile click, the editor pop and the
    /// AutoFill surfaces.
    /// </summary>
    public sealed class SpringOutEasing : Easing
    {
        public double Overshoot { get; init; } = 1.4;

        public override double Ease(double progress)
        {
            if (progress <= 0) return 0;
            if (progress >= 1) return 1;
            var t = progress - 1;
            return 1 + (Overshoot + 1) * t * t * t + Overshoot * t * t;
        }
    }
}
