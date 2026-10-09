using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Container;
using GiblexVault.Security.ZK.Models;
using Xunit;

namespace PhantomVault.Core.Tests;

public sealed class GvContainerBoundsTests
{
    [Theory]
    [InlineData(65_537u)]
    [InlineData(0x7fffffffu)]
    [InlineData(0xffffffffu)]
    [InlineData(32u)]
    public void HeaderLengths_AreRejectedAndFileHandleIsReleased(uint length)
    {
        string path = Path.GetTempFileName();
        byte[] master = RandomNumberGenerator.GetBytes(32);
        try
        {
            using (var output = File.Create(path))
            {
                output.Write(new byte[] { 0x47, 0x56, 0x2D, 0x43, 0x5A, 0x4B, 0x01, 0x00 });
                output.Write(BitConverter.GetBytes(length));
            }
            Assert.Throws<InvalidDataException>(() =>
                GvContainerZk.List(path, master, new EngineOptions(EncryptionProfile.Basic)));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OversizedToc_DoesNotModifyExistingContainer()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] master = RandomNumberGenerator.GetBytes(32);
        try
        {
            string path = Path.Combine(directory, "container");
            string input = Path.Combine(directory, "input");
            byte[] content = Encoding.UTF8.GetBytes("existing payload");
            File.WriteAllBytes(input, content);
            var options = new EngineOptions(EncryptionProfile.Basic);
            await GvContainerZk.CreateAsync(path, master, options);
            GvContainerZk.AddFile(path, master, options, "existing", input);
            byte[] original = File.ReadAllBytes(path);

            Assert.Throws<InvalidDataException>(() => GvContainerZk.AddFile(
                path, master, options, new string('x', 16 * 1024 * 1024), input));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(content, GvContainerZk.Extract(path, master, options, "existing"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TocLength_IsBounded_AndCurrentContainerStillRoundTrips()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] master = RandomNumberGenerator.GetBytes(32);
        try
        {
            string path = Path.Combine(directory, "container");
            string input = Path.Combine(directory, "input");
            byte[] content = Encoding.UTF8.GetBytes("current container payload");
            File.WriteAllBytes(input, content);
            var options = new EngineOptions(EncryptionProfile.Basic);
            await GvContainerZk.CreateAsync(path, master, options);
            long entryOffset = new FileInfo(path).Length;
            GvContainerZk.AddFile(path, master, options, "entry", input);
            Assert.Equal(content, GvContainerZk.Extract(path, master, options, "entry"));
            byte[] file = File.ReadAllBytes(path);
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
            {
                stream.Position = entryOffset + 12;
                stream.Write(BitConverter.GetBytes(256 * 1024 * 1024 + 1));
            }
            Assert.Throws<InvalidDataException>(() => GvContainerZk.Extract(path, master, options, "entry"));
            File.WriteAllBytes(path, file);
            long tocStart = BitConverter.ToInt64(file, file.Length - 8);
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
            {
                stream.Position = tocStart + 12;
                stream.Write(BitConverter.GetBytes(16 * 1024 * 1024 + 1));
            }
            Assert.Throws<InvalidDataException>(() => GvContainerZk.List(path, master, options));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
            Directory.Delete(directory, true);
        }
    }
}
