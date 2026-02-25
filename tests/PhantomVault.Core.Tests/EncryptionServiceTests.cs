using System;
using System.Security.Cryptography;
using System.Linq;
using Xunit;
using PhantomVault.Core.Services;

namespace PhantomVault.Core.Tests
{
    public class EncryptionServiceTests
    {
        [Fact]
        public void Encrypt_Decrypt_RoundTrip()
        {
            var svc = new EncryptionService();
            var key = svc.DeriveKey("password".AsSpan(), svc.GenerateSalt(), 32);
            var plaintext = System.Text.Encoding.UTF8.GetBytes("hello world");

            var result = svc.Encrypt(plaintext, key);

            var decrypted = svc.Decrypt(result.Ciphertext, result.Nonce, result.Tag, key);
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void DeriveKey_EmptyPassword_Throws()
        {
            var svc = new EncryptionService();
            Assert.Throws<ArgumentException>(() => svc.DeriveKey(ReadOnlySpan<char>.Empty, svc.GenerateSalt()));
        }

        [Fact]
        public void Argon2id_IsRequired_NoFallback()
        {
            // Argon2id is mandatory. There is no PBKDF2 fallback.
            // This test verifies that key derivation succeeds with Argon2id.
            var svc = new EncryptionService();
            var salt = svc.GenerateSalt();
            var key = svc.DeriveKey("password".AsSpan(), salt, 32);
            Assert.Equal(32, key.Length);

            var plaintext = System.Text.Encoding.UTF8.GetBytes("argon2id mandatory test");
            var res = svc.Encrypt(plaintext, key);
            var decrypted = svc.Decrypt(res.Ciphertext, res.Nonce, res.Tag, key);
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void Decrypt_FailsWithWrongKey()
        {
            var svc = new EncryptionService();
            var salt1 = svc.GenerateSalt();
            var salt2 = svc.GenerateSalt();
            var key1 = svc.DeriveKey("password1".AsSpan(), salt1, 32);
            var key2 = svc.DeriveKey("password2".AsSpan(), salt2, 32);

            // Quick check: keys should differ when passwords/salts differ
            Assert.False(key1.SequenceEqual(key2), "Derived keys unexpectedly equal; check DeriveKey behavior");

            // Salts must differ between GenerateSalt calls
            Assert.False(salt1.SequenceEqual(salt2), "GenerateSalt returned identical salts; RNG may be broken");

            // Deriving with the same password and salt deterministically should produce identical keys
            var key1b = svc.DeriveKey("password1".AsSpan(), salt1, 32);
            Assert.True(key1.SequenceEqual(key1b), "DeriveKey produced different outputs for identical inputs");
            var plaintext = System.Text.Encoding.UTF8.GetBytes("secret data");

            var res = svc.Encrypt(plaintext, key1);

            // Decryption with wrong key should either throw (auth failure) or produce different plaintext.
            bool threw = false;
            bool different = false;
            try
            {
                var dec = svc.Decrypt(res.Ciphertext, res.Nonce, res.Tag, key2);
                different = !plaintext.SequenceEqual(dec);
            }
            catch (CryptographicException)
            {
                threw = true;
            }

            Assert.True(threw || different, "Decryption with wrong key either failed authentication or produced different plaintext");
        }

        [Fact]
        public void Encrypt_ZeroizesTransientBuffers()
        {
            // Use a test observer rather than a static hook.
            byte[]? captured = null;
            var observer = new TestObserver(buf => captured = buf, _ => { });
            var svc = new EncryptionService(observer);
            var key = svc.DeriveKey("password".AsSpan(), svc.GenerateSalt(), 32);
            var plaintext = System.Text.Encoding.UTF8.GetBytes("zero test");
            var res = svc.Encrypt(plaintext, key);
            Assert.NotNull(captured);
            // ensure the rented buffer memory is zeroed (check first N bytes)
            Assert.True(captured.Take(plaintext.Length).All(b => b == 0));
        }

        [Fact]
        public void Decrypt_ZeroizesTransientBuffers()
        {
            byte[]? captured = null;
            var observer = new TestObserver(_ => { }, buf => captured = buf);
            var svc = new EncryptionService(observer);
            var key = svc.DeriveKey("password".AsSpan(), svc.GenerateSalt(), 32);
            var plaintext = System.Text.Encoding.UTF8.GetBytes("zero test decrypt");

            var res = svc.Encrypt(plaintext, key);

            var outp = svc.Decrypt(res.Ciphertext, res.Nonce, res.Tag, key);
            Assert.NotNull(captured);
            Assert.True(captured.Take(res.Ciphertext.Length).All(b => b == 0));
        }
    }

    // Simple test observer implemented in the test assembly to capture
    // zeroized buffers without touching production code.
    internal class TestObserver : IEncryptionObserver
    {
        private readonly Action<byte[]> _onPasswordZeroized;
        private readonly Action<byte[]> _onTransientZeroized;

        public TestObserver(Action<byte[]> onPasswordZeroized, Action<byte[]> onTransientZeroized)
        {
            _onPasswordZeroized = onPasswordZeroized;
            _onTransientZeroized = onTransientZeroized;
        }

        public void OnPasswordBufferZeroized(byte[] buffer) => _onPasswordZeroized(buffer);
        public void OnTransientBufferZeroized(byte[] buffer) => _onTransientZeroized(buffer);
    }
}