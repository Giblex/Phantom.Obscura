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

            // Crash-safe atomic write: journal marker + write-through temp + backup-keeping
            // replace. Prevents truncated/corrupt volumes when the process or power is lost
            // mid-write (root cause of "Unexpected end of volume" on next unlock).
            await CommitVolumeAtomicAsync(volumePath, headerBytes, async (output, ct) =>
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Rebuild a volume from in-memory/streamed sources rather than a directory on disk.
        /// Used by the writable virtual-drive mount to repack edits without ever staging
        /// decrypted plaintext to the filesystem. Each source provides a relative path and a
        /// factory that opens a fresh readable stream for that entry's bytes (the factory may
        /// be invoked twice — once to hash, once to copy). The write is atomic (temp + replace).
        /// </summary>
        public async Task CreateVolumeFromSourcesAsync(
            string volumePath,
            IReadOnlyList<ObscuraVolumeSource> sources,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath))
                throw new ArgumentException("Volume path is required", nameof(volumePath));
            if (sources is null)
                throw new ArgumentNullException(nameof(sources));

            var ordered = sources
                .OrderBy(s => s.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var entries = new List<ObscuraVolumeEntry>(ordered.Length);
            long currentOffset = 0;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var source in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long length;
                byte[] entryHash;
                await using (var stream = source.OpenRead())
                {
                    if (stream.CanSeek)
                    {
                        entryHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                        length = stream.Length;
                    }
                    else
                    {
                        using var counting = new CountingHashStream();
                        await stream.CopyToAsync(counting, 81920, cancellationToken).ConfigureAwait(false);
                        entryHash = counting.GetHash();
                        length = counting.Count;
                    }
                }

                entries.Add(new ObscuraVolumeEntry
                {
                    Path = source.Path.Replace('\\', '/'),
                    Offset = currentOffset,
                    Length = length,
                    Sha256 = Convert.ToBase64String(entryHash)
                });
                currentOffset += length;
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

            // Crash-safe atomic write (see CommitVolumeAtomicAsync): journal + write-through
            // temp + backup-keeping replace, so an interrupted commit can never corrupt the
            // live container — it is recovered on the next open.
            await CommitVolumeAtomicAsync(volumePath, headerBytes, async (output, ct) =>
            {
                foreach (var source in ordered)
                {
                    ct.ThrowIfCancellationRequested();
                    await using var input = source.OpenRead();
                    await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        // ---- Crash-safe commit + recovery ----------------------------------------------

        internal static string TempPathFor(string volumePath) => volumePath + ".tmp";
        internal static string BackupPathFor(string volumePath) => volumePath + ".bak";
        internal static string JournalPathFor(string volumePath) => volumePath + ".commit-journal";

        /// <summary>
        /// Durable, crash-safe replacement of <paramref name="volumePath"/>. A journal marker
        /// is flushed to disk first; the new volume is staged to a write-through temp file and
        /// forced to the platter; the live file is swapped in via <c>File.Replace</c>
        /// keeping the previous good copy as a backup. On success the backup and journal are
        /// removed. If the process or power dies at any point, <see cref="RecoverPendingCommit"/>
        /// repairs the volume on the next open. The header (magic + length-prefixed manifest)
        /// is written first, then <paramref name="writePayload"/> appends the entry bytes.
        /// </summary>
        private static async Task CommitVolumeAtomicAsync(
            string volumePath,
            byte[] headerBytes,
            Func<FileStream, CancellationToken, Task> writePayload,
            CancellationToken cancellationToken)
        {
            string tempPath = TempPathFor(volumePath);
            string backupPath = BackupPathFor(volumePath);
            string journalPath = JournalPathFor(volumePath);

            // Drop any stale temp from a previously aborted attempt.
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            // 1) Journal the intent and flush it before touching the live file.
            var journal = new CommitJournal
            {
                TempFile = Path.GetFileName(tempPath),
                BackupFile = Path.GetFileName(backupPath),
                StartedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await WriteAllBytesDurableAsync(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions), cancellationToken)
                .ConfigureAwait(false);

            try
            {
                // 2) Stage the full new volume, forcing bytes through the OS cache to disk.
                await using (var output = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
                    byte[] headerLengthBytes = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(headerLengthBytes, headerBytes.Length);
                    await output.WriteAsync(headerLengthBytes, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

                    await writePayload(output, cancellationToken).ConfigureAwait(false);

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }

                // 3) Atomic swap, keeping the previous good copy as a recoverable backup.
                if (File.Exists(volumePath))
                {
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                    File.Replace(tempPath, volumePath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, volumePath);
                }

                // 4) Success — discard backup and journal.
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                try { if (File.Exists(journalPath)) File.Delete(journalPath); } catch { }
            }
            catch
            {
                // Leave journal (+ backup, if any) so recovery can run; clear the partial temp.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Repairs a volume after an interrupted commit. Safe and cheap to call before every
        /// open. If a commit journal is present the live file is validated; if it is unreadable
        /// the backup copy is restored. Stale temp/backup/journal artifacts are always swept.
        /// Returns what (if anything) was recovered so callers can surface it to the user.
        /// </summary>
        public static VolumeRecoveryResult RecoverPendingCommit(string volumePath)
        {
            if (string.IsNullOrWhiteSpace(volumePath))
                return new VolumeRecoveryResult(false, false, false);

            string tempPath = TempPathFor(volumePath);
            string backupPath = BackupPathFor(volumePath);
            string journalPath = JournalPathFor(volumePath);

            bool recoveryPerformed = false;
            bool backupRestored = false;
            bool staleTempRemoved = false;

            if (File.Exists(journalPath))
            {
                recoveryPerformed = true;

                // A commit was interrupted. Either the old (pre-Replace) or new (post-Replace)
                // copy is live; both are internally complete. Only a rare mid-Replace failure
                // can leave the live file unreadable — repair that from the backup.
                if (!VolumeHeaderLooksValid(volumePath) && File.Exists(backupPath) && VolumeHeaderLooksValid(backupPath))
                {
                    try
                    {
                        if (File.Exists(volumePath)) File.Delete(volumePath);
                        File.Move(backupPath, volumePath);
                        backupRestored = true;
                    }
                    catch { }
                }

                try { if (File.Exists(tempPath)) { File.Delete(tempPath); staleTempRemoved = true; } } catch { }
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                try { File.Delete(journalPath); } catch { }
            }
            else if (File.Exists(tempPath))
            {
                // Legacy abort with no journal: sweep the orphaned temp.
                try { File.Delete(tempPath); staleTempRemoved = true; } catch { }
            }

            return new VolumeRecoveryResult(recoveryPerformed, backupRestored, staleTempRemoved);
        }

        private static bool VolumeHeaderLooksValid(string volumePath)
        {
            try
            {
                var info = new FileInfo(volumePath);
                if (!info.Exists || info.Length < 12) return false;

                Span<byte> head = stackalloc byte[12];
                using var fs = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Read(head) != 12) return false;
                if (!head.Slice(0, 8).SequenceEqual(Magic)) return false;

                int headerLen = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(8, 4));
                if (headerLen <= 0) return false;
                return info.Length >= 12 + headerLen;
            }
            catch
            {
                return false;
            }
        }

        private static async Task WriteAllBytesDurableAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            await using var fs = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            await fs.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            fs.Flush(flushToDisk: true);
        }

        private sealed record CommitJournal
        {
            public int Version { get; init; } = 1;
            public string TempFile { get; init; } = string.Empty;
            public string BackupFile { get; init; } = string.Empty;
            public long StartedUnix { get; init; }
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

            // Repair any interrupted prior commit before reading the container.
            RecoverPendingCommit(volumePath);

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

    /// <summary>
    /// Outcome of <see cref="ObscuraVolumeService.RecoverPendingCommit"/>. When
    /// <see cref="RecoveryPerformed"/> is true an earlier write was interrupted; the other
    /// flags say whether the live volume had to be restored from its backup and whether a
    /// stale temp file was swept.
    /// </summary>
    public readonly record struct VolumeRecoveryResult(
        bool RecoveryPerformed,
        bool BackupRestored,
        bool StaleTempRemoved);

    /// <summary>
    /// A single entry source for <see cref="ObscuraVolumeService.CreateVolumeFromSourcesAsync"/>.
    /// <see cref="OpenRead"/> must return a fresh readable stream each time it is called.
    /// </summary>
    public sealed class ObscuraVolumeSource
    {
        public ObscuraVolumeSource(string path, Func<Stream> openRead)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            OpenRead = openRead ?? throw new ArgumentNullException(nameof(openRead));
        }

        public string Path { get; }
        public Func<Stream> OpenRead { get; }
    }

    /// <summary>
    /// Write-only sink that SHA-256 hashes and counts bytes for non-seekable sources.
    /// </summary>
    internal sealed class CountingHashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long Count { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _hash.AppendData(buffer, offset, count);
            Count += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hash.AppendData(buffer);
            Count += buffer.Length;
        }

        public byte[] GetHash() => _hash.GetHashAndReset();

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }
    }
}

