using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.UI.Services.Licensing
{
    public enum LicensingResultKind
    {
        Success,
        NotConfigured,
        Cancelled,
        Failed
    }

    public sealed class LicensingResult
    {
        public LicensingResultKind Kind { get; init; }

        /// <summary>The signed token to persist, when <see cref="Kind"/> is Success.</summary>
        public string? Token { get; init; }

        public string? Message { get; init; }

        public static LicensingResult Success(string token) =>
            new() { Kind = LicensingResultKind.Success, Token = token };

        public static LicensingResult NotConfigured(string message) =>
            new() { Kind = LicensingResultKind.NotConfigured, Message = message };

        public static LicensingResult Failed(string message) =>
            new() { Kind = LicensingResultKind.Failed, Message = message };
    }

    /// <summary>
    /// Seam for the real payment/licensing backend. The shipped implementation
    /// is a stub; a future implementation will integrate a payment processor
    /// (Stripe/Paddle/etc.) and a licensing server that returns signed tokens.
    /// </summary>
    public interface ILicensingClient
    {
        /// <summary>True if a real backend (or the dev authority) can issue tokens.</summary>
        bool IsConfigured { get; }

        /// <summary>Purchase/activate a subscription and obtain a signed token.</summary>
        /// <param name="interval">
        /// Billing cadence the user chose. Defaults to Monthly so existing callers keep
        /// their behaviour; implementations map it to the matching price/checkout link.
        /// </param>
        Task<LicensingResult> ActivateAsync(
            PremiumTier tier,
            string? usbBindingId,
            BillingInterval interval = BillingInterval.Monthly,
            CancellationToken ct = default);

        /// <summary>Renew an existing subscription before expiry, obtaining a fresh token.</summary>
        Task<LicensingResult> RenewAsync(string? currentToken, string? usbBindingId, CancellationToken ct = default);
    }
}
