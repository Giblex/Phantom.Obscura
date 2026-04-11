using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class PolicyEngineTests
    {
        [Fact]
        public void EnforceManifestPolicy_UsesSemanticComparison()
        {
            var policy = new ObscuraPolicy
            {
                Manifest = new ObscuraPolicy.ManifestPolicy
                {
                    RequireSignature = false,
                    MinVersion = "1.2.0",
                    MaxVersion = "1.10.0"
                }
            };

            var engine = new PolicyEngine(policy);

            // 1.10 should be within [1.2, 1.10] semantically
            engine.EnforceManifestPolicy("1.10.0", manifestHasValidSignature: true);

            Assert.Throws<PolicyViolationException>(() =>
                engine.EnforceManifestPolicy("1.1.9", manifestHasValidSignature: true));

            Assert.Throws<PolicyViolationException>(() =>
                engine.EnforceManifestPolicy("1.10.1", manifestHasValidSignature: true));
        }

        [Fact]
        public void UsbKeyFile_LoadAndVerify_RejectsUnsignedFileWhenVerifierProvided()
        {
            using var verifier = ECDsa.Create(ECCurve.NamedCurves.nistP521);
            var json = JsonSerializer.Serialize(new
            {
                keyId = "primary",
                publicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                expiresAt = 0L
            });

            Assert.Throws<CryptographicException>(() => UsbKeyFile.LoadAndVerify(json, verifier));
        }

        [Fact]
        public void UsbKeyFile_LoadAndVerify_AcceptsSignedFileWhenVerifierProvided()
        {
            using var verifier = ECDsa.Create(ECCurve.NamedCurves.nistP521);
            var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = new
            {
                keyId = "primary",
                label = "Primary Key",
                publicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                issuedAt,
                expiresAt = 0L,
                volumeSerial = "ABC123"
            };

            var canonicalData = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            var signature = verifier.SignData(Encoding.UTF8.GetBytes(canonicalData), HashAlgorithmName.SHA512);
            var json = JsonSerializer.Serialize(new
            {
                payload.keyId,
                payload.label,
                payload.publicKey,
                payload.issuedAt,
                payload.expiresAt,
                payload.volumeSerial,
                signature = Convert.ToBase64String(signature)
            });

            var keyFile = UsbKeyFile.LoadAndVerify(json, verifier);

            Assert.Equal(payload.keyId, keyFile.KeyId);
            Assert.Equal(payload.volumeSerial, keyFile.VolumeSerial);
        }
    }
}
