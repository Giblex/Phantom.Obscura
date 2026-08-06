using System;
using System.IO;
using System.Security.Cryptography;
using GiblexVault.Security.ZK.Keys;
using PhantomVault.Core.Models.Security;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    /// <summary>
    /// Foundation tests for the public-key device identity model: keypair
    /// generation/persistence, Ed25519 sign/verify, and device vault-key
    /// wrap/unwrap (ECIES over X25519).
    /// </summary>
    public sealed class DeviceIdentityServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public DeviceIdentityServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PhantomObscuraIdentityTests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void GetOrCreateLocalIdentity_GeneratesStableIdentityWithPublicKeys()
        {
            var svc = new DeviceIdentityService(_tempDir);

            var first = svc.GetOrCreateLocalIdentity("Test Device");

            Assert.False(string.IsNullOrWhiteSpace(first.DeviceId));
            Assert.False(string.IsNullOrWhiteSpace(first.SigningPublicKeyBase64));
            Assert.False(string.IsNullOrWhiteSpace(first.AgreementPublicKeyBase64));
            Assert.Equal("Test Device", first.FriendlyName);
            Assert.Equal(DeviceRole.Owner, first.Role);

            // A fresh service instance over the same directory must load the same identity.
            var svc2 = new DeviceIdentityService(_tempDir);
            var second = svc2.GetOrCreateLocalIdentity();
            Assert.Equal(first.DeviceId, second.DeviceId);
            Assert.Equal(first.SigningPublicKeyBase64, second.SigningPublicKeyBase64);
            Assert.Equal(first.AgreementPublicKeyBase64, second.AgreementPublicKeyBase64);
        }

        [Fact]
        public void Sign_Then_Verify_RoundTrips()
        {
            var svc = new DeviceIdentityService(_tempDir);
            var identity = svc.GetOrCreateLocalIdentity();
            byte[] message = RandomNumberGenerator.GetBytes(128);

            byte[] sig = svc.Sign(message);

            Assert.True(svc.Verify(message, sig, identity.SigningPublicKeyBase64));
        }

        [Fact]
        public void Verify_FailsOnTamperedMessage()
        {
            var svc = new DeviceIdentityService(_tempDir);
            var identity = svc.GetOrCreateLocalIdentity();
            byte[] message = RandomNumberGenerator.GetBytes(64);
            byte[] sig = svc.Sign(message);

            message[0] ^= 0xFF;

            Assert.False(svc.Verify(message, sig, identity.SigningPublicKeyBase64));
        }

        [Fact]
        public void WrapVaultKeyToDevice_UnwrapsOnRecipientDevice()
        {
            // Recipient device owns its identity/keys.
            string recipientDir = Path.Combine(_tempDir, "recipient");
            var recipient = new DeviceIdentityService(recipientDir);
            var recipientIdentity = recipient.GetOrCreateLocalIdentity("Recipient");

            // Authorizing device wraps a vault key to the recipient's public key.
            string ownerDir = Path.Combine(_tempDir, "owner");
            var owner = new DeviceIdentityService(ownerDir);
            owner.GetOrCreateLocalIdentity("Owner");

            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            byte[] wrapped = owner.WrapVaultKeyToDevice(vaultKey, recipientIdentity.AgreementPublicKeyBase64);

            byte[] unwrapped = recipient.UnwrapVaultKey(wrapped);

            Assert.Equal(vaultKey, unwrapped);
        }

        [Fact]
        public void UnwrapVaultKey_FailsForWrongDevice()
        {
            var recipient = new DeviceIdentityService(Path.Combine(_tempDir, "r"));
            var recipientIdentity = recipient.GetOrCreateLocalIdentity();

            var attacker = new DeviceIdentityService(Path.Combine(_tempDir, "a"));
            attacker.GetOrCreateLocalIdentity();

            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            byte[] wrapped = DeviceAgreementKeys.WrapVaultKey(
                vaultKey, Convert.FromBase64String(recipientIdentity.AgreementPublicKeyBase64));

            // The attacker's device key cannot unwrap a blob sealed to the recipient.
            Assert.ThrowsAny<CryptographicException>(() => attacker.UnwrapVaultKey(wrapped));
        }

        [Fact]
        public void WrapVaultKey_TamperedCiphertext_FailsAuthentication()
        {
            var recipient = new DeviceIdentityService(Path.Combine(_tempDir, "r2"));
            var recipientIdentity = recipient.GetOrCreateLocalIdentity();

            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            byte[] wrapped = DeviceAgreementKeys.WrapVaultKey(
                vaultKey, Convert.FromBase64String(recipientIdentity.AgreementPublicKeyBase64));

            wrapped[^1] ^= 0xFF; // flip a tag byte

            Assert.ThrowsAny<CryptographicException>(() => recipient.UnwrapVaultKey(wrapped));
        }
    }
}
