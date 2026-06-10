using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{

    public sealed class ScopedRecoveryService
    {
        private const int RecoveryKeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int DomainRecoveryKeySize = 32;

        private readonly EncryptionService _encryptionService;

        public ScopedRecoveryService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        public byte[] GenerateDomainRecoveryKey()
        {
            var key = new byte[DomainRecoveryKeySize];
            RandomNumberGenerator.Fill(key);
            return key;
        }

        public SealedRecoveryKey SealDomainRecoveryKey(
            byte[] domainRecoveryKey,
            ReadOnlySpan<byte> recoveryDomainKey,
            CryptoDomain domain)
        {
            if (domainRecoveryKey == null || domainRecoveryKey.Length != DomainRecoveryKeySize)
                throw new ArgumentException($"Domain recovery key must be {DomainRecoveryKeySize} bytes", nameof(domainRecoveryKey));

            var metadata = new SealMetadata
            {
                Domain = domain.ToString(),
                CreatedUtc = DateTimeOffset.UtcNow,
                SealVersion = 1
            };
            var metadataJson = JsonSerializer.Serialize(metadata);
            var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

            var plaintext = new byte[domainRecoveryKey.Length + 4 + metadataBytes.Length];
            Buffer.BlockCopy(domainRecoveryKey, 0, plaintext, 0, domainRecoveryKey.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(metadataBytes.Length), 0, plaintext, domainRecoveryKey.Length, 4);
            Buffer.BlockCopy(metadataBytes, 0, plaintext, domainRecoveryKey.Length + 4, metadataBytes.Length);

            try
            {

                var aad = Encoding.UTF8.GetBytes($"phantom.recovery.seal.{domain}");

                var encrypted = _encryptionService.Encrypt(plaintext, recoveryDomainKey.ToArray(), aad);

                var sealedBlob = new byte[encrypted.Nonce.Length + encrypted.Ciphertext.Length + encrypted.Tag.Length];
                Buffer.BlockCopy(encrypted.Nonce, 0, sealedBlob, 0, encrypted.Nonce.Length);
                Buffer.BlockCopy(encrypted.Ciphertext, 0, sealedBlob, encrypted.Nonce.Length, encrypted.Ciphertext.Length);
                Buffer.BlockCopy(encrypted.Tag, 0, sealedBlob, encrypted.Nonce.Length + encrypted.Ciphertext.Length, encrypted.Tag.Length);

                var blobHash = ComputeBlobHash(sealedBlob);

                return new SealedRecoveryKey
                {
                    Domain = domain.ToString(),
                    SealedBlob = Convert.ToBase64String(sealedBlob),
                    CreatedUtc = metadata.CreatedUtc,
                    SealVersion = metadata.SealVersion,
                    BlobHash = blobHash,
                    HasBeenUsed = false
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public byte[] UnsealDomainRecoveryKey(
            SealedRecoveryKey sealedKey,
            ReadOnlySpan<byte> recoverySessionKey,
            CryptoDomain expectedDomain)
        {
            if (sealedKey == null)
                throw new ArgumentNullException(nameof(sealedKey));

            if (!string.Equals(sealedKey.Domain, expectedDomain.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Domain mismatch: sealed blob is for {sealedKey.Domain}, but user selected {expectedDomain}");

            var sealedBlob = Convert.FromBase64String(sealedKey.SealedBlob);

            var computedHash = ComputeBlobHash(sealedBlob);
            if (computedHash != sealedKey.BlobHash)
                throw new CryptographicException("Sealed blob has been tampered with");

            if (sealedBlob.Length < NonceSize + TagSize + DomainRecoveryKeySize + 4)
                throw new CryptographicException("Sealed blob is too short");

            var nonce = sealedBlob.AsSpan(0, NonceSize).ToArray();
            var tag = sealedBlob.AsSpan(sealedBlob.Length - TagSize, TagSize).ToArray();
            var ciphertext = sealedBlob.AsSpan(NonceSize, sealedBlob.Length - NonceSize - TagSize).ToArray();

            var aad = Encoding.UTF8.GetBytes($"phantom.recovery.seal.{expectedDomain}");

            byte[] plaintext;
            try
            {
                plaintext = _encryptionService.Decrypt(
                    ciphertext,
                    nonce,
                    tag,
                    recoverySessionKey.ToArray(),
                    aad);
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Invalid recovery key or corrupted sealed blob");
            }

            try
            {

                var domainRecoveryKey = new byte[DomainRecoveryKeySize];
                Buffer.BlockCopy(plaintext, 0, domainRecoveryKey, 0, DomainRecoveryKeySize);

                var metadataLength = BitConverter.ToInt32(plaintext, DomainRecoveryKeySize);
                if (metadataLength > 0 && metadataLength < plaintext.Length - DomainRecoveryKeySize - 4)
                {
                    var metadataJson = Encoding.UTF8.GetString(
                        plaintext, DomainRecoveryKeySize + 4, metadataLength);
                    var metadata = JsonSerializer.Deserialize<SealMetadata>(metadataJson);

                    if (metadata != null && !string.Equals(metadata.Domain, expectedDomain.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        CryptographicOperations.ZeroMemory(domainRecoveryKey);
                        throw new CryptographicException("Metadata domain mismatch");
                    }
                }

                return domainRecoveryKey;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public byte[] DeriveRecoverySessionKey(string recoveryCode, byte[] salt)
        {
            if (string.IsNullOrEmpty(recoveryCode))
                throw new ArgumentException("Recovery code is required", nameof(recoveryCode));
            if (salt == null || salt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

            var normalizedCode = recoveryCode
                .Replace("-", "")
                .Replace(" ", "")
                .ToUpperInvariant();

            var codeBytes = Encoding.UTF8.GetBytes(normalizedCode);
            var info = Encoding.UTF8.GetBytes("phantom.recovery.session.v1");

            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                codeBytes,
                RecoveryKeySize,
                salt,
                info);
        }

        private static string ComputeBlobHash(byte[] blob)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(blob);
            return $"sha256:{Convert.ToBase64String(hash)}";
        }

        private sealed class SealMetadata
        {
            public string Domain { get; set; } = string.Empty;
            public DateTimeOffset CreatedUtc { get; set; }
            public int SealVersion { get; set; }
        }
    }
}

