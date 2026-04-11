using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class PhantomContainerServiceTests
    {
        [Fact]
        public async Task GetPayloadSizeAsync_InvalidV4HeaderSize_Throws()
        {
            using var harness = await ContainerHarness.CreateAsync();
            harness.WriteInt32AtOffset(12, 99);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.Service.GetPayloadSizeAsync(harness.ContainerPath));

            Assert.Contains("header size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadContainerManifest_InvalidV4HeaderSize_ReturnsNull()
        {
            using var harness = await ContainerHarness.CreateAsync();
            harness.WriteInt32AtOffset(12, 99);

            var manifest = PhantomContainerService.ReadContainerManifest(harness.ContainerPath);

            Assert.Null(manifest);
        }

        [Fact]
        public async Task GetPayloadSizeAsync_InvalidManifestSize_Throws()
        {
            using var harness = await ContainerHarness.CreateAsync();
            harness.WriteInt32AtOffset(16, 70_000);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.Service.GetPayloadSizeAsync(harness.ContainerPath));

            Assert.Contains("manifest size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadManifestFromContainer_TruncatedV4Footer_Throws()
        {
            using var harness = await ContainerHarness.CreateAsync();
            harness.Service.UpdateManifestInContainer(
                harness.ContainerPath,
                new VaultManifest { VaultName = "Test Vault" },
                "UnitTestPassword!23",
                null);

            long footerOffset = harness.FindFooterOffset();
            harness.WriteInt32AtOffset(footerOffset + 5, 10_000);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                harness.Service.ReadManifestFromContainer(harness.ContainerPath, "UnitTestPassword!23", null));

            Assert.Contains("footer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadContainerManifest_V4Defaults_UseHardenedArgon2Profile()
        {
            using var harness = await ContainerHarness.CreateAsync();

            var manifest = PhantomContainerService.ReadContainerManifest(harness.ContainerPath);

            Assert.Null(manifest);
        }

        [Fact]
        public async Task GetPayloadSizeAsync_NewV4Header_RequiresAuthentication()
        {
            using var harness = await ContainerHarness.CreateAsync();

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                harness.Service.GetPayloadSizeAsync(harness.ContainerPath));

            Assert.Contains("authentication", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetPayloadSizeAsync_WithAuthentication_ReadsEncryptedPrivateHeader()
        {
            using var harness = await ContainerHarness.CreateAsync();

            long payloadSize = await harness.Service.GetPayloadSizeAsync(
                harness.ContainerPath,
                "UnitTestPassword!23",
                null);

            Assert.Equal(4096, payloadSize);
        }

        private sealed class ContainerHarness : IDisposable
        {
            private readonly string _tempDirectory;

            private ContainerHarness(string tempDirectory, string containerPath, PhantomContainerService service)
            {
                _tempDirectory = tempDirectory;
                ContainerPath = containerPath;
                Service = service;
            }

            public string ContainerPath { get; }

            public PhantomContainerService Service { get; }

            public static async Task<ContainerHarness> CreateAsync()
            {
                var tempDirectory = Path.Combine(Path.GetTempPath(), "phantom-container-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                var containerPath = Path.Combine(tempDirectory, "vault.pcv");
                var service = new PhantomContainerService(new EncryptionService());
                await service.CreateContainerAsync(containerPath, sizeBytes: 4096, password: "UnitTestPassword!23", keyfilePath: null);
                return new ContainerHarness(tempDirectory, containerPath, service);
            }

            public void WriteInt32AtOffset(long offset, int value)
            {
                using var stream = new FileStream(ContainerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                stream.Seek(offset, SeekOrigin.Begin);
                stream.Write(BitConverter.GetBytes(value));
                stream.Flush(true);
            }

            public long FindFooterOffset()
            {
                var marker = System.Text.Encoding.ASCII.GetBytes("MNFST");
                using var stream = new FileStream(ContainerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var data = new byte[stream.Length];
                stream.ReadExactly(data);

                for (var i = 0; i <= data.Length - marker.Length; i++)
                {
                    var matches = true;
                    for (var j = 0; j < marker.Length; j++)
                    {
                        if (data[i + j] != marker[j])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return i;
                    }
                }

                throw new InvalidOperationException("Footer marker not found");
            }

            public void Dispose()
            {
                Service.Dispose();
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup for temp test files.
                }
            }
        }
    }
}
