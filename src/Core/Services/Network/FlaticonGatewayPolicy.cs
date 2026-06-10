using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// Constants describing the InternetAccessRequest envelope for the Flaticon
    /// icon-download feature. Centralised so that the host allowlist and SPKI
    /// pin set are reviewed in exactly one place.
    ///
    /// <para>
    /// <b>SECURITY — PIN ROTATION</b>: The pins below are real base64
    /// SHA-256(SubjectPublicKeyInfo) values extracted from a live TLS handshake.
    /// Two pins are listed per host: the current leaf certificate (primary) and
    /// the issuing intermediate (backup) so that leaf rotation does not brick
    /// the gateway. Re-extract when the intermediate changes.
    /// </para>
    /// <para>
    /// To re-extract pins from a live host:
    /// <code>
    /// openssl s_client -connect api.flaticon.com:443 -servername api.flaticon.com 2&gt;/dev/null \
    ///   | openssl x509 -pubkey -noout \
    ///   | openssl pkey -pubin -outform der \
    ///   | openssl dgst -sha256 -binary \
    ///   | openssl enc -base64
    /// </code>
    /// </para>
    /// </summary>
    public static class FlaticonGatewayPolicy
    {
        public const string ApiHost = "api.flaticon.com";
        public const string CdnHost = "cdn-icons-png.flaticon.com";

        public const string FeatureId = "icon-downloader.flaticon";

        public const string UserVisibleReason =
            "Search and download icons from Flaticon for use in your vault.";

        // Leaf SPKI for *.flaticon.com (covers api.flaticon.com and cdn-icons-png.flaticon.com).
        // Issued by Let's Encrypt YE1; expires 2026-08-27.
        private const string LeafPinWildcardFlaticon =
            "2IdmhuiBZSOy43sob9m/bJdgUQ8AC2uKFkg8QGvS860=";

        // Backup pin: Let's Encrypt YE1 intermediate SPKI.
        // Survives leaf rotation as long as Flaticon stays on Let's Encrypt YE1.
        private const string IntermediatePinLetsEncryptYE1 =
            "brzvtCELCIZUo4sD/qPX0ccRtPsd3DY6RfmxpOU9oB4=";

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [ApiHost] = new[] { LeafPinWildcardFlaticon, IntermediatePinLetsEncryptYE1 },
                [CdnHost] = new[] { LeafPinWildcardFlaticon, IntermediatePinLetsEncryptYE1 },
            };

        public static IReadOnlyList<string> AllowedHosts { get; } = new[]
        {
            ApiHost,
            CdnHost,
        };

        public static TimeSpan DefaultTtl { get; } = TimeSpan.FromMinutes(10);

        public static InternetAccessRequest CreateRequest()
            => new()
            {
                FeatureId = FeatureId,
                UserVisibleReason = UserVisibleReason,
                AllowedHosts = AllowedHosts,
                SpkiPinsByHost = SpkiPinsByHost,
                Ttl = DefaultTtl,
                AllowSessionGrant = true,
            };
    }
}
