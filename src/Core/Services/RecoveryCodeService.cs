using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed class RecoveryCodeService
    {
        private const int DefaultCodeCount = 10;
        private const int CodeEntropyBytes = 16;
        private const int FormattedCodeLength = 19;

        public string[] GenerateRecoveryCodes(int count = DefaultCodeCount)
        {
            if (count <= 0 || count > 100)
                throw new ArgumentException("Recovery code count must be between 1 and 100", nameof(count));

            var codes = new string[count];

            for (int i = 0; i < count; i++)
            {

                var bytes = RandomNumberGenerator.GetBytes(CodeEntropyBytes);

                var base64 = Convert.ToBase64String(bytes)
                    .Replace("+", "")
                    .Replace("/", "")
                    .Replace("=", "")
                    .ToUpperInvariant();

                var code = base64.Substring(0, 16);
                codes[i] = FormatRecoveryCode(code);
            }

            return codes;
        }

        public RecoveryCode[] PrepareRecoveryCodesForStorage(string[] codes)
        {
            var recoveryCodeMetadata = new RecoveryCode[codes.Length];

            for (int i = 0; i < codes.Length; i++)
            {

                var plainCode = codes[i].Replace("-", "");

                recoveryCodeMetadata[i] = new RecoveryCode
                {
                    Hash = HashRecoveryCode(plainCode),
                    Used = false,
                    UsedAtUtc = null,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
            }

            return recoveryCodeMetadata;
        }

        public bool ValidateAndUseRecoveryCode(VaultManifest manifest, string inputCode)
        {
            if (manifest.RecoveryCodes == null || manifest.RecoveryCodes.Length == 0)
                return false;

            var normalizedInput = inputCode.Replace("-", "").Replace(" ", "").ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(normalizedInput))
                return false;

            foreach (var recoveryCode in manifest.RecoveryCodes)
            {

                if (recoveryCode.Used)
                    continue;

                if (!TryParseStoredHash(recoveryCode.Hash, out var salt, out var storedHash))
                    continue;

                var inputHash = DeriveRecoveryCodeHash(normalizedInput, salt);
                try
                {
                    if (CryptographicOperations.FixedTimeEquals(inputHash, storedHash))
                    {

                        recoveryCode.Used = true;
                        recoveryCode.UsedAtUtc = DateTimeOffset.UtcNow;
                        return true;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(inputHash);
                    CryptographicOperations.ZeroMemory(salt);
                    CryptographicOperations.ZeroMemory(storedHash);
                }
            }

            return false;
        }

        public int GetRemainingCodeCount(VaultManifest manifest)
        {
            if (manifest.RecoveryCodes == null)
                return 0;

            return manifest.RecoveryCodes.Count(rc => !rc.Used);
        }

        public string[] RegenerateRecoveryCodes(VaultManifest manifest, int count = DefaultCodeCount)
        {

            var newCodes = GenerateRecoveryCodes(count);

            manifest.RecoveryCodes = PrepareRecoveryCodesForStorage(newCodes);

            return newCodes;
        }

        private static string FormatRecoveryCode(string code)
        {
            if (code.Length != 16)
                throw new ArgumentException("Recovery code must be 16 characters", nameof(code));

            return $"{code.Substring(0, 4)}-{code.Substring(4, 4)}-{code.Substring(8, 4)}-{code.Substring(12, 4)}";
        }

        private static string HashRecoveryCode(string code)
        {

            var salt = RandomNumberGenerator.GetBytes(16);

            var hash = DeriveRecoveryCodeHash(code, salt);

            var combined = new byte[salt.Length + hash.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);

            return Convert.ToBase64String(combined);
        }

        private static byte[] DeriveRecoveryCodeHash(string code, byte[] salt)
        {
            var normalized = Encoding.UTF8.GetBytes(code);
            try
            {
                var kdfParams = KdfDefaults.Recovery(salt);

                return Argon2Kdf.DeriveKey(normalized, salt, kdfParams);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(normalized);
            }
        }

        private static bool TryParseStoredHash(string storedHash, out byte[] salt, out byte[] hash)
        {
            salt = Array.Empty<byte>();
            hash = Array.Empty<byte>();

            try
            {
                var combined = Convert.FromBase64String(storedHash);
                if (combined.Length <= 16)
                    return false;

                salt = new byte[16];
                hash = new byte[combined.Length - 16];
                Buffer.BlockCopy(combined, 0, salt, 0, salt.Length);
                Buffer.BlockCopy(combined, salt.Length, hash, 0, hash.Length);
                CryptographicOperations.ZeroMemory(combined);
                return true;
            }
            catch
            {
                if (salt.Length > 0)
                    CryptographicOperations.ZeroMemory(salt);
                if (hash.Length > 0)
                    CryptographicOperations.ZeroMemory(hash);
                salt = Array.Empty<byte>();
                hash = Array.Empty<byte>();
                return false;
            }
        }
    }
}

