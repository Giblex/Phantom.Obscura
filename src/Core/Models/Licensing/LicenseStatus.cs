using System;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.Core.Models.Licensing
{
    public enum LicenseFailureReason
    {
        None = 0,
        Empty,
        NotProvisioned,
        MalformedToken,
        BadSignature,
        Expired,
        BindingMismatch,
        ClockRollback
    }

    /// <summary>
    /// Result of verifying a license token. <see cref="IsValid"/> is the single
    /// source of truth for whether premium features unlock. Always defaults to a
    /// safe Free status on any failure.
    /// </summary>
    public sealed class LicenseStatus
    {
        public bool IsValid { get; init; }
        public PremiumTier Tier { get; init; } = PremiumTier.Free;
        public DateTimeOffset? ExpiresUtc { get; init; }
        public int DaysRemaining { get; init; }
        public bool InGracePeriod { get; init; }
        public LicenseFailureReason Reason { get; init; } = LicenseFailureReason.Empty;
        public IReadOnlyCollection<PremiumFeature> Features { get; init; } = Array.Empty<PremiumFeature>();

        public bool Has(PremiumFeature feature) =>
            IsValid && Tier == PremiumTier.Premium &&
            (Features.Count == 0 || Features.Contains(feature));

        public static LicenseStatus Free(LicenseFailureReason reason) => new()
        {
            IsValid = false,
            Tier = PremiumTier.Free,
            Reason = reason,
            Features = Array.Empty<PremiumFeature>()
        };

        public static LicenseStatus Premium() => new()
        {
            IsValid = true,
            Tier = PremiumTier.Premium,
            Reason = LicenseFailureReason.None,
            Features = Array.Empty<PremiumFeature>()
        };
    }
}
