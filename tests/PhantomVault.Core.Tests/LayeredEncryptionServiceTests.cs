using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class LayeredEncryptionServiceTests
    {
        [Fact]
        public void DecryptLayered_DoesNotZeroPlaintext()
        {
            var encryptionService = new EncryptionService();
            var layered = new LayeredEncryptionService(encryptionService);

            byte[] masterKey = RandomNumberGenerator.GetBytes(32);
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] plaintext = Encoding.UTF8.GetBytes("Layered encryption regression test.");

            var encrypted = layered.EncryptLayered(
                plaintext,
                masterKey,
                LayeredEncryptionService.SecurityLevel.Standard,
                salt);

            var decrypted = layered.DecryptLayered(encrypted, masterKey, salt);

            Assert.Equal(plaintext, decrypted);
        }
    }
}
