using System;
using System.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Security
{
    /// <summary>
    /// The vault's soft-lock PIN, stored inside the encrypted vault manifest.
    ///
    /// The PIN used to live in a sidecar file beside the manifest (or in settings.json),
    /// sealed with DPAPI only. DPAPI keeps out other Windows users, not other programs running
    /// as the same user: any of them could seal a record of its own and so choose the PIN that
    /// reopens a soft-locked vault. The manifest is AES-256-GCM under the vault's own key, so a
    /// record there can only be written by someone who can already unlock the vault, and
    /// verification reads the copy decrypted at unlock rather than anything on disk.
    /// </summary>
    public static class VaultPinLock
    {
        public const int MinLength = 6;
        public const int MaxLength = 8;

        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;

        public static bool IsValidFormat(string? pin) =>
            pin != null
            && pin.Length >= MinLength
            && pin.Length <= MaxLength
            && pin.All(char.IsDigit);

        public static bool IsConfigured(VaultManifest? manifest) =>
            manifest != null
            && !string.IsNullOrWhiteSpace(manifest.PinSaltBase64)
            && !string.IsNullOrWhiteSpace(manifest.PinHashBase64)
            && manifest.PinPbkdf2Iterations > 0;

        /// <summary>
        /// Writes a new PIN record into <paramref name="manifest"/>. This only changes the
        /// in-memory manifest; the caller persists it with the vault's credentials.
        /// </summary>
        public static void SetPin(VaultManifest manifest, string pin)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            if (!IsValidFormat(pin))
                throw new ArgumentException($"PIN must contain {MinLength}-{MaxLength} digits.", nameof(pin));

            // The work factor comes from the manifest model's default, so it lives in one place.
            int iterations = new VaultManifest().PinPbkdf2Iterations;
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);
            try
            {
                manifest.PinSaltBase64 = Convert.ToBase64String(salt);
                manifest.PinHashBase64 = Convert.ToBase64String(hash);
                manifest.PinPbkdf2Iterations = iterations;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
                CryptographicOperations.ZeroMemory(salt);
            }
        }

        /// <summary>Removes the PIN record from <paramref name="manifest"/> (in memory).</summary>
        public static void ClearPin(VaultManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            manifest.PinSaltBase64 = null;
            manifest.PinHashBase64 = null;
        }

        /// <summary>
        /// Checks <paramref name="pin"/> against the record in <paramref name="manifest"/>, in
        /// constant time. Pass the manifest decrypted at unlock, never one re-read from disk.
        /// </summary>
        public static bool Verify(VaultManifest? manifest, string? pin)
        {
            if (string.IsNullOrEmpty(pin) || !IsConfigured(manifest))
                return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(manifest!.PinSaltBase64!);
                expected = Convert.FromBase64String(manifest.PinHashBase64!);
            }
            catch (FormatException)
            {
                return false;
            }

            // FixedTimeEquals over two empty arrays is true, so an empty stored hash must not
            // get that far.
            if (salt.Length == 0 || expected.Length == 0)
                return false;

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, manifest.PinPbkdf2Iterations, HashAlgorithmName.SHA256, expected.Length);
            try
            {
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(salt);
            }
        }
    }
}
