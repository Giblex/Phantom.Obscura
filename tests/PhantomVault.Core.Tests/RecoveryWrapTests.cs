using System;
using System.Security.Cryptography;
using System.Text;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Recovery;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class RecoveryWrapTests
    {
        private const int LegacyNonceSize = 12;
        private const int TagSize = 16;
        private static readonly byte[] WrapMagic = Encoding.ASCII.GetBytes("RKW1");
        private static readonly byte[] RecoveryWrapInfo = Encoding.UTF8.GetBytes("PhantomVault.Recovery.Wrap.v1");

        [Fact]
        public void WrapMasterKey_UsesRandomSaltAndVersionedEnvelope()
        {
            var masterKey = RandomNumberGenerator.GetBytes(32);
            var recoveryKey = Recovery.GenerateRecoveryKey();

            var wrappedOne = Recovery.WrapMasterKey(masterKey, recoveryKey);
            var wrappedTwo = Recovery.WrapMasterKey(masterKey, recoveryKey);

            Assert.True(wrappedOne.AsSpan(0, WrapMagic.Length).SequenceEqual(WrapMagic));
            Assert.True(wrappedTwo.AsSpan(0, WrapMagic.Length).SequenceEqual(WrapMagic));
            Assert.NotEqual(Convert.ToBase64String(wrappedOne), Convert.ToBase64String(wrappedTwo));
            Assert.Equal(masterKey, Recovery.UnwrapMasterKey(wrappedOne, recoveryKey));
            Assert.Equal(masterKey, Recovery.UnwrapMasterKey(wrappedTwo, recoveryKey));
        }

        [Fact]
        public void UnwrapMasterKey_LegacyEnvelope_RemainsSupported()
        {
            var masterKey = RandomNumberGenerator.GetBytes(32);
            var recoveryKey = Recovery.GenerateRecoveryKey();
            var legacyWrapped = CreateLegacyWrappedMasterKey(masterKey, recoveryKey);

            var unwrapped = Recovery.UnwrapMasterKey(legacyWrapped, recoveryKey);

            Assert.Equal(masterKey, unwrapped);
        }

        [Fact]
        public void WrapMasterKey_NewEnvelope_DoesNotMatchLegacyFixedSaltOutput()
        {
            var masterKey = RandomNumberGenerator.GetBytes(32);
            var recoveryKey = Recovery.GenerateRecoveryKey();

            var modernWrapped = Recovery.WrapMasterKey(masterKey, recoveryKey);
            var legacyWrapped = CreateLegacyWrappedMasterKey(masterKey, recoveryKey);

            Assert.False(modernWrapped.AsSpan().SequenceEqual(legacyWrapped));
        }

        private static byte[] CreateLegacyWrappedMasterKey(byte[] masterKey, byte[] recoveryKey)
        {
            var salt = new byte[32];
            var kek = Hkdf.Sha256(recoveryKey, salt, RecoveryWrapInfo);
            var nonce = RandomNumberGenerator.GetBytes(LegacyNonceSize);
            var ciphertext = new byte[masterKey.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(kek.AsSpan(), TagSize);

            try
            {
                aes.Encrypt(nonce, masterKey, ciphertext, tag);

                var wrapped = new byte[LegacyNonceSize + ciphertext.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, wrapped, 0, LegacyNonceSize);
                Buffer.BlockCopy(ciphertext, 0, wrapped, LegacyNonceSize, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, wrapped, LegacyNonceSize + ciphertext.Length, TagSize);
                return wrapped;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }
        }
    }
}
