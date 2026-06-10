using System;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Embedded Ed25519 update-signing public key. The matching private key is
    /// held offline and is the sole authority that can author a manifest the
    /// verifier will accept.
    ///
    /// <para>
    /// Two-stage rollout:
    /// </para>
    /// <list type="number">
    /// <item><description>While <see cref="Bytes"/> is the 32-byte placeholder
    /// (all zeros), <see cref="IsProvisioned"/> returns <c>false</c> and the
    /// verifier refuses to even attempt signature verification — every check
    /// returns <see cref="UpdateVerificationFailure.PublicKeyNotProvisioned"/>.
    /// This is failsafe-by-default: an unprovisioned build cannot be tricked
    /// into accepting an attacker-signed manifest.</description></item>
    /// <item><description>To go live, generate an Ed25519 keypair offline,
    /// keep the private key on an air-gapped signing box, and replace
    /// <see cref="Placeholder"/> below with the 32-byte public key. The
    /// CI hard-rules workflow rejects the placeholder appearing outside this
    /// file.</description></item>
    /// </list>
    /// </summary>
    public static class UpdatePublicKey
    {
        /// <summary>
        /// 32-byte placeholder (Ed25519 public keys are exactly 32 bytes).
        /// SECURITY: an all-zero public key would in theory still be a valid
        /// Ed25519 point, so we additionally short-circuit on the "all bytes
        /// zero" sentinel via <see cref="IsProvisioned"/>.
        /// </summary>
        private static readonly byte[] Placeholder = new byte[32];

        /// <summary>
        /// Production public key. Replace <see cref="Placeholder"/> with the
        /// 32 bytes of the live Ed25519 public key on release. Until then,
        /// <see cref="IsProvisioned"/> stays false and updates are disabled.
        /// </summary>
        public static ReadOnlySpan<byte> Bytes => Placeholder;

        /// <summary>
        /// Returns false while the embedded key is still the all-zero
        /// placeholder. Constant-time check.
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
