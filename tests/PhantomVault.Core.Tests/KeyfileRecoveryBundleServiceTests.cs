#nullable enable
using System;
using System.Security.Cryptography;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.DomainKeys;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class KeyfileRecoveryBundleServiceTests
    {
        private static KeyfileRecoveryBundleService MakeService()
            => new(
                new RecoveryStoreFactory(new ScopedRecoveryService(new EncryptionService())),
                new RecoveryCodeService());

        [Fact]
        public void Create_ThenRestore_RoundTripsKeyfile_ForEveryCode()
        {
            var keyfile = RandomNumberGenerator.GetBytes(256 * 1024);
            var service = MakeService();

            var bundle = service.Create(keyfile, codeCount: 6, deviceId: "dev-1", appVersion: "1.0.0");

            Assert.Equal(6, bundle.RecoveryCodes.Length);
            Assert.True(service.IsRecoveryFile(bundle.RecoveryFileBytes));

            foreach (var code in bundle.RecoveryCodes)
            {
                var restored = service.RestoreKeyfile(bundle.RecoveryFileBytes, code);
                Assert.Equal(keyfile, restored);
            }
        }

        [Fact]
        public void Restore_WrongCode_Throws()
        {
            var service = MakeService();
            var bundle = service.Create(RandomNumberGenerator.GetBytes(1024));

            Assert.Throws<UnauthorizedAccessException>(
                () => service.RestoreKeyfile(bundle.RecoveryFileBytes, "0000-0000-0000-0000"));
        }

        [Fact]
        public void Create_EmptyKeyfile_Throws()
        {
            var service = MakeService();
            Assert.Throws<ArgumentException>(() => service.Create(Array.Empty<byte>()));
        }

        [Fact]
        public void IsRecoveryFile_RejectsRandomBytes()
        {
            var service = MakeService();
            Assert.False(service.IsRecoveryFile(RandomNumberGenerator.GetBytes(512)));
        }

        [Fact]
        public void Create_DefaultCodeCount_IsTen()
        {
            var service = MakeService();
            var bundle = service.Create(RandomNumberGenerator.GetBytes(64));
            Assert.Equal(KeyfileRecoveryBundleService.DefaultRecoveryCodeCount, bundle.RecoveryCodes.Length);
        }
    }
}
