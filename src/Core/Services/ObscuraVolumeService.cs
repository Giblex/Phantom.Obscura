using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Packs encrypted inner artifacts into a single master Obscura volume.
    /// The outer volume hides the container layout and provides integrity checks,
    /// but does not add another encryption layer on top of already encrypted artifacts.
    /// </summary>
    public sealed class ObscuraVolumeService
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("OBSCUR01");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public async Task CreateVolumeFromDirectoryAsync(string volumePath, string sourceRoot, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath))
                throw new ArgumentException("Volume path is required", nameof(volumePath));
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException($"Source root not found: {sourceRoot}");

            var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var entries = new List<ObscuraVolumeEntry>(files.Length);
            long currentOffset = 0;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                var fileInfo = new FileInfo(file);
                byte[] entryHash;
                await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    entryHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                entries.Add(new ObscuraVolumeEntry
                {
                    Path = relativePath,
                    Offset = currentOffset,
                    Length = fileInfo.Length,
                    Sha256 = Convert.ToBase64String(entryHash)
                });

                currentOffset += fileInfo.Length;
                payloadHasher.AppendData(entryHash);
            }

            var manifest = new ObscuraVolumeManifest
            {
                Version = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                Entries = entries,
                PayloadHash = Convert.ToBase64String(payloadHasher.GetHashAndReset())
            };

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var volumeDir = Path.GetDirectoryName(volumePath);
            if (!string.IsNullOrEmpty(volumeDir))
                Directory.CreateDirectory(volumeDir);

            await using var output = new FileStream(volumePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);

            byte[] headerLengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(headerLengthBytes, headerBytes.Length);
            await output.WriteAsync(headerLengthBytes, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

            foreach (var file in files)
            {
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<string> ExtractVolumeAsync(string volumePath, string destinationRoot, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                throw new FileNotFoundException("Obscura volume not found", volumePath);
            if (string.IsNullOrWhiteSpace(destinationRoot))
                throw new ArgumentException("Destination root is required", nameof(destinationRoot));

            var manifest = await ReadManifestAsync(volumePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(destinationRoot);

            await using var input = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int headerLength = await ReadAndValidateHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            long payloadStart = Magic.Length + sizeof(int) + headerLength;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string outputPath = Path.Combine(destinationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                input.Position = payloadStart + entry.Offset;
                await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

                long remaining = entry.Length;
                byte[] buffer = new byte[81920];
                using var entryHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (remaining > 0)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, remaining);
                    int bytesRead = await input.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        throw new EndOfStreamException($"Unexpected end of volume while reading {entry.Path}");

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    entryHasher.AppendData(buffer.AsSpan(0, bytesRead));
                    remaining -= bytesRead;
                }

                byte[] computedEntryHash = entryHasher.GetHashAndReset();
                payloadHasher.AppendData(computedEntryHash);
                string computedEntryHashBase64 = Convert.ToBase64String(computedEntryHash);
                if (!string.Equals(computedEntryHashBase64, entry.Sha256, StringComparison.Ordinal))
                    throw new CryptographicException($"Entry integrity check failed for {entry.Path}");
            }

            string computedPayloadHash = Convert.ToBase64String(payloadHasher.GetHashAndReset());
            if (!string.Equals(computedPayloadHash, manifest.PayloadHash, StringComparison.Ordinal))
                throw new CryptographicException("Master volume integrity check failed");

            return destinationRoot;
        }

        public async Task<bool> IsObscuraVolumeAsync(string volumePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                return false;

            await using var input = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var magicBuffer = new byte[Magic.Length];
            int read = await input.ReadAsync(magicBuffer.AsMemory(0, magicBuffer.Length), cancellationToken).ConfigureAwait(false);
            return read == Magic.Length && magicBuffer.SequenceEqual(Magic);
        }

        public async Task<ObscuraVolumeManifest> ReadManifestAsync(string volumePath, CancellationToken cancellationToken = default)
        {
            await using var input = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int headerLength = await ReadAndValidateHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            byte[] headerBytes = new byte[headerLength];
            int read = await input.ReadAsync(headerBytes.AsMemory(0, headerLength), cancellationToken).ConfigureAwait(false);
            if (read != headerLength)
                throw new EndOfStreamException("Failed to read Obscura volume header");

            return JsonSerializer.Deserialize<ObscuraVolumeManifest>(headerBytes, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse Obscura volume manifest");
        }

        private static async Task<int> ReadAndValidateHeaderAsync(Stream input, CancellationToken cancellationToken)
        {
            byte[] magicBuffer = new byte[Magic.Length];
            int magicRead = await input.ReadAsync(magicBuffer.AsMemory(0, magicBuffer.Length), cancellationToken).ConfigureAwait(false);
            if (magicRead != Magic.Length || !magicBuffer.SequenceEqual(Magic))
                throw new InvalidOperationException("Invalid Obscura volume format");

            byte[] headerLengthBytes = new byte[4];
            int headerLengthRead = await input.ReadAsync(headerLengthBytes.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (headerLengthRead != 4)
                throw new EndOfStreamException("Failed to read Obscura volume header length");

            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(headerLengthBytes);
            if (headerLength <= 0 || headerLength > 1024 * 1024)
                throw new InvalidOperationException("Invalid Obscura volume header length");

            return headerLength;
        }
    }

    public sealed class ObscuraVolumeManifest
    {
        public int Version { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string PayloadHash { get; set; } = string.Empty;
        public List<ObscuraVolumeEntry> Entries { get; set; } = new();
    }

    public sealed class ObscuraVolumeEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
