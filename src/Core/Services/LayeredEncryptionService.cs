using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides layered encryption with configurable depth for defense-in-depth security.
    /// Supports 2, 3, and 5-layer encryption schemes using multiple algorithms:
    /// - Layer 1: AES-256-GCM (authenticated encryption with integrity)
    /// - Layer 2: ChaCha20-Poly1305 (stream cipher with authentication)
    /// - Layer 3: AES-256-CBC (traditional block cipher)
    /// - Layer 4: Twofish-256 (additional algorithm diversity)
    /// - Layer 5: Serpent-256 (final layer for maximum security)
    /// </summary>
    public sealed class LayeredEncryptionService
    {
        private readonly EncryptionService _encryptionService;

        public LayeredEncryptionService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        /// <summary>Encryption security levels matching different data sensitivity tiers.</summary>
        public enum SecurityLevel
        {
            Standard = 2,   // AES-GCM + ChaCha20
            Sensitive = 3,  // AES-GCM + ChaCha20 + AES-CBC
            Maximum = 5     // Full cascade
        }

        public class LayeredEncryptionResult
        {
            public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
            public byte[] Nonce1 { get; set; } = Array.Empty<byte>();
            public byte[] Tag1 { get; set; } = Array.Empty<byte>();
            public byte[] Nonce2 { get; set; } = Array.Empty<byte>();
            public byte[] Tag2 { get; set; } = Array.Empty<byte>();
            public byte[] IV3 { get; set; } = Array.Empty<byte>();
            public byte[] IV4 { get; set; } = Array.Empty<byte>();
            public byte[] IV5 { get; set; } = Array.Empty<byte>();
            public SecurityLevel Level { get; set; }
        }

        #region Encryption

        public async Task<LayeredEncryptionResult> EncryptLayeredAsync(
            ReadOnlyMemory<byte> plaintext,
            ReadOnlyMemory<byte> masterKey,
            SecurityLevel level,
            ReadOnlyMemory<byte> salt,
            ReadOnlyMemory<byte> contextData = default,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => EncryptLayered(
                plaintext.Span,
                masterKey.Span,
                level,
                salt.Span,
                contextData.Span), cancellationToken);
        }

        public LayeredEncryptionResult EncryptLayered(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> masterKey,
            SecurityLevel level,
            ReadOnlySpan<byte> salt,
            ReadOnlySpan<byte> contextData = default)
        {
            if (masterKey.Length != 32)
                throw new ArgumentException("Master key must be 32 bytes", nameof(masterKey));
            if (salt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

            var result = new LayeredEncryptionResult { Level = level };
            byte[] currentData = plaintext.ToArray();

            try
            {
                // Layer 1: AES-256-GCM
                var layer1Result = _encryptionService.Encrypt(currentData, masterKey, contextData);
                result.Nonce1 = layer1Result.Nonce;
                result.Tag1 = layer1Result.Tag;
                currentData = layer1Result.Ciphertext;

                if (level == SecurityLevel.Standard)
                {
                    var layer2Key = DeriveLayerKey(masterKey, salt, "Layer2-ChaCha20");
                    var layer2Result = EncryptChaCha20(currentData, layer2Key, contextData);
                    result.Nonce2 = layer2Result.Nonce;
                    result.Tag2 = layer2Result.Tag;
                    result.Ciphertext = layer2Result.Ciphertext;
                    CryptographicOperations.ZeroMemory(layer2Key);
                    return result;
                }

                // Layer 2: ChaCha20-Poly1305
                var key2 = DeriveLayerKey(masterKey, salt, "Layer2-ChaCha20");
                var enc2 = EncryptChaCha20(currentData, key2, contextData);
                result.Nonce2 = enc2.Nonce;
                result.Tag2 = enc2.Tag;
                currentData = enc2.Ciphertext;
                CryptographicOperations.ZeroMemory(key2);

                if (level == SecurityLevel.Sensitive)
                {
                    var layer3Key = DeriveLayerKey(masterKey, salt, "Layer3-AES-CBC");
                    var layer3Result = EncryptAesCbc(currentData, layer3Key, out byte[] iv3);
                    result.IV3 = iv3;
                    result.Ciphertext = layer3Result;
                    CryptographicOperations.ZeroMemory(layer3Key);
                    return result;
                }

                // Layer 3: AES-256-CBC
                var key3 = DeriveLayerKey(masterKey, salt, "Layer3-AES-CBC");
                var enc3 = EncryptAesCbc(currentData, key3, out byte[] iv3Full);
                result.IV3 = iv3Full;
                currentData = enc3;
                CryptographicOperations.ZeroMemory(key3);

                // Layer 4: Twofish-256
                var key4 = DeriveLayerKey(masterKey, salt, "Layer4-Twofish", contextData);
                var enc4 = EncryptTwofish(currentData, key4, out byte[] iv4);
                result.IV4 = iv4;
                currentData = enc4;
                CryptographicOperations.ZeroMemory(key4);

                // Layer 5: Serpent-256
                var key5 = DeriveLayerKey(masterKey, salt, "Layer5-Serpent", contextData);
                var enc5 = EncryptSerpent(currentData, key5, out byte[] iv5);
                result.IV5 = iv5;
                result.Ciphertext = enc5;
                CryptographicOperations.ZeroMemory(key5);

                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentData);
            }
        }

        #endregion

        #region Decryption

        public byte[] DecryptLayered(
            LayeredEncryptionResult encryptedData,
            ReadOnlySpan<byte> masterKey,
            ReadOnlySpan<byte> salt,
            ReadOnlySpan<byte> contextData = default)
        {
            if (masterKey.Length != 32)
                throw new ArgumentException("Master key must be 32 bytes", nameof(masterKey));

            byte[] currentData = encryptedData.Ciphertext;
            bool ownsCurrentData = false;
            byte[]? finalPlaintext = null;

            try
            {
                if (encryptedData.Level == SecurityLevel.Maximum)
                {
                    var key5 = DeriveLayerKey(masterKey, salt, "Layer5-Serpent", contextData);
                    currentData = ReplaceBuffer(currentData, DecryptSerpent(currentData, key5, encryptedData.IV5), ref ownsCurrentData);
                    CryptographicOperations.ZeroMemory(key5);

                    var key4 = DeriveLayerKey(masterKey, salt, "Layer4-Twofish", contextData);
                    currentData = ReplaceBuffer(currentData, DecryptTwofish(currentData, key4, encryptedData.IV4), ref ownsCurrentData);
                    CryptographicOperations.ZeroMemory(key4);
                }

                if (encryptedData.Level == SecurityLevel.Maximum || encryptedData.Level == SecurityLevel.Sensitive)
                {
                    var key3 = DeriveLayerKey(masterKey, salt, "Layer3-AES-CBC");
                    currentData = ReplaceBuffer(currentData, DecryptAesCbc(currentData, key3, encryptedData.IV3), ref ownsCurrentData);
                    CryptographicOperations.ZeroMemory(key3);
                }

                var key2 = DeriveLayerKey(masterKey, salt, "Layer2-ChaCha20");
                currentData = ReplaceBuffer(currentData, DecryptChaCha20(currentData, key2, encryptedData.Nonce2, encryptedData.Tag2, contextData), ref ownsCurrentData);
                CryptographicOperations.ZeroMemory(key2);

                currentData = ReplaceBuffer(currentData, _encryptionService.Decrypt(currentData, encryptedData.Nonce1, encryptedData.Tag1, masterKey, contextData), ref ownsCurrentData);

                finalPlaintext = currentData;
                ownsCurrentData = false;
                return finalPlaintext;
            }
            finally
            {
                if (ownsCurrentData)
                {
                    CryptographicOperations.ZeroMemory(currentData);
                }
            }
        }

        private static byte[] ReplaceBuffer(byte[] current, byte[] next, ref bool ownsCurrent)
        {
            if (ownsCurrent)
            {
                CryptographicOperations.ZeroMemory(current);
            }
            ownsCurrent = true;
            return next;
        }

        #endregion

        #region Key Derivation & Helpers

        private byte[] DeriveLayerKey(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> salt, string label, ReadOnlySpan<byte> context = default)
        {
            // HKDF with SHA-512 and label + optional context info
            var info = CombineBytes(Encoding.UTF8.GetBytes(label), context.ToArray());
            var derived = new byte[32];
            using var hkdf = new HKDF(HashAlgorithmName.SHA512, masterKey.ToArray(), salt.ToArray());
            hkdf.DeriveKey(info, derived);
            return derived;
        }

        private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptChaCha20(byte[] plaintext, byte[] key, ReadOnlySpan<byte> aad)
        {
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            using var chacha = new System.Security.Cryptography.ChaCha20Poly1305(key);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            chacha.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            return (ciphertext, nonce, tag);
        }

        private byte[] DecryptChaCha20(byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, ReadOnlySpan<byte> aad)
        {
            using var chacha = new System.Security.Cryptography.ChaCha20Poly1305(key);
            byte[] plaintext = new byte[ciphertext.Length];
            chacha.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }

        private byte[] EncryptAesCbc(byte[] plaintext, byte[] key, out byte[] iv)
        {
            iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        }

        private byte[] DecryptAesCbc(byte[] ciphertext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        private byte[] EncryptTwofish(byte[] plaintext, byte[] key, out byte[] iv)
        {
            iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var engine = new TwofishEngine();
            var blockCipher = new CbcBlockCipher(engine);
            var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
            var keyParamWithIv = new ParametersWithIV(new KeyParameter(key), iv);

            cipher.Init(true, keyParamWithIv);
            var output = new byte[cipher.GetOutputSize(plaintext.Length)];
            var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return output[..length];
        }

        private byte[] DecryptTwofish(byte[] ciphertext, byte[] key, byte[] iv)
        {
            var engine = new TwofishEngine();
            var blockCipher = new CbcBlockCipher(engine);
            var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
            var keyParamWithIv = new ParametersWithIV(new KeyParameter(key), iv);

            cipher.Init(false, keyParamWithIv);
            var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
            var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return output[..length];
        }

        private byte[] EncryptSerpent(byte[] plaintext, byte[] key, out byte[] iv)
        {
            iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var engine = new SerpentEngine();
            var blockCipher = new CbcBlockCipher(engine);
            var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
            var keyParamWithIv = new ParametersWithIV(new KeyParameter(key), iv);

            cipher.Init(true, keyParamWithIv);
            var output = new byte[cipher.GetOutputSize(plaintext.Length)];
            var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return output[..length];
        }

        private byte[] DecryptSerpent(byte[] ciphertext, byte[] key, byte[] iv)
        {
            var engine = new SerpentEngine();
            var blockCipher = new CbcBlockCipher(engine);
            var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
            var keyParamWithIv = new ParametersWithIV(new KeyParameter(key), iv);

            cipher.Init(false, keyParamWithIv);
            var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
            var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return output[..length];
        }

        private static byte[] CombineBytes(byte[] first, byte[] second)
        {
            var combined = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
            return combined;
        }

        #endregion

        private sealed class HKDF : IDisposable
        {
            private readonly HMAC _hmac;
            private readonly byte[] _prk;

            public HKDF(HashAlgorithmName hashAlgorithm, byte[] ikm, byte[] salt)
            {
                _hmac = hashAlgorithm.Name switch
                {
                    "SHA256" => new HMACSHA256(salt),
                    "SHA512" => new HMACSHA512(salt),
                    _ => throw new ArgumentException("Unsupported hash algorithm", nameof(hashAlgorithm))
                };

                _prk = _hmac.ComputeHash(ikm);
                _hmac.Key = _prk;
            }

            public void DeriveKey(byte[] info, Span<byte> output)
            {
                int hashLength = _prk.Length;
                int iterations = (output.Length + hashLength - 1) / hashLength;

                byte[] t = Array.Empty<byte>();
                int offset = 0;

                for (byte i = 1; i <= iterations; i++)
                {
                    var block = CombineBytes(t, CombineBytes(info, new[] { i }));
                    t = _hmac.ComputeHash(block);

                    int copyLength = Math.Min(hashLength, output.Length - offset);
                    t.AsSpan(0, copyLength).CopyTo(output.Slice(offset, copyLength));
                    offset += copyLength;
                }
            }

            public void Dispose()
            {
                _hmac.Dispose();
                CryptographicOperations.ZeroMemory(_prk);
            }
        }
    }
}
