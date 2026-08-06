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
    /// <item><description>While <see cref="Bytes"/> is the 32-byte placeholder
    /// (all zeros), <see cref="IsProvisioned"/> returns <c>false</c> and the
    /// verifier refuses to verify — every vault stays Free. An unprovisioned
    /// build cannot be tricked into unlocking premium.</description></item>
    /// <item><description>To go live, generate an Ed25519 keypair offline, keep
    /// the private key on an air-gapped signing box, and replace
    /// <see cref="Placeholder"/> with the 32-byte public key.</description></item>
    /// </list>
    /// </summary>
    public static class LicensePublicKey
    {
        // Live Ed25519 license-signing public key. The matching private seed is held
        // only by the licensing backend (Cloudflare Worker secret LICENSE_SIGNING_KEY)
        // and never ships in the client. For a hardened production release, regenerate
        // this pair on an air-gapped box; this value pairs the bundled Stripe backend.
        private static readonly byte[] Placeholder = new byte[]
        {
            180, 54, 31, 42, 28, 49, 99, 185, 233, 95, 170, 157, 45, 129, 253, 147,
            196, 213, 4, 75, 178, 34, 234, 116, 20, 122, 62, 150, 133, 103, 254, 229
        };

        /// <summary>
        /// Production public key. Verifies tokens minted by the licensing backend.
        /// </summary>
        public static ReadOnlySpan<byte> Bytes => Placeholder;

        /// <summary>
        /// False while the embedded key is still the all-zero placeholder.
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
