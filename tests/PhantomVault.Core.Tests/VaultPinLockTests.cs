using System;
using PhantomVault.Core.Models;
using PhantomVault.Core.Security;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// The soft-lock PIN record lives in the encrypted manifest and is verified against the
    /// in-memory copy. These cover the record itself: set, verify, clear, and rejection of
    /// malformed or altered records.
    /// </summary>
    public sealed class VaultPinLockTests
    {
        [Fact]
        public void SetPin_ThenVerify_AcceptsOnlyThatPin()
        {
            var manifest = new VaultManifest();

            VaultPinLock.SetPin(manifest, "482913");

            Assert.True(VaultPinLock.IsConfigured(manifest));
            Assert.True(VaultPinLock.Verify(manifest, "482913"));
            Assert.False(VaultPinLock.Verify(manifest, "482914"));
            Assert.False(VaultPinLock.Verify(manifest, ""));
            Assert.False(VaultPinLock.Verify(manifest, null));
        }

        [Fact]
        public void SetPin_UsesAFreshSaltEachTime()
        {
            var first = new VaultManifest();
            var second = new VaultManifest();

            VaultPinLock.SetPin(first, "123456");
            VaultPinLock.SetPin(second, "123456");

            Assert.NotEqual(first.PinSaltBase64, second.PinSaltBase64);
            Assert.NotEqual(first.PinHashBase64, second.PinHashBase64);
        }

        [Fact]
        public void ClearPin_RemovesTheRecord()
        {
            var manifest = new VaultManifest();
            VaultPinLock.SetPin(manifest, "123456");

            VaultPinLock.ClearPin(manifest);

            Assert.False(VaultPinLock.IsConfigured(manifest));
            Assert.False(VaultPinLock.Verify(manifest, "123456"));
        }

        [Fact]
        public void Verify_IsFalse_WithoutAManifestOrPin()
        {
            Assert.False(VaultPinLock.Verify(null, "123456"));
            Assert.False(VaultPinLock.Verify(new VaultManifest(), "123456"));
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("123456789")]
        [InlineData("12a456")]
        [InlineData("")]
        public void SetPin_RejectsMalformedPins(string pin)
        {
            Assert.Throws<ArgumentException>(() => VaultPinLock.SetPin(new VaultManifest(), pin));
        }

        [Fact]
        public void Verify_IsFalse_WhenTheStoredHashIsAltered()
        {
            var manifest = new VaultManifest();
            VaultPinLock.SetPin(manifest, "123456");

            var hash = Convert.FromBase64String(manifest.PinHashBase64!);
            hash[0] ^= 0x01;
            manifest.PinHashBase64 = Convert.ToBase64String(hash);

            Assert.False(VaultPinLock.Verify(manifest, "123456"));
        }

        [Fact]
        public void Verify_IsFalse_ForAMalformedRecord()
        {
            var manifest = new VaultManifest
            {
                PinSaltBase64 = "not base64!",
                PinHashBase64 = "also not base64!"
            };

            Assert.False(VaultPinLock.Verify(manifest, "123456"));
        }
    }
}
