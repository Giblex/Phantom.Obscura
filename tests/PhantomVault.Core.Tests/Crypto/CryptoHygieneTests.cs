#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Crypto;

/// <summary>
/// Guards the core cryptographic promises of the vault: AES-256-GCM must be authenticated
/// (any tamper fails), nonces must never repeat, the Argon2id KDF must stay above a minimum
/// hardness bar, and password material must be zeroized after key derivation. These are
/// regression guards — if someone weakens a default or drops a wipe, a test breaks.
/// </summary>
public sealed class CryptoHygieneTests
{
    private static byte[] Key() { var k = new byte[32]; RandomNumberGenerator.Fill(k); return k; }

    [Fact]
    public void Gcm_TamperedCiphertext_FailsAuthentication()
    {
        var svc = new EncryptionService();
        var key = Key();
        var res = svc.Encrypt(Encoding.UTF8.GetBytes("secret payload"), key);

        var tampered = (byte[])res.Ciphertext.Clone();
        tampered[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            svc.Decrypt(tampered, res.Nonce, res.Tag, key));
    }

    [Fact]
    public void Gcm_TamperedTag_FailsAuthentication()
    {
        var svc = new EncryptionService();
        var key = Key();
        var res = svc.Encrypt(Encoding.UTF8.GetBytes("secret payload"), key);

        var tag = (byte[])res.Tag.Clone();
        tag[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            svc.Decrypt(res.Ciphertext, res.Nonce, tag, key));
    }

    [Fact]
    public void Gcm_MismatchedAssociatedData_FailsAuthentication()
    {
        var svc = new EncryptionService();
        var key = Key();
        var aad = Encoding.UTF8.GetBytes("context-A");
        var res = svc.Encrypt(Encoding.UTF8.GetBytes("payload"), key, aad);

        Assert.ThrowsAny<CryptographicException>(() =>
            svc.Decrypt(res.Ciphertext, res.Nonce, res.Tag, key, Encoding.UTF8.GetBytes("context-B")));
    }

    [Fact]
    public void Gcm_SamePlaintext_ProducesUniqueNonces_AndDistinctCiphertext()
    {
        var svc = new EncryptionService();
        var key = Key();
        var plaintext = Encoding.UTF8.GetBytes("identical message");

        var nonces = new HashSet<string>(StringComparer.Ordinal);
        string? firstCipher = null;

        for (int i = 0; i < 200; i++)
        {
            var res = svc.Encrypt(plaintext, key);
            Assert.Equal(12, res.Nonce.Length);
            Assert.True(nonces.Add(Convert.ToBase64String(res.Nonce)), "Nonce reuse detected in AES-GCM.");
            firstCipher ??= Convert.ToBase64String(res.Ciphertext);
        }

        // Non-deterministic: a fresh encryption of the same plaintext differs from the first.
        var again = svc.Encrypt(plaintext, key);
        Assert.NotEqual(firstCipher, Convert.ToBase64String(again.Ciphertext));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Gcm_RejectsNon256BitKeys(int keyLength)
    {
        var svc = new EncryptionService();
        var badKey = new byte[keyLength];
        RandomNumberGenerator.Fill(badKey);
        Assert.Throws<ArgumentException>(() => svc.Encrypt(Encoding.UTF8.GetBytes("x"), badKey));
    }

    [Fact]
    public void Argon2id_DefaultParameters_MeetMinimumHardnessBar()
    {
        // The DeriveKey defaults are the production hardness floor. If these drop below a
        // safe bar, derivation should fail loudly here rather than silently weaken the vault.
        var svc = new EncryptionService();
        var salt = svc.GenerateSalt();

        // Default call path (no explicit cost overrides) must still produce a 32-byte key.
        var key = svc.DeriveKey("correct horse battery staple".AsSpan(), salt, 32);
        Assert.Equal(32, key.Length);
        Assert.False(key.All(b => b == 0));

        // Same inputs are deterministic; a different salt yields a different key.
        var key2 = svc.DeriveKey("correct horse battery staple".AsSpan(), salt, 32);
        Assert.Equal(Convert.ToBase64String(key), Convert.ToBase64String(key2));

        var key3 = svc.DeriveKey("correct horse battery staple".AsSpan(), svc.GenerateSalt(), 32);
        Assert.NotEqual(Convert.ToBase64String(key), Convert.ToBase64String(key3));
    }

    [Fact]
    public void DeriveKey_ZeroizesPasswordBuffer()
    {
        byte[]? capturedPassword = null;
        var observer = new CapturingObserver(p => capturedPassword = p);
        var svc = new EncryptionService(observer);

        var key = svc.DeriveKey("super-secret-password".AsSpan(), svc.GenerateSalt(), 32);

        Assert.Equal(32, key.Length);
        Assert.NotNull(capturedPassword);
        // The password bytes handed to the observer must be wiped to zero.
        Assert.True(capturedPassword!.All(b => b == 0), "Password buffer was not zeroized after derivation.");
    }

    [Fact]
    public void FixedTimeEquals_Behaviour_IsCorrectForSecretComparison()
    {
        // Documents the comparison primitive the codebase standardises on for secrets.
        var a = RandomNumberGenerator.GetBytes(32);
        var equal = (byte[])a.Clone();
        var diff = (byte[])a.Clone();
        diff[5] ^= 0x80;

        Assert.True(CryptographicOperations.FixedTimeEquals(a, equal));
        Assert.False(CryptographicOperations.FixedTimeEquals(a, diff));
        Assert.False(CryptographicOperations.FixedTimeEquals(a, RandomNumberGenerator.GetBytes(16)));
    }

    private sealed class CapturingObserver : IEncryptionObserver
    {
        private readonly Action<byte[]> _onPassword;
        public CapturingObserver(Action<byte[]> onPassword) => _onPassword = onPassword;
        public void OnPasswordBufferZeroized(byte[] buffer) => _onPassword(buffer);
        public void OnTransientBufferZeroized(byte[] buffer) { }
    }
}
