using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Service for scoped recovery operations.
    ///
    /// Recovery flow:
    /// 1. User enters recovery code → validates against stored hash
    /// 2. Recovery code derives K_recovery_session via HKDF
    /// 3. User explicitly selects domain to recover (Obscura OR Attestor)
    /// 4. K_recovery_session decrypts the sealed blob for chosen domain
    /// 5. Recovered domain key is used to rewrap/rekey domain store
    /// 6. Mandatory key rotation + invalidate used recovery codes
    ///
    /// CRITICAL: Recovery does NOT unlock the master key.
    /// It only unseals ONE domain at a time.
    /// </summary>
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

        /// <summary>
        /// Generates a new random recovery key for a domain.
        /// This key can restore access to the domain if the main password is lost.
        /// </summary>
        public byte[] GenerateDomainRecoveryKey()
        {
            var key = new byte[DomainRecoveryKeySize];
            RandomNumberGenerator.Fill(key);
            return key;
        }

        /// <summary>
        /// Seals a domain recovery key using the recovery domain key.
        /// The sealed blob can only be opened with K_recovery.
        /// </summary>
        /// <param name="domainRecoveryKey">The recovery key for the domain (e.g., K_obscura_recover)</param>
        /// <param name="recoveryDomainKey">K_recovery from DomainKeyProvider</param>
        /// <param name="domain">Target domain name</param>
        /// <returns>Sealed recovery key blob</returns>
        public SealedRecoveryKey SealDomainRecoveryKey(
            byte[] domainRecoveryKey,
            ReadOnlySpan<byte> recoveryDomainKey,
            CryptoDomain domain)
        {
            if (domainRecoveryKey == null || domainRecoveryKey.Length != DomainRecoveryKeySize)
                throw new ArgumentException($"Domain recovery key must be {DomainRecoveryKeySize} bytes", nameof(domainRecoveryKey));

            // Create metadata to include in the sealed blob
            var metadata = new SealMetadata
            {
                Domain = domain.ToString(),
                CreatedUtc = DateTimeOffset.UtcNow,
                SealVersion = 1
            };
            var metadataJson = JsonSerializer.Serialize(metadata);
            var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

            // Plaintext = recovery key || metadata length (4 bytes) || metadata
            var plaintext = new byte[domainRecoveryKey.Length + 4 + metadataBytes.Length];
            Buffer.BlockCopy(domainRecoveryKey, 0, plaintext, 0, domainRecoveryKey.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(metadataBytes.Length), 0, plaintext, domainRecoveryKey.Length, 4);
            Buffer.BlockCopy(metadataBytes, 0, plaintext, domainRecoveryKey.Length + 4, metadataBytes.Length);

            try
            {
                // AAD = domain name (prevents cross-domain blob substitution)
                var aad = Encoding.UTF8.GetBytes($"phantom.recovery.seal.{domain}");

                var encrypted = _encryptionService.Encrypt(plaintext, recoveryDomainKey.ToArray(), aad);

                // Format: nonce || ciphertext || tag
                var sealedBlob = new byte[encrypted.Nonce.Length + encrypted.Ciphertext.Length + encrypted.Tag.Length];
                Buffer.BlockCopy(encrypted.Nonce, 0, sealedBlob, 0, encrypted.Nonce.Length);
                Buffer.BlockCopy(encrypted.Ciphertext, 0, sealedBlob, encrypted.Nonce.Length, encrypted.Ciphertext.Length);
                Buffer.BlockCopy(encrypted.Tag, 0, sealedBlob, encrypted.Nonce.Length + encrypted.Ciphertext.Length, encrypted.Tag.Length);

                // Compute blob hash for verification
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

        /// <summary>
        /// Unseals a domain recovery key.
        /// Call this during recovery mode to restore access to a domain.
        /// </summary>
        /// <param name="sealedKey">The sealed recovery key blob</param>
        /// <param name="recoverySessionKey">K_recovery_session derived from recovery code</param>
        /// <param name="expectedDomain">The domain the user selected (must match)</param>
        /// <returns>The domain recovery key</returns>
        public byte[] UnsealDomainRecoveryKey(
            SealedRecoveryKey sealedKey,
            ReadOnlySpan<byte> recoverySessionKey,
            CryptoDomain expectedDomain)
        {
            if (sealedKey == null)
                throw new ArgumentNullException(nameof(sealedKey));

            // Verify domain matches user's explicit selection
            if (!string.Equals(sealedKey.Domain, expectedDomain.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Domain mismatch: sealed blob is for {sealedKey.Domain}, but user selected {expectedDomain}");

            var sealedBlob = Convert.FromBase64String(sealedKey.SealedBlob);

            // Verify blob integrity
            var computedHash = ComputeBlobHash(sealedBlob);
            if (computedHash != sealedKey.BlobHash)
                throw new CryptographicException("Sealed blob has been tampered with");

            if (sealedBlob.Length < NonceSize + TagSize + DomainRecoveryKeySize + 4)
                throw new CryptographicException("Sealed blob is too short");

            // Extract components
            var nonce = sealedBlob.AsSpan(0, NonceSize).ToArray();
            var tag = sealedBlob.AsSpan(sealedBlob.Length - TagSize, TagSize).ToArray();
            var ciphertext = sealedBlob.AsSpan(NonceSize, sealedBlob.Length - NonceSize - TagSize).ToArray();

            // AAD must match what was used during sealing
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
                // Extract domain recovery key (first 32 bytes)
                var domainRecoveryKey = new byte[DomainRecoveryKeySize];
                Buffer.BlockCopy(plaintext, 0, domainRecoveryKey, 0, DomainRecoveryKeySize);

                // Optionally validate metadata
                var metadataLength = BitConverter.ToInt32(plaintext, DomainRecoveryKeySize);
                if (metadataLength > 0 && metadataLength < plaintext.Length - DomainRecoveryKeySize - 4)
                {
                    var metadataJson = Encoding.UTF8.GetString(
                        plaintext, DomainRecoveryKeySize + 4, metadataLength);
                    var metadata = JsonSerializer.Deserialize<SealMetadata>(metadataJson);

                    // Verify domain in metadata matches
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

        /// <summary>
        /// Derives a recovery session key from a recovery code.
        /// This is used to decrypt sealed blobs during recovery mode.
        /// </summary>
        /// <param name="recoveryCode">The recovery code entered by user</param>
        /// <param name="salt">Salt from the recovery store</param>
        /// <returns>K_recovery_session</returns>
        public byte[] DeriveRecoverySessionKey(string recoveryCode, byte[] salt)
        {
            if (string.IsNullOrEmpty(recoveryCode))
                throw new ArgumentException("Recovery code is required", nameof(recoveryCode));
            if (salt == null || salt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

            // Normalize recovery code (remove dashes, uppercase)
            var normalizedCode = recoveryCode
                .Replace("-", "")
                .Replace(" ", "")
                .ToUpperInvariant();

            // Use HKDF to derive session key
            // This is faster than Argon2id because recovery codes have high entropy
            var codeBytes = Encoding.UTF8.GetBytes(normalizedCode);
            var info = Encoding.UTF8.GetBytes("phantom.recovery.session.v1");

            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                codeBytes,
                RecoveryKeySize,
                salt,
                info);
        }

        /// <summary>
        /// Computes SHA-256 hash of a sealed blob for integrity verification.
        /// </summary>
        private static string ComputeBlobHash(byte[] blob)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(blob);
            return $"sha256:{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Metadata embedded in sealed recovery blobs.
        /// </summary>
        private sealed class SealMetadata
        {
            public string Domain { get; set; } = string.Empty;
            public DateTimeOffset CreatedUtc { get; set; }
            public int SealVersion { get; set; }
        }
    }
}
