using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Debug tests to isolate ZK encryption/decryption issues
    /// </summary>
    public class ZkVaultServiceDebugTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly EncryptionService _encryptionService;
        private readonly IZkVaultService _zkVaultService;

        public ZkVaultServiceDebugTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"PhantomVault_Debug_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDirectory);
            
            _encryptionService = new EncryptionService();
            _zkVaultService = new ZkVaultService();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public async Task DebugTest_SimpleEncryptDecrypt()
        {
            // Arrange
            string testData = "Hello, World!";
            byte[] testDataBytes = Encoding.UTF8.GetBytes(testData);
            byte[] masterKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            string encryptedPath = Path.Combine(_testDirectory, "test.zkf");

            // Unlock with master key
            bool unlocked = await _zkVaultService.UnlockWithHybridKeyAsync(masterKey);
            Assert.True(unlocked);

            // Encrypt
            using (var plaintextStream = new MemoryStream(testDataBytes))
            {
                long initialPosition = plaintextStream.Position;
                long length = plaintextStream.Length;
                Console.WriteLine($"Stream before encryption: Position={initialPosition}, Length={length}");
                
                await _zkVaultService.EncryptStreamAsync(plaintextStream, encryptedPath);
                
                Console.WriteLine($"Stream after encryption: Position={plaintextStream.Position}");
            }

            // Check encrypted file exists and has content
            Assert.True(File.Exists(encryptedPath));
            long encryptedSize = new FileInfo(encryptedPath).Length;
            Console.WriteLine($"Encrypted file size: {encryptedSize} bytes");
            Assert.True(encryptedSize > 0, "Encrypted file should have content");

            // Decrypt
            using (var decryptedStream = await _zkVaultService.OpenFileStreamForViewingAsync(encryptedPath))
            {
                Console.WriteLine($"Decrypted stream: Position={decryptedStream.Position}, Length={decryptedStream.Length}");
                
                using (var reader = new StreamReader(decryptedStream))
                {
                    string decryptedData = await reader.ReadToEndAsync();
                    Console.WriteLine($"Decrypted data: '{decryptedData}'");
                    Assert.Equal(testData, decryptedData);
                }
            }
        }
    }
}
