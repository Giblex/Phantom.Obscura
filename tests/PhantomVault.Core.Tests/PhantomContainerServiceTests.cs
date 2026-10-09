#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class PhantomContainerServiceTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public async Task UnsupportedVersions_AreRejectedOnEverySurfaceWithoutOverwriting(int version)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string container = Path.Combine(directory, "legacy.pvault");
                string target = Path.Combine(directory, "target");
                byte[] bytes = new byte[12];
                Encoding.ASCII.GetBytes("PHANTOM1").CopyTo(bytes, 0);
                BitConverter.GetBytes(version).CopyTo(bytes, 8);
                File.WriteAllBytes(container, bytes);
                File.WriteAllText(target, "keep");
                using var service = new PhantomContainerService(new EncryptionService());
                await Assert.ThrowsAsync<NotSupportedException>(() => service.OpenContainerAsync(container, target, null, null));
                using var output = new MemoryStream();
                await Assert.ThrowsAsync<NotSupportedException>(() => service.OpenContainerToStreamAsync(container, output, null, null));
                await Assert.ThrowsAsync<NotSupportedException>(() => service.GetPayloadSizeAsync(container));
                await Assert.ThrowsAsync<NotSupportedException>(() => service.GetPayloadSizeAsync(container, null, null));
                Assert.Throws<NotSupportedException>(() => PhantomContainerService.ReadContainerManifest(container));
                Assert.Throws<NotSupportedException>(() => service.ReadManifestFromContainer(container, null, null));
                Assert.Throws<NotSupportedException>(() => service.UpdateManifestInContainer(container, new VaultManifest(), null, null));
                Assert.Equal("keep", File.ReadAllText(target));
                Assert.Equal(bytes, File.ReadAllBytes(container));
                Assert.Equal(0, output.Length);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Theory]
        [InlineData("KdfIterations", 17)]
        [InlineData("KdfIterations", 0)]
        [InlineData("KdfMemoryKb", -1)]
        [InlineData("KdfMemoryKb", 1_048_577)]
        [InlineData("PrivateHeaderCiphertextSize", 65_537)]
        [InlineData("PrivateHeaderCiphertextSize", 2_147_483_647)]
        [InlineData("PrivateHeaderCiphertextSize", -1)]
        public async Task UntrustedBootstrapParameters_AreRejectedBeforeDerivationOrAllocation(string property, int value)
        {
            using var harness = await ContainerHarness.CreateAsync();
            byte[] original = File.ReadAllBytes(harness.ContainerPath);
            int headerLength = BitConverter.ToInt32(original, 16);
            var header = JsonNode.Parse(original.AsSpan(20, headerLength))!;
            header[property] = value;
            byte[] modifiedHeader = JsonSerializerBytes(header);
            using (var output = File.Create(harness.ContainerPath))
            {
                output.Write(original.AsSpan(0, 16));
                output.Write(BitConverter.GetBytes(modifiedHeader.Length));
                output.Write(modifiedHeader);
                output.Write(original.AsSpan(20 + headerLength));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                harness.Service.GetPayloadSizeAsync(harness.ContainerPath, ContainerHarness.Password, harness.KeyfilePath));
            Assert.Throws<InvalidDataException>(() =>
                harness.Service.ReadManifestFromContainer(harness.ContainerPath, ContainerHarness.Password, harness.KeyfilePath));
        }

        private static byte[] JsonSerializerBytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task DecryptedBlock_IsZeroizedAfterSuccessfulFailedOrCancelledWrite(int failureMode)
        {
            using var harness = await ContainerHarness.CreateAsync();
            using var output = new CapturingStream(failureMode);
            if (failureMode == 1)
                await Assert.ThrowsAsync<IOException>(() => harness.Service.OpenContainerToStreamAsync(
                    harness.ContainerPath, output, ContainerHarness.Password, harness.KeyfilePath));
            else if (failureMode == 2)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Service.OpenContainerToStreamAsync(
                    harness.ContainerPath, output, ContainerHarness.Password, harness.KeyfilePath));
            else
                await harness.Service.OpenContainerToStreamAsync(
                    harness.ContainerPath, output, ContainerHarness.Password, harness.KeyfilePath);
            Assert.NotNull(output.Plaintext);
            Assert.True(output.SawNonzeroPlaintext);
            Assert.Equal(4096, output.Plaintext!.Length);
            Assert.All(output.Plaintext, value => Assert.Equal((byte)0, value));
        }

        private sealed class CapturingStream(int failureMode) : MemoryStream
        {
            public byte[]? Plaintext { get; private set; }
            public bool SawNonzeroPlaintext { get; private set; }
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Assert.True(MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment));
                Plaintext = segment.Array;
                foreach (byte value in buffer.Span)
                    if (value != 0) SawNonzeroPlaintext = true;
                if (failureMode == 1) throw new IOException("Simulated write failure.");
                if (failureMode == 2) return ValueTask.FromCanceled(new CancellationToken(true));
                return base.WriteAsync(buffer, cancellationToken);
            }
        }

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
                ContainerHarness.Password,
                harness.KeyfilePath);

            long footerOffset = harness.FindFooterOffset();
            harness.WriteInt32AtOffset(footerOffset + 5, 10_000);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                harness.Service.ReadManifestFromContainer(harness.ContainerPath, ContainerHarness.Password, harness.KeyfilePath));

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
                ContainerHarness.Password,
                harness.KeyfilePath);

            Assert.Equal(4096, payloadSize);
        }

        private sealed class ContainerHarness : IDisposable
        {
            private readonly string _tempDirectory;

            private ContainerHarness(string tempDirectory, string containerPath, string keyfilePath, PhantomContainerService service)
            {
                _tempDirectory = tempDirectory;
                ContainerPath = containerPath;
                KeyfilePath = keyfilePath;
                Service = service;
            }

            public string ContainerPath { get; }

            /// <summary>
            /// The keyfile is the mandatory unlock factor, so every container in
            /// these tests has one. The password is the optional extra factor.
            /// </summary>
            public string KeyfilePath { get; }

            public const string Password = "UnitTestPassword!23";

            public PhantomContainerService Service { get; }

            public static async Task<ContainerHarness> CreateAsync()
            {
                var tempDirectory = Path.Combine(Path.GetTempPath(), "phantom-container-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                // KeyfileGuard rejects password-only access, so the harness has to
                // provision a real keyfile before creating the container. These tests
                // predate the guard and used to pass keyfilePath: null.
                var keyfilePath = Path.Combine(tempDirectory, "vault.key");
                new KeyfileGeneratorService().GenerateKeyfile(keyfilePath, sizeKB: 1);

                var containerPath = Path.Combine(tempDirectory, "vault.pcv");
                var service = new PhantomContainerService(new EncryptionService());
                byte[] content = new byte[4096];
                Array.Fill(content, (byte)0x5a);
                using var payload = new MemoryStream(content, writable: false);
                await service.CreateContainerFromStreamAsync(containerPath, payload, sizeBytes: 4096, password: Password, keyfilePath: keyfilePath);
                return new ContainerHarness(tempDirectory, containerPath, keyfilePath, service);
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
                        // The generated keyfile is marked read-only, which would
                        // otherwise make the recursive delete fail and leak temp dirs.
                        foreach (var file in Directory.EnumerateFiles(_tempDirectory, "*", SearchOption.AllDirectories))
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }

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
