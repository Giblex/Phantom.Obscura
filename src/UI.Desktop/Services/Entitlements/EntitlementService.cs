using System;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Licensing;

namespace PhantomVault.UI.Services.Entitlements
{
    /// <summary>
    /// Default entitlement service. Holds the verified <see cref="LicenseStatus"/>
    /// for the active vault and answers feature-gate queries. Failsafe: any
    /// verification failure leaves the app on the Free tier.
    /// </summary>
    public sealed class EntitlementService : IEntitlementService
    {
        // Offline tolerance: a valid license keeps working this long past its
        // expiry so a brief lack of connectivity at renewal time doesn't lock
        // a paying user out. Renewal should replace the token well before this.
        public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(7);

        private readonly ILicenseVerifier _verifier;
        private readonly IMonotonicTimeGuard? _timeGuard;
        private LicenseStatus _status = LicenseStatus.Free(LicenseFailureReason.Empty);

        public EntitlementService(ILicenseVerifier verifier, IMonotonicTimeGuard? timeGuard = null)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _timeGuard = timeGuard;
        }

        public LicenseStatus Status => _status;

        public PremiumTier CurrentTier => _status.Tier;

        public bool IsPremium => _status.IsValid && _status.Tier == PremiumTier.Premium;

        public bool IsUnlocked(PremiumFeature feature) => _status.Has(feature);

        public event EventHandler? Changed;

        public void ApplyToken(string? token, string? usbBindingId)
        {
            var status = _verifier.Verify(token, usbBindingId, OfflineGrace);

            // A time-based license is only as trustworthy as the system clock. If the clock
            // was wound back to revive an expired token, refuse to honour it.
            if (status.IsValid && _timeGuard is not null && _timeGuard.IsRollback(DateTimeOffset.UtcNow))
            {
                status = LicenseStatus.Free(LicenseFailureReason.ClockRollback);
            }

            _status = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _status = LicenseStatus.Free(LicenseFailureReason.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ForceStatus(LicenseStatus status)
        {
            _status = status ?? throw new ArgumentNullException(nameof(status));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
