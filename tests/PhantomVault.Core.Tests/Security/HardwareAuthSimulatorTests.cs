using System;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    public sealed class HardwareAuthSimulatorTests
    {
        [Fact]
        public void DeterministicAuthenticatorSimulator_VerifiesChallengeAndRejectsWrongRelyingParty()
        {
            using var simulator = new DeterministicAuthenticatorSimulator("vault.local");
            byte[] challenge = SHA256.HashData(Encoding.UTF8.GetBytes("challenge"));

            var assertion = simulator.Sign(challenge);

            Assert.True(simulator.Verify("vault.local", challenge, assertion));
            Assert.False(simulator.Verify("evil.local", challenge, assertion));
        }

        [Fact]
        public void YubiKeyFido2Implementation_FailsClosedWhenInvoked()
        {
            var service = new YubiKeyServiceImpl();

            Assert.Throws<FeatureNotAvailableException>(() =>
                service.RegisterCredential(
                    "vault.local",
                    Encoding.UTF8.GetBytes("user"),
                    "User",
                    RandomNumberGenerator.GetBytes(32)));
        }

        private sealed class DeterministicAuthenticatorSimulator : IDisposable
        {
            private readonly string _relyingPartyId;
            private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            public DeterministicAuthenticatorSimulator(string relyingPartyId)
            {
                _relyingPartyId = relyingPartyId;
            }

            public byte[] Sign(byte[] challenge)
                => _key.SignData(BuildSignedData(_relyingPartyId, challenge), HashAlgorithmName.SHA256);

            public bool Verify(string relyingPartyId, byte[] challenge, byte[] signature)
            {
                using var verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(_key.ExportSubjectPublicKeyInfo(), out _);
                return verifier.VerifyData(BuildSignedData(relyingPartyId, challenge), signature, HashAlgorithmName.SHA256);
            }

            public void Dispose() => _key.Dispose();

            private static byte[] BuildSignedData(string relyingPartyId, byte[] challenge)
            {
                byte[] rpHash = SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId));
                byte[] challengeHash = SHA256.HashData(challenge);
                byte[] signedData = new byte[rpHash.Length + challengeHash.Length];
                Buffer.BlockCopy(rpHash, 0, signedData, 0, rpHash.Length);
                Buffer.BlockCopy(challengeHash, 0, signedData, rpHash.Length, challengeHash.Length);
                return signedData;
            }
        }
    }
}
