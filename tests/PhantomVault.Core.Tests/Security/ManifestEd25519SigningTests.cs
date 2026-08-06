using System;
using System.Security;
using GiblexVault.Security.ZK.Keys;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    /// <summary>
    /// Phase 2: asymmetric (Ed25519) manifest signing. Any enrolled device can
    /// verify; only the vault signing private-key holder can produce a valid
    /// signature. Legacy HMAC-only manifests must still load.
    /// </summary>
    public sealed class ManifestEd25519SigningTests
    {
        private static VaultManifest NewManifest() => new()
        {
            ContainerPath = "G:/vault/obscura.vol",
            SaltBase64 = Convert.ToBase64String(new byte[16]),
            DeviceId = Guid.NewGuid().ToString("N"),
            Algorithm = "AES-256-GCM",
            Version = 2,
        };

        [Fact]
        public void SignEd25519_Then_Verify_RoundTrips()
        {
            var (pub, priv) = UserKeys.Generate();
            var manifest = NewManifest();

            ManifestService.SignManifestEd25519(manifest, priv, pub);

            Assert.False(string.IsNullOrEmpty(manifest.ManifestEd25519SignatureBase64));
            Assert.Equal(Convert.ToBase64String(pub), manifest.ManifestSigningPublicKeyBase64);
            Assert.True(ManifestService.VerifyManifestEd25519(manifest, requireSignature: true));
        }

        [Fact]
        public void SignEd25519_IncrementsSequence()
        {
            var (pub, priv) = UserKeys.Generate();
            var manifest = NewManifest();
            long before = manifest.ManifestSequence;

            ManifestService.SignManifestEd25519(manifest, priv, pub);

            Assert.Equal(before + 1, manifest.ManifestSequence);
        }

        [Fact]
        public void Verify_FailsWhenSecurityFieldTampered()
        {
            var (pub, priv) = UserKeys.Generate();
            var manifest = NewManifest();
            ManifestService.SignManifestEd25519(manifest, priv, pub);

            manifest.ContainerPath = "G:/vault/evil.vol";

            Assert.Throws<SecurityException>(() => ManifestService.VerifyManifestEd25519(manifest));
        }

        [Fact]
        public void Verify_FailsWhenSigningKeySwapped()
        {
            var (pub, priv) = UserKeys.Generate();
            var manifest = NewManifest();
            ManifestService.SignManifestEd25519(manifest, priv, pub);

            // Attacker substitutes their own public key but cannot re-sign as the vault.
            var (attackerPub, _) = UserKeys.Generate();
            manifest.ManifestSigningPublicKeyBase64 = Convert.ToBase64String(attackerPub);

            Assert.Throws<SecurityException>(() => ManifestService.VerifyManifestEd25519(manifest));
        }

        [Fact]
        public void Verify_AllowsLegacyManifestWhenSignatureNotRequired()
        {
            var manifest = NewManifest(); // no asymmetric signature

            Assert.True(ManifestService.VerifyManifestEd25519(manifest, requireSignature: false));
        }

        [Fact]
        public void Verify_ThrowsForLegacyManifestWhenSignatureRequired()
        {
            var manifest = NewManifest();

            Assert.Throws<SecurityException>(() => ManifestService.VerifyManifestEd25519(manifest, requireSignature: true));
        }

        [Fact]
        public void HmacAndEd25519_CoexistOnSameManifest()
        {
            var (pub, priv) = UserKeys.Generate();
            byte[] hmacKey = new byte[32];
            Random.Shared.NextBytes(hmacKey);

            var manifest = NewManifest();
            ManifestService.SignManifestEd25519(manifest, priv, pub);
            // Legacy HMAC path still computes/verifies over the same canonical data.
            manifest.IntegritySignatureBase64 = ManifestService.ComputeIntegritySignature(manifest, hmacKey);

            Assert.True(ManifestService.VerifyManifestEd25519(manifest, requireSignature: true));
            Assert.True(ManifestService.VerifyIntegritySignature(manifest, hmacKey, requireSignature: true));
        }
    }
}
