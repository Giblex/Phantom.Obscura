using System;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.UI.Services.Entitlements
{
    /// <summary>
    /// App-wide source of truth for premium entitlements. ViewModels query this
    /// to gate features. The current status is derived from the active vault's
    /// signed license token (verified, never trusted blindly).
    /// </summary>
    public interface IEntitlementService
    {
        LicenseStatus Status { get; }

        PremiumTier CurrentTier { get; }

        bool IsPremium { get; }

        /// <summary>True if the given feature is unlocked under the current license.</summary>
        bool IsUnlocked(PremiumFeature feature);

        /// <summary>Raised whenever the entitlement status changes (unlock, lock, renew).</summary>
        event EventHandler? Changed;

        /// <summary>
        /// Verifies and applies a license token (typically from the manifest on
        /// unlock) against the active vault's USB/device binding.
        /// </summary>
        void ApplyToken(string? token, string? usbBindingId);

        /// <summary>Resets to Free (e.g. on vault lock).</summary>
        void Clear();

        /// <summary>
        /// Directly overrides the entitlement status without going through token
        /// verification. Only for local developer/testing paths (e.g. Dev Bypass) —
        /// never call this from a code path that handles a real vault's license.
        /// </summary>
        void ForceStatus(LicenseStatus status);
    }
}
