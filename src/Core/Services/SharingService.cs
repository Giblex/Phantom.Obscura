using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed class SharingService
    {

        public Task<SharedCredential> CreateShareAsync(Credential credential, RSAParameters recipientPublicKey)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));

            string plainJson = JsonSerializer.Serialize(credential);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainJson);

            byte[] symKey = new byte[32];
            RandomNumberGenerator.Fill(symKey);

            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);
            byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
            byte[] ciphertext = new byte[plainBytes.Length];

            using (var aes = new AesGcm(symKey.AsSpan(), tag.Length))
            {
                aes.Encrypt(nonce, plainBytes, ciphertext, tag, null);
            }

            var encPayload = new
            {
                nonce = Convert.ToBase64String(nonce),
                tag = Convert.ToBase64String(tag),
                ciphertext = Convert.ToBase64String(ciphertext)
            };
            string encJson = JsonSerializer.Serialize(encPayload);
            string encCredentialBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(encJson));

            byte[] encryptedKey;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(recipientPublicKey);
                encryptedKey = rsa.Encrypt(symKey, RSAEncryptionPadding.OaepSHA256);
            }

            Array.Clear(symKey, 0, symKey.Length);
            Array.Clear(plainBytes, 0, plainBytes.Length);

            var result = new SharedCredential
            {
                Title = credential.Title,
                EncryptedCredentialBase64 = encCredentialBase64,
                EncryptedKeyBase64 = Convert.ToBase64String(encryptedKey),
                CreatedUtc = DateTimeOffset.UtcNow
            };

            return Task.FromResult(result);
        }

        public Credential DecryptShare(SharedCredential share, RSAParameters recipientPrivateKey)
        {
            if (share == null) throw new ArgumentNullException(nameof(share));

            byte[] encryptedKey = Convert.FromBase64String(share.EncryptedKeyBase64);
            byte[] symKey;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(recipientPrivateKey);
                symKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
            }

            byte[] encJsonBytes = Convert.FromBase64String(share.EncryptedCredentialBase64);
            string encJson = Encoding.UTF8.GetString(encJsonBytes);
            if (!JsonUtils.TryParseRecovering(encJson, out var doc, out var docErr))
            {
                throw new FormatException($"Encrypted credential JSON malformed: {docErr}");
            }
            using (var d = doc ?? throw new FormatException("Encrypted credential JSON parse returned null document"))
            {
                var root = d.RootElement;
                string nonceStr = root.GetProperty("nonce").GetString() ?? throw new FormatException("Missing or null 'nonce' in encrypted payload");
                string tagStr = root.GetProperty("tag").GetString() ?? throw new FormatException("Missing or null 'tag' in encrypted payload");
                string ciphertextStr = root.GetProperty("ciphertext").GetString() ?? throw new FormatException("Missing or null 'ciphertext' in encrypted payload");
                byte[] nonce = Convert.FromBase64String(nonceStr);
                byte[] tag = Convert.FromBase64String(tagStr);
                byte[] ciphertext = Convert.FromBase64String(ciphertextStr);
                byte[] plain = new byte[ciphertext.Length];

                using (var aes = new AesGcm(symKey.AsSpan(), AesGcm.TagByteSizes.MaxSize))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plain, null);
                }
                string json = Encoding.UTF8.GetString(plain);
                var credential = JsonSerializer.Deserialize<Credential>(json) ?? throw new InvalidOperationException("Failed to deserialize credential");

                Array.Clear(symKey, 0, symKey.Length);
                Array.Clear(plain, 0, plain.Length);
                return credential;
            }
        }
    }
}

