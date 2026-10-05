using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// Derives the ROM decryption key, and wraps the ROM's contribution for recovery.
    ///
    /// The ROM key comes from the vault's keyfile material — which on a provisioned device is the
    /// USB keyfile composed with the host companion key kept in the user profile. That is what
    /// makes a copied stick insufficient: the clone carries the sealed image but not the half of
    /// the key material that never left the machine.
    /// </summary>
    public static class BootRomKeyDerivation
    {
        private const string RomKeyInfo = "phantom-rom-key-v1";
        private const string EscrowKeyInfo = "phantom-rom-escrow-v1";
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;

        /// <summary>
        /// The 32-byte key the ROM image is sealed under. Caller owns the result and must zero it.
        /// </summary>
        public static byte[] DeriveRomKey(string keyfilePath, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> keyId)
        {
            if (string.IsNullOrWhiteSpace(keyfilePath))
                throw new SecurityException("Boot ROM binding requires keyfile material.");

            byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
            try
            {
                Span<byte> info = stackalloc byte[RomKeyInfo.Length + PhantomRomContainer.KeyIdSize];
                Encoding.ASCII.GetBytes(RomKeyInfo, info);
                keyId.CopyTo(info[RomKeyInfo.Length..]);

                return HKDF.DeriveKey(HashAlgorithmName.SHA256, keyfileBytes, KeySize, salt.ToArray(), info.ToArray());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyfileBytes);
            }
        }

        /// <summary>
        /// Seals the ROM's contribution under a recovery code, so losing or damaging the ROM is
        /// survivable. Without this, a bound vault whose ROM is gone is unopenable for good.
        /// </summary>
        public static byte[] WrapContributionForRecovery(
            ReadOnlySpan<byte> contribution,
            string recoveryCode,
            ReadOnlySpan<byte> escrowSalt)
        {
            if (contribution.Length != KeySize)
                throw new ArgumentException("Contribution must be 32 bytes.", nameof(contribution));

            byte[] escrowKey = DeriveEscrowKey(recoveryCode, escrowSalt);
            try
            {
                var blob = new byte[NonceSize + KeySize + TagSize];
                var span = blob.AsSpan();
                RandomNumberGenerator.Fill(span[..NonceSize]);

                using var aes = new AesGcm(escrowKey, TagSize);
                aes.Encrypt(
                    nonce: span[..NonceSize],
                    plaintext: contribution,
                    ciphertext: span.Slice(NonceSize, KeySize),
                    tag: span.Slice(NonceSize + KeySize, TagSize));

                return blob;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(escrowKey);
            }
        }

        /// <summary>
        /// Recovers the contribution from the escrow blob. Throws <see cref="SecurityException"/>
        /// when the recovery code is wrong or the blob has been altered.
        /// </summary>
        public static byte[] UnwrapContributionForRecovery(
            ReadOnlySpan<byte> escrowBlob,
            string recoveryCode,
            ReadOnlySpan<byte> escrowSalt)
        {
            if (escrowBlob.Length != NonceSize + KeySize + TagSize)
                throw new SecurityException("Boot ROM recovery blob is malformed.");

            byte[] escrowKey = DeriveEscrowKey(recoveryCode, escrowSalt);
            var contribution = new byte[KeySize];
            try
            {
                using var aes = new AesGcm(escrowKey, TagSize);
                aes.Decrypt(
                    nonce: escrowBlob[..NonceSize],
                    ciphertext: escrowBlob.Slice(NonceSize, KeySize),
                    tag: escrowBlob.Slice(NonceSize + KeySize, TagSize),
                    plaintext: contribution);
                return contribution;
            }
            catch (CryptographicException ex)
            {
                CryptographicOperations.ZeroMemory(contribution);
                throw new SecurityException("The recovery code did not unlock the Boot ROM escrow.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(escrowKey);
            }
        }

        /// <summary>
        /// A printable recovery code: 32 bytes of entropy in groups of five Base32 characters.
        /// Shown once when binding is enabled, and never stored by the app.
        /// </summary>
        public static string GenerateRecoveryCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I, O, 0, 1
            Span<byte> entropy = stackalloc byte[32];
            RandomNumberGenerator.Fill(entropy);

            var builder = new StringBuilder(64);
            for (int i = 0; i < entropy.Length; i++)
            {
                if (i > 0 && i % 4 == 0) builder.Append('-');
                builder.Append(alphabet[entropy[i] & 0x1F]);
                builder.Append(alphabet[(entropy[i] >> 3) & 0x1F]);
            }
            return builder.ToString();
        }

        private static byte[] DeriveEscrowKey(string recoveryCode, ReadOnlySpan<byte> escrowSalt)
        {
            if (string.IsNullOrWhiteSpace(recoveryCode))
                throw new SecurityException("A recovery code is required.");

            // Normalised so formatting (case, dashes, spaces) never changes the key.
            string normalised = recoveryCode.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            byte[] codeBytes = Encoding.UTF8.GetBytes(normalised);
            try
            {
                return HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    codeBytes,
                    KeySize,
                    escrowSalt.ToArray(),
                    Encoding.ASCII.GetBytes(EscrowKeyInfo));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(codeBytes);
            }
        }
    }
}
