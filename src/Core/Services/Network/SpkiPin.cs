using System;
using System.Net.Security;
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
        public static bool MatchesValidatedChain(
            X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors, string pinBase64)
        {
            if (errors != SslPolicyErrors.None)
                return false;
            if (Matches(certificate, pinBase64))
                return true;
            if (chain is null || chain.ChainElements.Count < 2 ||
                chain.ChainStatus.Length != 0 ||
                !certificate.RawData.AsSpan().SequenceEqual(chain.ChainElements[0].Certificate.RawData))
                return false;

            // Trust anchors are not rotation backups: only intermediates in the validated chain.
            for (int i = 1; i < chain.ChainElements.Count - 1; i++)
            {
                if (Matches(chain.ChainElements[i].Certificate, pinBase64))
                    return true;
            }
            return false;
        }

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
