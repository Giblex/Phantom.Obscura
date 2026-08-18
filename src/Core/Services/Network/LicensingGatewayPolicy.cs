using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// InternetAccessRequest envelope for the premium licensing backend on giblex.com.
    /// Checkout session creation and license-token polling both funnel through
    /// <see cref="IInternetGateway"/> with SPKI pinning — never a standalone HttpClient.
    /// </summary>
    public static class LicensingGatewayPolicy
    {
        public const string LicensingHost = "giblex.com";

        public const string FeatureId = "licensing.stripe";

        public const string UserVisibleReason =
            "Connect to the Giblex licensing service to start Stripe checkout and retrieve your signed premium licence token. " +
            "No card data is handled in Phantom Obscura — payment stays on Stripe's hosted page.";

        // Leaf SPKI for giblex.com (extracted via tools/GetSpkiPin).
        private const string LeafPinGiblex =
            "efEqSKc6JUeLKJQbpzAziICCOITT3/g8lh2H4A23YXQ=";

        // Backup: Google Trust Services WE1 intermediate (shared with HIBP hosts).
        private const string IntermediatePinGtsWE1 =
            "kIdp6NNEd8wsugYyyIYFsi1ylMCED3hZbSR8ZFsa/A4=";

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [LicensingHost] = new[] { LeafPinGiblex, IntermediatePinGtsWE1 },
            };

        public static IReadOnlyList<string> AllowedHosts { get; } = new[] { LicensingHost };

        /// <summary>Covers checkout POST + up to 15 minutes of licence polling.</summary>
        public static TimeSpan DefaultTtl { get; } = TimeSpan.FromMinutes(15);

        public static InternetAccessRequest CreateRequest()
            => new()
            {
                FeatureId = FeatureId,
                UserVisibleReason = UserVisibleReason,
                AllowedHosts = AllowedHosts,
                SpkiPinsByHost = SpkiPinsByHost,
                Ttl = DefaultTtl,
                AllowSessionGrant = false,
            };
    }
}
