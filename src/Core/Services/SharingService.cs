using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Implements a hybrid encryption scheme for securely sharing
    /// individual credentials or folders with trusted contacts. A
    /// randomly generated symmetric key is used to encrypt the
    /// credential payload with AES‑256‑GCM. That symmetric key is then
    /// encrypted with the recipient's RSA‑4096 public key. This method
    /// ensures that only the intended recipient can decrypt the
    /// symmetric key and access the shared data.
    /// </summary>
    public sealed class SharingService
    {
        /// <summary>
        /// Creates a share package for the given credential. The caller
        /// must provide the recipient's RSA public key parameters. The
        /// resulting <see cref="SharedCredential"/> can be serialized
        /// and transmitted to the recipient over an untrusted channel.
        /// </summary>
        /// <param name="credential">The credential to share.</param>
        /// <param name="recipientPublicKey">Recipient's RSA public key.</param>
        public Task<SharedCredential> CreateShareAsync(Credential credential, RSAParameters recipientPublicKey)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            // Serialize the credential to JSON
            string plainJson = JsonSerializer.Serialize(credential);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainJson);

            // Generate a random 256‑bit symmetric key
            byte[] symKey = new byte[32];
            RandomNumberGenerator.Fill(symKey);
            // Generate a random nonce for AES‑GCM
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);
            byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
            byte[] ciphertext = new byte[plainBytes.Length];
            // Use the ReadOnlySpan<byte> constructor with explicit tag size.
            using (var aes = new AesGcm(symKey.AsSpan(), tag.Length))
            {
                aes.Encrypt(nonce, plainBytes, ciphertext, tag, null);
            }
            // Build encrypted payload JSON
            var encPayload = new
            {
                nonce = Convert.ToBase64String(nonce),
                tag = Convert.ToBase64String(tag),
                ciphertext = Convert.ToBase64String(ciphertext)
            };
            string encJson = JsonSerializer.Serialize(encPayload);
            string encCredentialBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(encJson));

            // Encrypt the symmetric key with recipient's RSA public key
            byte[] encryptedKey;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(recipientPublicKey);
                encryptedKey = rsa.Encrypt(symKey, RSAEncryptionPadding.OaepSHA256);
            }
            // Clear sensitive data from memory
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

        /// <summary>
        /// Decrypts a share package using the recipient's private RSA key.
        /// Returns the original credential. The caller must ensure they
        /// control the corresponding private key.
        /// </summary>
        /// <param name="share">The shared credential package.</param>
        /// <param name="recipientPrivateKey">Recipient's RSA private key.</param>
        public Credential DecryptShare(SharedCredential share, RSAParameters recipientPrivateKey)
        {
            if (share == null) throw new ArgumentNullException(nameof(share));
            // Decode the encrypted symmetric key
            byte[] encryptedKey = Convert.FromBase64String(share.EncryptedKeyBase64);
            byte[] symKey;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(recipientPrivateKey);
                symKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
            }
            // Decode encrypted credential JSON
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
                // Use the ReadOnlySpan<byte> constructor with explicit tag size.
                using (var aes = new AesGcm(symKey.AsSpan(), AesGcm.TagByteSizes.MaxSize))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plain, null);
                }
                string json = Encoding.UTF8.GetString(plain);
                var credential = JsonSerializer.Deserialize<Credential>(json) ?? throw new InvalidOperationException("Failed to deserialize credential");
                // Clear sensitive data
                Array.Clear(symKey, 0, symKey.Length);
                Array.Clear(plain, 0, plain.Length);
                return credential;
            }
        }
    }
}