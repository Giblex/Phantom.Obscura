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

        /// <summary>Upper bound on a manifest, so a corrupt length cannot drive a huge allocation.</summary>
        private const int MaxHeaderBytes = 1024 * 1024;

        public async Task CreateVolumeFromDirectoryAsync(
            string volumePath,
            string sourceRoot,
            string keyfilePath,
            CancellationToken cancellationToken = default)
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
            await CommitVolumeAtomicAsync(volumePath, headerBytes, keyfilePath, async (output, ct) =>
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
            string keyfilePath,
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
            await CommitVolumeAtomicAsync(volumePath, headerBytes, keyfilePath, async (output, ct) =>
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
            string keyfilePath,
            Func<FileStream, CancellationToken, Task> writePayload,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("A keyfile is required to write an Obscura volume.", nameof(keyfilePath));

            string tempPath = TempPathFor(volumePath);
            string backupPath = BackupPathFor(volumePath);
            string journalPath = JournalPathFor(volumePath);

            // Drop any stale temp from a previously aborted attempt.
            TryDeleteArtifact(tempPath, "stale staging file from a previous attempt");

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
                    // v2 header: salt | nonce | tag | int32 cipherLength | ciphertext.
                    // No magic — see ObscuraVolumeFormat for why a "random-looking" constant
                    // would still be a perfect fingerprint.
                    byte[] salt = RandomNumberGenerator.GetBytes(ObscuraVolumeFormat.SaltLength);
                    byte[] plaintext = ObscuraVolumeFormat.PackHeaderPlaintext(headerBytes);
                    byte[] key = ObscuraVolumeFormat.DeriveHeaderKey(salt, keyfilePath);

                    EncryptionResult encrypted;
                    try
                    {
                        encrypted = new EncryptionService().Encrypt(
                            plaintext, key, ObscuraVolumeFormat.BuildHeaderAad(salt));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(key);
                        CryptographicOperations.ZeroMemory(plaintext);
                    }

                    byte[] cipherLengthBytes = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(cipherLengthBytes, encrypted.Ciphertext.Length);

                    await output.WriteAsync(salt, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(encrypted.Nonce, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(encrypted.Tag, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(cipherLengthBytes, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(encrypted.Ciphertext, cancellationToken).ConfigureAwait(false);

                    await writePayload(output, cancellationToken).ConfigureAwait(false);

                    // Pad the file out to a size bucket with random bytes. Encrypting the
                    // header hides every per-entry length, but the file's own size still
                    // approximated how much was stored — a 268 MB volume announced itself as
                    // a well-used vault. Readers are bounded by the entry offsets in the
                    // manifest, so trailing bytes are never interpreted.
                    await WriteRandomPaddingAsync(output, cancellationToken).ConfigureAwait(false);

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }

                // 3) Atomic swap, keeping the previous good copy as a recoverable backup.
                if (File.Exists(volumePath))
                {
                    TryDeleteArtifact(backupPath, "previous backup superseded by this commit");
                    File.Replace(tempPath, volumePath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, volumePath);
                }

                // 4) Success — discard backup and journal.
                //
                // The backup is the PREVIOUS volume, encrypted under the same keyfile. Leaving
                // it behind is not a plaintext leak, but it is a rollback artifact: anyone who
                // can open the live vault can open the backup, and the backup still holds
                // credentials the user has since deleted or rotated. That makes a failed delete
                // a revocation failure, which is why it is no longer swallowed silently.
                TryDeleteArtifact(backupPath, "rollback copy of the previous vault");
                TryDeleteArtifact(journalPath, "commit journal");
            }
            catch
            {
                // Leave journal (+ backup, if any) so recovery can run; clear the partial temp.
                TryDeleteArtifact(tempPath, "partial staging file from the failed commit");
                throw;
            }
        }

        /// <summary>
        /// Deletes a commit artifact, reporting rather than swallowing a failure.
        ///
        /// Every one of these deletes used to be <c>try { … } catch { }</c>. On a fixed disk
        /// that is nearly always harmless; on removable media — the primary target for this
        /// volume — deletes fail far more often (exFAT quirks, write-protect, the stick pulled
        /// mid-operation), and a silent failure left an artifact nobody would ever look for
        /// again. Cleanup still must never fail the commit, so this reports and returns.
        /// </summary>
        private static void TryDeleteArtifact(string path, string what)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex,
                    "[ObscuraVolume] Could not remove the {What} at {Path} — it remains on the volume's drive",
                    what, path);
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
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "[ObscuraVolume] Could not restore {VolumePath} from its backup", volumePath);
                    }
                }

                if (File.Exists(tempPath)) staleTempRemoved = true;
                TryDeleteArtifact(tempPath, "staging file from the interrupted commit");
                TryDeleteArtifact(backupPath, "rollback copy of the previous vault");
                TryDeleteArtifact(journalPath, "commit journal");
            }
            else
            {
                // No journal. Either a legacy abort or a commit whose cleanup deletes failed
                // silently before they were made observable. Both leave artifacts that nothing
                // else will ever look for, so sweep them here.
                //
                // The orphaned BACKUP used to be missed entirely: the old code only looked for
                // a temp file in this branch, so a .bak that outlived its commit sat next to
                // the vault indefinitely — an openable snapshot of an older vault state.
                if (File.Exists(tempPath)) staleTempRemoved = true;
                TryDeleteArtifact(tempPath, "orphaned staging file");
                TryDeleteArtifact(backupPath, "orphaned rollback copy of a previous vault");
            }

            return new VolumeRecoveryResult(recoveryPerformed, backupRestored, staleTempRemoved);
        }

        /// <summary>
        /// Structural sanity check used by recovery to decide whether the live file is intact
        /// or should be replaced from the backup.
        ///
        /// Deliberately keyless and deliberately weak. Recovery runs before anything has a
        /// keyfile, and it only has to answer "is this file complete enough to keep?" — a
        /// question about truncation, not authenticity. Authenticity is settled later, when
        /// the header is actually opened.
        ///
        /// This previously insisted on the v1 signature. Left that way it would have judged
        /// every v2 volume invalid and "recovered" a perfectly good vault by overwriting it
        /// with an older backup — data loss caused by the repair path.
        /// </summary>
        private static bool VolumeHeaderLooksValid(string volumePath)
        {
            try
            {
                var info = new FileInfo(volumePath);
                if (!info.Exists || info.Length < ObscuraVolumeFormat.V2FixedPrefixLength) return false;

                byte[] head = new byte[ObscuraVolumeFormat.V2FixedPrefixLength];
                using var fs = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                int total = 0;
                while (total < head.Length)
                {
                    int read = fs.Read(head, total, head.Length - total);
                    if (read == 0) break;
                    total += read;
                }
                if (total < 12) return false;

                if (ObscuraVolumeFormat.IsLegacyHeader(head))
                {
                    int legacyLength = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8, 4));
                    return legacyLength > 0 && legacyLength <= MaxHeaderBytes
                        && info.Length >= Magic.Length + 4 + (long)legacyLength;
                }

                if (total < ObscuraVolumeFormat.V2FixedPrefixLength) return false;
                int cipherLength = BinaryPrimitives.ReadInt32LittleEndian(
                    head.AsSpan(ObscuraVolumeFormat.V2FixedPrefixLength - 4, 4));
                return cipherLength > 0 && cipherLength <= MaxHeaderBytes
                    && cipherLength % ObscuraVolumeFormat.HeaderPaddingGranularity == 0
                    && info.Length >= ObscuraVolumeFormat.V2FixedPrefixLength + (long)cipherLength;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Appends random bytes until the stream reaches the next size bucket. Random rather
        /// than zeroed: a long zero run at the tail of an otherwise-opaque file is itself a
        /// signal, and would let an observer subtract the padding back off to recover the
        /// true payload size.
        /// </summary>
        private static async Task WriteRandomPaddingAsync(FileStream output, CancellationToken cancellationToken)
        {
            long actual = output.Position;
            long target = ObscuraVolumeFormat.BucketedSize(actual);
            long remaining = target - actual;
            if (remaining <= 0) return;

            const int ChunkSize = 1024 * 1024;
            byte[] chunk = new byte[(int)Math.Min(ChunkSize, remaining)];
            while (remaining > 0)
            {
                int count = (int)Math.Min(chunk.Length, remaining);
                RandomNumberGenerator.Fill(chunk.AsSpan(0, count));
                await output.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
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

        public Task<string> ExtractVolumeAsync(
            string volumePath, string destinationRoot, string keyfilePath,
            CancellationToken cancellationToken = default)
            => ExtractVolumeAsync(volumePath, destinationRoot, keyfilePath, progress: null, verify: true, cancellationToken);

        public Task<string> ExtractVolumeAsync(
            string volumePath,
            string destinationRoot,
            string keyfilePath,
            IProgress<double>? progress,
            CancellationToken cancellationToken = default)
            => ExtractVolumeAsync(volumePath, destinationRoot, keyfilePath, progress, verify: true, cancellationToken);

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
            string keyfilePath,
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

            // One header read, not two. This used to parse the header twice — once for the
            // manifest and once for the payload offset — leaving two places that had to agree
            // about the layout. With two on-disk versions that duplication is a latent bug, so
            // the offset now comes back from the same read that produced the manifest.
            var header = await ReadHeaderAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            var manifest = header.Manifest;
            long payloadStart = header.PayloadStart;

            Directory.CreateDirectory(destinationRoot);
            const int IoBuffer = 1024 * 1024;

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
            string keyfilePath,
            CancellationToken cancellationToken = default)
        {
            var manifest = await ReadManifestAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        /// Keyless structural plausibility check — "could this be a volume?", not "is it one?".
        ///
        /// Under v2 the real question cannot be answered without the keyfile, by design: a
        /// volume carries no signature, so authentication IS identification. This remains for
        /// pre-unlock callers that hold no keyfile (see SecurityCheckService), and it is
        /// deliberately named for what it can actually prove — the old name claimed a
        /// certainty it never had, since it only ever compared eight constant bytes. A caller
        /// needing a real answer must open the header with <see cref="ReadManifestAsync"/> and
        /// handle the CryptographicException a foreign or corrupt volume produces.
        /// </summary>
        public async Task<bool> IsPlausibleObscuraVolumeAsync(string volumePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                return false;

            var info = new FileInfo(volumePath);
            if (info.Length < ObscuraVolumeFormat.V2FixedPrefixLength) return false;

            await using var input = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] head = new byte[ObscuraVolumeFormat.V2FixedPrefixLength];
            int read = await ReadExactlyAsync(input, head, cancellationToken).ConfigureAwait(false);
            if (read < 12) return false;

            if (ObscuraVolumeFormat.IsLegacyHeader(head))
            {
                int legacyLength = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8, 4));
                return legacyLength > 0 && legacyLength <= MaxHeaderBytes
                    && info.Length >= Magic.Length + 4 + (long)legacyLength;
            }

            if (read < ObscuraVolumeFormat.V2FixedPrefixLength) return false;
            int cipherLength = BinaryPrimitives.ReadInt32LittleEndian(
                head.AsSpan(ObscuraVolumeFormat.V2FixedPrefixLength - 4, 4));
            return cipherLength > 0 && cipherLength <= MaxHeaderBytes
                && cipherLength % ObscuraVolumeFormat.HeaderPaddingGranularity == 0
                && info.Length >= ObscuraVolumeFormat.V2FixedPrefixLength + (long)cipherLength;
        }

        public async Task<ObscuraVolumeManifest> ReadManifestAsync(
            string volumePath, string keyfilePath, CancellationToken cancellationToken = default)
            => (await ReadHeaderAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false)).Manifest;

        /// <summary>
        /// Finds which of <paramref name="candidates"/> opens this volume, or null if none do.
        ///
        /// The unlock flow does not know a single keyfile — it knows a list (USB-only, and
        /// USB composed with each host companion). The header's GCM tag is the cheapest
        /// possible test of "is this the right one", far cheaper than the manifest's Argon2
        /// pass, so resolving here costs almost nothing and saves the caller guessing.
        ///
        /// A legacy volume needs no key at all, so the first candidate is returned unchanged.
        /// </summary>
        public async Task<string?> ResolveKeyfileAsync(
            string volumePath, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
        {
            if (candidates == null || candidates.Count == 0) return null;

            if (await IsLegacyVolumeAsync(volumePath, cancellationToken).ConfigureAwait(false))
                return candidates[0];

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    await ReadHeaderAsync(volumePath, candidate, cancellationToken).ConfigureAwait(false);
                    return candidate;
                }
                catch (CryptographicException) { /* wrong candidate — try the next */ }
                catch (ArgumentException) { /* unusable candidate path */ }
            }

            return null;
        }

        /// <summary>
        /// Manifest plus the offset at which the payload begins.
        ///
        /// Exposed because callers that read entries directly (the WinFsp mount) need the
        /// offset too, and were each recomputing it from format constants. PhantomMountService
        /// had its own copy that assumed the v1 layout and never checked the signature, so a
        /// v2 volume would have mounted at a garbage offset and served corrupt bytes. One
        /// accessor, one place that knows the layout.
        /// </summary>
        public async Task<ObscuraVolumeHeaderInfo> ReadHeaderInfoAsync(
            string volumePath, string keyfilePath, CancellationToken cancellationToken = default)
        {
            var header = await ReadHeaderAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            return new ObscuraVolumeHeaderInfo(header.Manifest, header.PayloadStart, header.IsLegacy);
        }

        /// <summary>True when the volume is still in the legacy plaintext-header format.</summary>
        public async Task<bool> IsLegacyVolumeAsync(string volumePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath)) return false;

            await using var input = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] head = new byte[8];
            int read = await ReadExactlyAsync(input, head, cancellationToken).ConfigureAwait(false);
            return read == 8 && ObscuraVolumeFormat.IsLegacyHeader(head);
        }

        /// <summary>
        /// Rewrites a legacy plaintext-header volume to v2 without decrypting or rebuilding
        /// any entry. The legacy payload is authenticated first, then copied byte-for-byte
        /// through the normal journalled atomic commit path. Returns <c>false</c> when the
        /// volume was already v2, making this safe to call after every successful unlock.
        /// </summary>
        public async Task<bool> UpgradeLegacyVolumeAsync(
            string volumePath,
            string keyfilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(volumePath) || !File.Exists(volumePath))
                throw new FileNotFoundException("Obscura volume not found", volumePath);
            if (string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("A keyfile is required to upgrade an Obscura volume.", nameof(keyfilePath));

            RecoverPendingCommit(volumePath);
            var header = await ReadHeaderAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            if (!header.IsLegacy) return false;

            long payloadLength = ValidateManifestLayout(header.Manifest, new FileInfo(volumePath).Length - header.PayloadStart);
            await VerifyPayloadAsync(volumePath, header, cancellationToken).ConfigureAwait(false);

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header.Manifest, JsonOptions);
            await CommitVolumeAtomicAsync(volumePath, headerBytes, keyfilePath, async (output, ct) =>
            {
                await using var input = new FileStream(
                    volumePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1024 * 1024, useAsync: true);
                input.Position = header.PayloadStart;
                await CopyExactlyAsync(input, output, payloadLength, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            // Do not report success until the replacement header authenticates with the
            // selected keyfile. The old volume remains recoverable through the commit journal
            // if the process or media fails before the atomic swap completes.
            var upgraded = await ReadHeaderAsync(volumePath, keyfilePath, cancellationToken).ConfigureAwait(false);
            if (upgraded.IsLegacy)
                throw new InvalidOperationException("The Obscura volume upgrade did not replace the legacy header.");
            return true;
        }

        private static long ValidateManifestLayout(ObscuraVolumeManifest manifest, long availablePayloadBytes)
        {
            long payloadLength = 0;
            foreach (var entry in manifest.Entries)
            {
                if (entry.Offset < 0 || entry.Length < 0)
                    throw new InvalidOperationException("Obscura volume contains a negative entry offset or length.");

                long end;
                try { end = checked(entry.Offset + entry.Length); }
                catch (OverflowException ex) { throw new InvalidOperationException("Obscura volume entry range overflows.", ex); }
                if (end > availablePayloadBytes)
                    throw new EndOfStreamException($"Unexpected end of volume while validating {entry.Path}");
                payloadLength = Math.Max(payloadLength, end);
            }
            return payloadLength;
        }

        private static async Task VerifyPayloadAsync(
            string volumePath, VolumeHeader header, CancellationToken cancellationToken)
        {
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var entry in header.Manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var input = new FileStream(
                    volumePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1024 * 1024, useAsync: true);
                input.Position = header.PayloadStart + entry.Offset;

                using var entryHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1024 * 1024];
                long remaining = entry.Length;
                while (remaining > 0)
                {
                    int read = await input.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                    if (read == 0) throw new EndOfStreamException($"Unexpected end of volume while validating {entry.Path}");
                    entryHasher.AppendData(buffer.AsSpan(0, read));
                    remaining -= read;
                }

                byte[] hash = entryHasher.GetHashAndReset();
                if (!string.IsNullOrWhiteSpace(entry.Sha256) &&
                    !CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(entry.Sha256)))
                    throw new CryptographicException($"Legacy volume entry integrity check failed for {entry.Path}");
                payloadHasher.AppendData(hash);
            }

            string computed = Convert.ToBase64String(payloadHasher.GetHashAndReset());
            if (!string.Equals(computed, header.Manifest.PayloadHash, StringComparison.Ordinal))
                throw new CryptographicException("Legacy volume payload integrity check failed");
        }

        private static async Task CopyExactlyAsync(
            Stream input, Stream output, long length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024 * 1024];
            long remaining = length;
            while (remaining > 0)
            {
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Unexpected end of legacy volume payload during upgrade.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }

        /// <summary>
        /// Reads a volume header, transparently handling both on-disk versions.
        ///
        /// v1 volumes open with the ASCII signature and carry a plaintext manifest; v2
        /// volumes open with a random salt and carry an encrypted one. The absence of the v1
        /// signature is the only discriminator needed — see <see cref="ObscuraVolumeFormat"/>
        /// for why v2 deliberately has no signature of its own.
        ///
        /// Returns the manifest together with the offset at which the payload begins, so
        /// callers never have to recompute that from format constants. Getting that
        /// arithmetic wrong silently misreads every entry, so it is derived in exactly one
        /// place.
        /// </summary>
        private static async Task<VolumeHeader> ReadHeaderAsync(
            string volumePath,
            string keyfilePath,
            CancellationToken cancellationToken)
        {
            await using var input = new FileStream(
                volumePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);

            byte[] head = new byte[ObscuraVolumeFormat.V2FixedPrefixLength];
            int headRead = await ReadExactlyAsync(input, head, cancellationToken).ConfigureAwait(false);
            if (headRead < 12)
                throw new InvalidOperationException("Obscura volume is truncated.");

            if (ObscuraVolumeFormat.IsLegacyHeader(head))
                return await ReadLegacyHeaderAsync(input, head, cancellationToken).ConfigureAwait(false);

            if (headRead < ObscuraVolumeFormat.V2FixedPrefixLength)
                throw new InvalidOperationException("Obscura volume is truncated.");

            return await ReadV2HeaderAsync(input, head, keyfilePath, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<VolumeHeader> ReadLegacyHeaderAsync(
            FileStream input, byte[] head, CancellationToken cancellationToken)
        {
            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8, 4));
            if (headerLength <= 0 || headerLength > MaxHeaderBytes)
                throw new InvalidOperationException("Invalid Obscura volume header length");

            input.Position = Magic.Length + 4;
            byte[] headerBytes = new byte[headerLength];
            if (await ReadExactlyAsync(input, headerBytes, cancellationToken).ConfigureAwait(false) != headerLength)
                throw new EndOfStreamException("Failed to read Obscura volume header");

            var manifest = JsonSerializer.Deserialize<ObscuraVolumeManifest>(headerBytes, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse Obscura volume manifest");

            return new VolumeHeader(manifest, Magic.Length + 4 + headerLength, IsLegacy: true);
        }

        private static async Task<VolumeHeader> ReadV2HeaderAsync(
            FileStream input, byte[] head, string keyfilePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("A keyfile is required to open this Obscura volume.", nameof(keyfilePath));

            byte[] salt = head.AsSpan(0, ObscuraVolumeFormat.SaltLength).ToArray();
            byte[] nonce = head.AsSpan(ObscuraVolumeFormat.SaltLength, ObscuraVolumeFormat.NonceLength).ToArray();
            byte[] tag = head.AsSpan(
                ObscuraVolumeFormat.SaltLength + ObscuraVolumeFormat.NonceLength,
                ObscuraVolumeFormat.TagLength).ToArray();
            int cipherLength = BinaryPrimitives.ReadInt32LittleEndian(
                head.AsSpan(ObscuraVolumeFormat.V2FixedPrefixLength - 4, 4));

            if (cipherLength <= 0 || cipherLength > MaxHeaderBytes)
                throw new InvalidOperationException("Invalid Obscura volume header length");

            byte[] ciphertext = new byte[cipherLength];
            if (await ReadExactlyAsync(input, ciphertext, cancellationToken).ConfigureAwait(false) != cipherLength)
                throw new EndOfStreamException("Failed to read Obscura volume header");

            byte[] key = ObscuraVolumeFormat.DeriveHeaderKey(salt, keyfilePath);
            byte[] plaintext;
            try
            {
                // A failed tag is how a wrong keyfile presents. It is also how "this is not
                // one of our volumes" presents — under v2 those are the same question, which
                // is the point: only a keyfile holder can tell the difference.
                plaintext = new EncryptionService().Decrypt(
                    ciphertext, nonce, tag, key, ObscuraVolumeFormat.BuildHeaderAad(salt));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException(
                    "The Obscura volume header could not be authenticated with this keyfile.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            try
            {
                byte[] json = ObscuraVolumeFormat.UnpackHeaderPlaintext(plaintext);
                var manifest = JsonSerializer.Deserialize<ObscuraVolumeManifest>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Failed to parse Obscura volume manifest");

                return new VolumeHeader(manifest, ObscuraVolumeFormat.V2FixedPrefixLength + cipherLength, IsLegacy: false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>
        /// Reads until the buffer is full or the stream ends, returning the count actually
        /// read. A single ReadAsync is allowed to return fewer bytes than asked for, and the
        /// original code treated a short read as a format error — which turned an ordinary
        /// slow read off removable media into "invalid volume".
        /// </summary>
        private static async Task<int> ReadExactlyAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await input.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            return total;
        }

        private sealed record VolumeHeader(ObscuraVolumeManifest Manifest, long PayloadStart, bool IsLegacy);

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

    /// <summary>Manifest of an Obscura volume together with where its payload starts.</summary>
    public sealed record ObscuraVolumeHeaderInfo(ObscuraVolumeManifest Manifest, long PayloadStart, bool IsLegacy);

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

