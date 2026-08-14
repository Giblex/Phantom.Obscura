using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace PhantomVault.Core.Services
{

    public sealed class LayeredEncryptionService
    {
        private readonly EncryptionService _encryptionService;

        public LayeredEncryptionService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        public enum SecurityLevel
        {
            Standard = 2,
            Sensitive = 3,
            Maximum = 5
        }

        public class LayeredEncryptionResult
        {
            public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
            public byte[] Nonce1 { get; set; } = Array.Empty<byte>();
            public byte[] Tag1 { get; set; } = Array.Empty<byte>();
            public byte[] Nonce2 { get; set; } = Array.Empty<byte>();
            public byte[] Tag2 { get; set; } = Array.Empty<byte>();
            // Layer 3 — ChaCha20-Poly1305 (replaces former AES-CBC)
            public byte[] Nonce3 { get; set; } = Array.Empty<byte>();
            public byte[] Tag3 { get; set; } = Array.Empty<byte>();
            // Layer 4 — Twofish-GCM (replaces former Twofish-CBC)
            public byte[] Nonce4 { get; set; } = Array.Empty<byte>();
            public byte[] Tag4 { get; set; } = Array.Empty<byte>();
            // Layer 5 — Serpent-GCM (replaces former Serpent-CBC)
            public byte[] Nonce5 { get; set; } = Array.Empty<byte>();
            public byte[] Tag5 { get; set; } = Array.Empty<byte>();
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

                var key2 = DeriveLayerKey(masterKey, salt, "Layer2-ChaCha20");
                var enc2 = EncryptChaCha20(currentData, key2, contextData);
                result.Nonce2 = enc2.Nonce;
                result.Tag2 = enc2.Tag;
                currentData = enc2.Ciphertext;
                CryptographicOperations.ZeroMemory(key2);

                if (level == SecurityLevel.Sensitive)
                {
                    // Layer 3: ChaCha20-Poly1305 (authenticated — replaces former AES-CBC)
                    var layer3Key = DeriveLayerKey(masterKey, salt, "Layer3-ChaCha20");
                    var layer3Result = EncryptChaCha20(currentData, layer3Key, contextData);
                    result.Nonce3 = layer3Result.Nonce;
                    result.Tag3 = layer3Result.Tag;
                    result.Ciphertext = layer3Result.Ciphertext;
                    CryptographicOperations.ZeroMemory(layer3Key);
                    return result;
                }

                // Layer 3: ChaCha20-Poly1305
                var key3 = DeriveLayerKey(masterKey, salt, "Layer3-ChaCha20");
                var enc3 = EncryptChaCha20(currentData, key3, contextData);
                result.Nonce3 = enc3.Nonce;
                result.Tag3 = enc3.Tag;
                currentData = enc3.Ciphertext;
                CryptographicOperations.ZeroMemory(key3);

                // Layer 4: Twofish-GCM (authenticated — replaces former Twofish-CBC)
                var key4 = DeriveLayerKey(masterKey, salt, "Layer4-Twofish", contextData);
                var enc4 = EncryptTwofishGcm(currentData, key4, contextData);
                result.Nonce4 = enc4.Nonce;
                result.Tag4 = enc4.Tag;
                currentData = enc4.Ciphertext;
                CryptographicOperations.ZeroMemory(key4);

                // Layer 5: Serpent-GCM (authenticated — replaces former Serpent-CBC)
                var key5 = DeriveLayerKey(masterKey, salt, "Layer5-Serpent", contextData);
                var enc5 = EncryptSerpentGcm(currentData, key5, contextData);
                result.Nonce5 = enc5.Nonce;
                result.Tag5 = enc5.Tag;
                result.Ciphertext = enc5.Ciphertext;
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
                    // Layer 5: Serpent-GCM
                    var key5 = DeriveLayerKey(masterKey, salt, "Layer5-Serpent", contextData);
                    currentData = ReplaceBuffer(currentData, DecryptSerpentGcm(currentData, key5, encryptedData.Nonce5, encryptedData.Tag5, contextData), ref ownsCurrentData);
                    CryptographicOperations.ZeroMemory(key5);

                    // Layer 4: Twofish-GCM
                    var key4 = DeriveLayerKey(masterKey, salt, "Layer4-Twofish", contextData);
                    currentData = ReplaceBuffer(currentData, DecryptTwofishGcm(currentData, key4, encryptedData.Nonce4, encryptedData.Tag4, contextData), ref ownsCurrentData);
                    CryptographicOperations.ZeroMemory(key4);
                }

                if (encryptedData.Level == SecurityLevel.Maximum || encryptedData.Level == SecurityLevel.Sensitive)
                {
                    // Layer 3: ChaCha20-Poly1305
                    var key3 = DeriveLayerKey(masterKey, salt, "Layer3-ChaCha20");
                    currentData = ReplaceBuffer(currentData, DecryptChaCha20(currentData, key3, encryptedData.Nonce3, encryptedData.Tag3, contextData), ref ownsCurrentData);
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
            byte[] ikmArray = masterKey.ToArray();
            byte[] saltArray = salt.ToArray();
            try
            {
                var info = CombineBytes(Encoding.UTF8.GetBytes(label), context.ToArray());
                var derived = new byte[32];
                using var hkdf = new HKDF(HashAlgorithmName.SHA512, ikmArray, saltArray);
                hkdf.DeriveKey(info, derived);
                return derived;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ikmArray);
                CryptographicOperations.ZeroMemory(saltArray);
            }
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

        private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptTwofishGcm(byte[] plaintext, byte[] key, ReadOnlySpan<byte> aad)
        {
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            var cipher = new GcmBlockCipher(new TwofishEngine());
            var parameters = new AeadParameters(new KeyParameter(key), 128, nonce, aad.ToArray());
            cipher.Init(true, parameters);

            byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
            int len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            len += cipher.DoFinal(output, len);

            // GCM appends the 16-byte tag to the ciphertext output
            byte[] ciphertext = output[..(len - 16)];
            byte[] tag = output[(len - 16)..len];
            return (ciphertext, nonce, tag);
        }

        private byte[] DecryptTwofishGcm(byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, ReadOnlySpan<byte> aad)
        {
            var cipher = new GcmBlockCipher(new TwofishEngine());
            var parameters = new AeadParameters(new KeyParameter(key), 128, nonce, aad.ToArray());
            cipher.Init(false, parameters);

            // Reassemble ciphertext+tag for BouncyCastle
            byte[] input = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, input, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, input, ciphertext.Length, tag.Length);

            byte[] output = new byte[cipher.GetOutputSize(input.Length)];
            int len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += DoFinalChecked(cipher, output, len, "Twofish-GCM");
            return output[..len];
        }

        private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptSerpentGcm(byte[] plaintext, byte[] key, ReadOnlySpan<byte> aad)
        {
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            var cipher = new GcmBlockCipher(new SerpentEngine());
            var parameters = new AeadParameters(new KeyParameter(key), 128, nonce, aad.ToArray());
            cipher.Init(true, parameters);

            byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
            int len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            len += cipher.DoFinal(output, len);

            byte[] ciphertext = output[..(len - 16)];
            byte[] tag = output[(len - 16)..len];
            return (ciphertext, nonce, tag);
        }

        private byte[] DecryptSerpentGcm(byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, ReadOnlySpan<byte> aad)
        {
            var cipher = new GcmBlockCipher(new SerpentEngine());
            var parameters = new AeadParameters(new KeyParameter(key), 128, nonce, aad.ToArray());
            cipher.Init(false, parameters);

            byte[] input = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, input, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, input, ciphertext.Length, tag.Length);

            byte[] output = new byte[cipher.GetOutputSize(input.Length)];
            int len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += DoFinalChecked(cipher, output, len, "Serpent-GCM");
            return output[..len];
        }

        /// <summary>
        /// Finalises a BouncyCastle AEAD decryption, translating a failed tag check
        /// into <see cref="CryptographicException"/>.
        ///
        /// The ChaCha20-Poly1305 layer uses the BCL and already throws
        /// CryptographicException on a bad tag; without this the Twofish and Serpent
        /// layers would instead surface BouncyCastle's InvalidCipherTextException,
        /// so callers had to know which layer failed in order to catch it. The
        /// message deliberately carries no detail about the ciphertext.
        /// </summary>
        private static int DoFinalChecked(GcmBlockCipher cipher, byte[] output, int offset, string layerName)
        {
            try
            {
                return cipher.DoFinal(output, offset);
            }
            catch (InvalidCipherTextException ex)
            {
                throw new CryptographicException(
                    $"Authentication failed while decrypting the {layerName} layer — the data has been tampered with or the key is wrong.",
                    ex);
            }
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

