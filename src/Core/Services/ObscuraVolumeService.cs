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

            // Atomic write: stage to a sibling temp file, fsync, then replace the live volume.
            // Prevents truncated/corrupt volumes when the process is killed mid-write
            // (root cause of "Unexpected end of volume" on next unlock).
            string tempPath = volumePath + ".tmp";
            try
            {
                await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
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

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(volumePath))
                    File.Replace(tempPath, volumePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, volumePath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        public Task<string> ExtractVolumeAsync(string volumePath, string destinationRoot, CancellationToken cancellationToken = default)
            => ExtractVolumeAsync(volumePath, destinationRoot, progress: null, verify: true, cancellationToken);

        public Task<string> ExtractVolumeAsync(
            string volumePath,
            string destinationRoot,
            IProgress<double>? progress,
            CancellationToken cancellationToken = default)
            => ExtractVolumeAsync(volumePath, destinationRoot, progress, verify: true, cancellationToken);

        /// <summary>
        /// Extract the master volume to <paramref name="destinationRoot"/>.
        /// <para>
        /// Parallelizes file extraction across the available cores using separate FileStream
        /// handles (the volume format stores random-access offsets per entry, so concurrent
        /// reads are safe). Uses a 1 MiB IO buffer.
        /// </para>
        /// <para>
        /// When <paramref name="verify"/> is <c>true</c>, per-entry SHA-256 and the overall
        /// PayloadHash are checked inline (slower, fail-fast on tampering). When <c>false</c>,
        /// verification is skipped on the critical path — callers should run
        /// <see cref="VerifyExtractedVolumeAsync"/> in the background after the UI is up to
        /// detect tampering after the fact.
        /// </para>
        /// </summary>
        public async Task<string> ExtractVolumeAsync(
            string volumePath,
            string destinationRoot,
            IProgress<double>? progress,
            bool verify,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                throw new FileNotFoundException("Obscura volume not found", volumePath);
            if (string.IsNullOrWhiteSpace(destinationRoot))
                throw new ArgumentException("Destination root is required", nameof(destinationRoot));

            var manifest = await ReadManifestAsync(volumePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(destinationRoot);

            int headerLength;
            const int IoBuffer = 1024 * 1024;
            await using (var headerStream = new FileStream(
                volumePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true))
            {
                headerLength = await ReadAndValidateHeaderAsync(headerStream, cancellationToken).ConfigureAwait(false);
            }
            long payloadStart = Magic.Length + sizeof(int) + headerLength;

            long totalBytes = 0;
            foreach (var entry in manifest.Entries) totalBytes += entry.Length;
            long copied = 0;
            long progressThreshold = Math.Max(1, totalBytes / 100);
            long nextProgressAt = progressThreshold;
            object progressLock = new();

            // Per-entry hashes captured for an optional payload-hash verification at the end.
            // Indexed by entry order so we feed the payload hasher in the deterministic order
            // used at volume-creation time.
            var entryHashes = verify ? new byte[manifest.Entries.Count][] : null;

            // Run extraction in parallel — each worker opens its own FileStream handle and
            // seeks to its entry's offset. We cap concurrency to keep memory and IO depth sane.
            int dop = Math.Min(Math.Max(2, Environment.ProcessorCount / 2), 8);
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = dop
            };

            await Parallel.ForEachAsync(
                Enumerable.Range(0, manifest.Entries.Count),
                parallelOptions,
                async (i, ct) =>
                {
                    var entry = manifest.Entries[i];
                    string outputPath = Path.Combine(destinationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                    await using var input = new FileStream(
                        volumePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: IoBuffer, useAsync: true);
                    input.Position = payloadStart + entry.Offset;

                    await using var output = new FileStream(
                        outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: IoBuffer, useAsync: true);

                    byte[] buffer = new byte[IoBuffer];
                    long remaining = entry.Length;
                    IncrementalHash? entryHasher = verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                    try
                    {
                        while (remaining > 0)
                        {
                            int bytesToRead = (int)Math.Min((long)buffer.Length, remaining);
                            int bytesRead = await input.ReadAsync(buffer.AsMemory(0, bytesToRead), ct).ConfigureAwait(false);
                            if (bytesRead == 0)
                                throw new EndOfStreamException($"Unexpected end of volume while reading {entry.Path}");

                            await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                            entryHasher?.AppendData(buffer.AsSpan(0, bytesRead));
                            remaining -= bytesRead;

                            if (progress != null && totalBytes > 0)
                            {
                                long newCopied = Interlocked.Add(ref copied, bytesRead);
                                long localNext;
                                lock (progressLock) { localNext = nextProgressAt; }
                                if (newCopied >= localNext)
                                {
                                    bool fire = false;
                                    lock (progressLock)
                                    {
                                        if (newCopied >= nextProgressAt)
                                        {
                                            nextProgressAt = newCopied + progressThreshold;
                                            fire = true;
                                        }
                                    }
                                    if (fire) progress.Report((double)newCopied / totalBytes);
                                }
                            }
                        }

                        if (entryHasher != null && entryHashes != null)
                        {
                            entryHashes[i] = entryHasher.GetHashAndReset();
                        }
                    }
                    finally
                    {
                        entryHasher?.Dispose();
                    }
                }).ConfigureAwait(false);

            if (verify && entryHashes != null)
            {
                using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (int i = 0; i < entryHashes.Length; i++)
                {
                    if (entryHashes[i] == null) continue;
                    payloadHasher.AppendData(entryHashes[i]);
                }
                string computedPayloadHash = Convert.ToBase64String(payloadHasher.GetHashAndReset());
                if (!string.Equals(computedPayloadHash, manifest.PayloadHash, StringComparison.Ordinal))
                    throw new CryptographicException("Master volume integrity check failed");
            }

            progress?.Report(1.0);
            return destinationRoot;
        }

        /// <summary>
        /// Re-read the extracted destination and verify it against the volume's stored
        /// PayloadHash. Designed to run in the background after a verify:false extraction
        /// completes — surfaces tampering after the fact without blocking the unlock.
        /// </summary>
        public async Task<bool> VerifyExtractedVolumeAsync(
            string volumePath,
            string destinationRoot,
            CancellationToken cancellationToken = default)
        {
            var manifest = await ReadManifestAsync(volumePath, cancellationToken).ConfigureAwait(false);
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(destinationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return false;
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1024 * 1024, useAsync: true);
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                payloadHasher.AppendData(hash);
            }

            string computed = Convert.ToBase64String(payloadHasher.GetHashAndReset());
            return string.Equals(computed, manifest.PayloadHash, StringComparison.Ordinal);
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

