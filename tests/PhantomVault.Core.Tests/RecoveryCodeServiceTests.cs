using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class RecoveryCodeServiceTests
    {
        [Fact]
        public void PrepareRecoveryCodesForStorage_ThenValidate_SucceedsOnce()
        {
            var service = new RecoveryCodeService();
            var codes = service.GenerateRecoveryCodes(3);
            var manifest = new VaultManifest
            {
                RecoveryCodes = service.PrepareRecoveryCodesForStorage(codes)
            };

            var valid = service.ValidateAndUseRecoveryCode(manifest, codes[0]);
            var reused = service.ValidateAndUseRecoveryCode(manifest, codes[0]);

            Assert.True(valid);
            Assert.False(reused);
            Assert.True(manifest.RecoveryCodes![0].Used);
            Assert.NotNull(manifest.RecoveryCodes[0].UsedAtUtc);
        }

        [Fact]
        public void ValidateAndUseRecoveryCode_WrongCode_FailsWithoutMutatingStoredCodes()
        {
            var service = new RecoveryCodeService();
            var codes = service.GenerateRecoveryCodes(2);
            var manifest = new VaultManifest
            {
                RecoveryCodes = service.PrepareRecoveryCodesForStorage(codes)
            };

            var valid = service.ValidateAndUseRecoveryCode(manifest, "WRNG-WRNG-WRNG-WRNG");

            Assert.False(valid);
            Assert.All(manifest.RecoveryCodes!, recoveryCode => Assert.False(recoveryCode.Used));
        }
    }
}
