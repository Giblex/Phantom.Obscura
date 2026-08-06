using System;
using System.Collections.Generic;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.Core.Services.Licensing
{
    /// <summary>
    /// Default license verifier. Mirrors the update-verification failsafe model:
    /// production builds verify against the embedded <see cref="LicensePublicKey"/>;
    /// tests and the env-gated dev authority inject a synthetic public key via
    /// the internal constructor (InternalsVisibleTo).
    /// </summary>
    public sealed class LicenseVerifier : ILicenseVerifier
    {
        private const int Ed25519PublicKeySize = 32;

        private readonly byte[]? _injectedPublicKey;

        public LicenseVerifier()
        {
            _injectedPublicKey = null;
        }

        /// <summary>
        /// Constructs a verifier that trusts an explicit public key instead of
        /// the embedded production key. Used by tests and the dev license
        /// authority — never on a production code path.
        /// </summary>
        public LicenseVerifier(byte[] trustedPublicKey)
        {
            if (trustedPublicKey is null) throw new ArgumentNullException(nameof(trustedPublicKey));
            if (trustedPublicKey.Length != Ed25519PublicKeySize)
                throw new ArgumentException($"Public key must be {Ed25519PublicKeySize} bytes.", nameof(trustedPublicKey));
            _injectedPublicKey = (byte[])trustedPublicKey.Clone();
        }

        private bool IsProvisioned => _injectedPublicKey is not null || LicensePublicKey.IsProvisioned;

        private ReadOnlySpan<byte> ActivePublicKey => _injectedPublicKey is not null
            ? _injectedPublicKey.AsSpan()
            : LicensePublicKey.Bytes;

        public LicenseStatus Verify(string? token, string? currentUsbBindingId, TimeSpan offlineGrace)
        {
            if (string.IsNullOrWhiteSpace(token))
                return LicenseStatus.Free(LicenseFailureReason.Empty);

            if (!IsProvisioned)
                return LicenseStatus.Free(LicenseFailureReason.NotProvisioned);

            if (!LicenseTokenCodec.TryVerify(token, ActivePublicKey, out var claims) || claims is null)
                return LicenseStatus.Free(LicenseFailureReason.BadSignature);

            // Binding check: if the token is bound to a specific stick/device,
            // it must match the active vault's binding.
            if (!string.IsNullOrEmpty(claims.UsbBindingId) &&
                !string.IsNullOrEmpty(currentUsbBindingId) &&
                !string.Equals(claims.UsbBindingId, currentUsbBindingId, StringComparison.Ordinal))
            {
                return LicenseStatus.Free(LicenseFailureReason.BindingMismatch);
            }

            var now = DateTimeOffset.UtcNow;
            bool withinValidity = now <= claims.ExpiresUtc;
            bool withinGrace = !withinValidity && now <= claims.ExpiresUtc + offlineGrace;

            if (!withinValidity && !withinGrace)
                return LicenseStatus.Free(LicenseFailureReason.Expired);

            int daysRemaining = (int)Math.Ceiling((claims.ExpiresUtc - now).TotalDays);
            if (daysRemaining < 0) daysRemaining = 0;

            return new LicenseStatus
            {
                IsValid = true,
                Tier = claims.Tier,
                ExpiresUtc = claims.ExpiresUtc,
                DaysRemaining = daysRemaining,
                InGracePeriod = withinGrace,
                Reason = LicenseFailureReason.None,
                Features = ParseFeatures(claims.Features)
            };
        }

        private static IReadOnlyCollection<PremiumFeature> ParseFeatures(List<string>? names)
        {
            if (names is null || names.Count == 0)
                return Array.Empty<PremiumFeature>();

            var set = new HashSet<PremiumFeature>();
            foreach (var name in names)
            {
                if (Enum.TryParse<PremiumFeature>(name, ignoreCase: true, out var feature))
                    set.Add(feature);
            }
            return set;
        }
    }
}
