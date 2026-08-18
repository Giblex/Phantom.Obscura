using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.UI.Services.Licensing
{
    /// <summary>
    /// Placeholder licensing client. With no real backend wired, it reports
    /// "not configured" in production. When the env-gated <see cref="DevLicenseAuthority"/>
    /// is enabled, it issues locally-signed dev tokens so the end-to-end
    /// purchase/unlock UX can be tested.
    /// </summary>
    public sealed class StubLicensingClient : ILicensingClient
    {
        private static readonly TimeSpan DevTokenDuration = TimeSpan.FromDays(30);

        private readonly DevLicenseAuthority _devAuthority;

        public StubLicensingClient(DevLicenseAuthority devAuthority)
        {
            _devAuthority = devAuthority ?? throw new ArgumentNullException(nameof(devAuthority));
        }

        public bool IsConfigured => DevLicenseAuthority.IsEnabled;

        public Task<LicensingResult> ActivateAsync(PremiumTier tier, string? usbBindingId,
            BillingInterval interval = BillingInterval.Monthly, CancellationToken ct = default)
        {
            if (!DevLicenseAuthority.IsEnabled)
            {
                return Task.FromResult(LicensingResult.NotConfigured(
                    "Online checkout isn't available yet in this build. " +
                    "Payment integration is coming soon."));
            }

            string token = _devAuthority.IssueToken(tier, usbBindingId, DevTokenDuration);
            return Task.FromResult(LicensingResult.Success(token));
        }

        public Task<LicensingResult> RenewAsync(string? currentToken, string? usbBindingId, CancellationToken ct = default)
            => ActivateAsync(PremiumTier.Premium, usbBindingId, BillingInterval.Monthly, ct);
    }
}
