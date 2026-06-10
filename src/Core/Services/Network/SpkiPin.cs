using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// SubjectPublicKeyInfo SHA-256 pin helper.
    /// Compares the SHA-256 of the certificate's SPKI (DER-encoded public key)
    /// against a base64-encoded pin string.
    /// </summary>
    public static class SpkiPin
    {
        /// <summary>
        /// Compute the base64 SHA-256 SPKI pin for a certificate. Used for tests,
        /// developer tooling, and one-time pin extraction during pin rotation.
        /// </summary>
        public static string ComputePinBase64(X509Certificate2 cert)
        {
            if (cert is null) throw new ArgumentNullException(nameof(cert));
            var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
            var hash = SHA256.HashData(spki);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Constant-time compare of a certificate's SPKI hash to a pin value.
        /// </summary>
        public static bool Matches(X509Certificate2 cert, string pinBase64)
        {
            if (cert is null || string.IsNullOrWhiteSpace(pinBase64)) return false;

            byte[] expected;
            try { expected = Convert.FromBase64String(pinBase64); }
            catch (FormatException) { return false; }

            if (expected.Length != 32) return false;

            var actual = SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo());
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
