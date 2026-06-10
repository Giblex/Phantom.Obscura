using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// InternetAccessRequest envelope for the HaveIBeenPwned password-breach lookup.
    /// k-anonymity range API: the client sends the first 5 hex chars of SHA-1(password)
    /// and receives all suffixes with that prefix, so the cleartext password never leaves
    /// the device. This service still requires explicit user consent per session.
    ///
    /// <para>
    /// <b>SECURITY — PIN ROTATION</b>: pins are real base64 SHA-256(SubjectPublicKeyInfo)
    /// values from a live TLS handshake. Two pins per host: leaf (primary) plus the
    /// Google Trust Services WE1 intermediate (backup) for leaf-rotation tolerance.
    /// Re-extract via the openssl pipeline documented in <see cref="FlaticonGatewayPolicy"/>
    /// when the intermediate changes.
    /// </para>
    /// </summary>
    public static class HibpGatewayPolicy
    {
        public const string RangeHost = "api.pwnedpasswords.com";
        public const string BreachHost = "haveibeenpwned.com";

        public const string FeatureId = "password-health.hibp";

        public const string UserVisibleReason =
            "Check whether your passwords appear in any known data breach. " +
            "Only the first 5 characters of a SHA-1 hash are sent — your passwords never leave the device.";

        // Leaf SPKI for api.pwnedpasswords.com; expires 2026-08-09.
        private const string LeafPinPwnedPasswords =
            "9IwmXwvi5X2PS4f4WyChoe7zqc+804o3cHd42i9C/QA=";

        // Leaf SPKI for haveibeenpwned.com; expires 2026-08-07.
        private const string LeafPinHaveIBeenPwned =
            "VgvnWRjPVQSn3Nu/iTPWsgPdGDJqsy+3XCnmPIJEBpE=";

        // Backup pin: Google Trust Services WE1 intermediate SPKI.
        // Both HIBP hosts chain through GTS WE1, so this single backup covers leaf rotation
        // for as long as HIBP stays on Google Trust Services.
        private const string IntermediatePinGtsWE1 =
            "kIdp6NNEd8wsugYyyIYFsi1ylMCED3hZbSR8ZFsa/A4=";

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> SpkiPinsByHost { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [RangeHost] = new[] { LeafPinPwnedPasswords, IntermediatePinGtsWE1 },
                [BreachHost] = new[] { LeafPinHaveIBeenPwned, IntermediatePinGtsWE1 },
            };

        public static IReadOnlyList<string> AllowedHosts { get; } = new[]
        {
            RangeHost,
            BreachHost,
        };

        public static TimeSpan DefaultTtl { get; } = TimeSpan.FromMinutes(5);

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
