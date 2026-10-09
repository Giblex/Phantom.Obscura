using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services.Licensing
{
    /// <summary>
    /// Embedded Ed25519 license-signing public key. The matching private key is
    /// held offline by the licensing authority and is the sole production
    /// authority that can author a license token the verifier will accept.
    ///
    /// <para>Failsafe-by-default, identical to the update-signing model:</para>
    /// <list type="number">
    /// <item><description>If <see cref="Bytes"/> is ever the 32-byte all-zero
    /// sentinel, <see cref="IsProvisioned"/> returns <c>false</c> and the
    /// verifier refuses to verify — every vault stays Free. An unprovisioned
    /// build cannot be tricked into unlocking premium.</description></item>
    /// <item><description>The embedded <see cref="ProductionPublicKey"/> below is
    /// the live key paired with the current licensing backend. For a hardened
    /// release, regenerate the keypair offline and keep the private key on an
    /// air-gapped signing box.</description></item>
    /// </list>
    /// </summary>
    public static class LicensePublicKey
    {
        // Live Ed25519 license-signing public key. The matching private seed is held
        // only by the licensing backend (Cloudflare Worker secret LICENSE_SIGNING_KEY)
        // and never ships in the client. For a hardened production release, regenerate
        // this pair on an air-gapped box; this value pairs the bundled Stripe backend.
        private static readonly byte[] ProductionPublicKey = new byte[]
        {
            199, 211, 78, 228, 92, 178, 14, 219, 239, 124, 101, 100, 77, 132, 96, 141,
            167, 24, 194, 62, 200, 248, 151, 71, 146, 105, 101, 56, 141, 220, 31, 209
        };

        /// <summary>
        /// Production public key. Verifies tokens minted by the licensing backend.
        /// </summary>
        public static ReadOnlySpan<byte> Bytes => ProductionPublicKey;

        /// <summary>
        /// False if the embedded key is ever the all-zero sentinel.
        /// Constant-time check.
        /// </summary>
        public static bool IsProvisioned
        {
            get
            {
                ReadOnlySpan<byte> bytes = Bytes;
                if (bytes.Length != 32) return false;
                Span<byte> zero = stackalloc byte[32];
                return !CryptographicOperations.FixedTimeEquals(bytes, zero);
            }
        }
    }
}
