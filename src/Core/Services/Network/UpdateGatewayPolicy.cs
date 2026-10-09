using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// InternetAccessRequest envelope for the Phantom Obscura update channel.
    ///
    /// <para>
    /// Flow: the client fetches a signed manifest from
    /// <c>https://updates.giblex.com/manifests/phantom-obscura/{channel}.json</c>
    /// (and its <c>.sig</c> sibling), verifies it with the embedded Ed25519
    /// public key, then downloads the asset from the URL inside the manifest.
    /// Every byte of that flow funnels through <see cref="IInternetGateway"/>
    /// with this policy active.
    /// </para>
    ///
    /// <para>
    /// <b>SECURITY — SPKI PINNING.</b> Pinning is optional across the gateway model:
    /// a host with pins has them enforced, a host without is not pinned. This channel
    /// ships with no pin, so it proceeds on standard public-CA TLS validation; its
    /// integrity gate is the Ed25519 manifest signature, verified against
    /// <see cref="Update.UpdatePublicKey"/> after download (and fail-closed while that
    /// key is unprovisioned). To add pinning later as defence-in-depth, extract the real
    /// leaf + intermediate SPKI hashes via the same .NET SslStream recipe used for
    /// <see cref="FlaticonGatewayPolicy"/> / <see cref="HibpGatewayPolicy"/> and add them
    /// to <see cref="SpkiPinsByHost"/>.
    /// </para>
    /// </summary>
    public static class UpdateGatewayPolicy
    {
        public const string UpdateHost = "updates.giblex.com";

        public const string FeatureId = "app-update";

        public const string UserVisibleReason =
            "Check for a new Phantom Obscura release and download the signed update package. " +
            "The update is verified with an embedded Ed25519 key before it can be installed.";

        /// <summary>
        /// No SPKI pins configured for the update host — pinning is optional (see the
        /// class remarks). Add real leaf/intermediate SPKI hashes here to enable pinning
        /// as defence-in-depth; while empty, the host is not pinned.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> AllowedHosts { get; } = new[] { UpdateHost };

        // Long enough for a check + manifest fetch + asset download (asset capped
        // at 1 GiB by the verifier; download stream is gated on Content-Length too).
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

        public static Uri ManifestUrl(Update.UpdateChannel channel)
        {
            string c = channel switch
            {
                Update.UpdateChannel.Stable => "stable",
                Update.UpdateChannel.Beta => "beta",
                _ => throw new ArgumentOutOfRangeException(nameof(channel)),
            };
            return new Uri($"https://{UpdateHost}/manifests/phantom-obscura/{c}.json");
        }

        public static Uri SignatureUrl(Update.UpdateChannel channel)
            => new(ManifestUrl(channel).ToString() + ".sig");
    }
}
