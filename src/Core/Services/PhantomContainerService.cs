using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Custom encrypted container format for PhantomVault.
    /// Provides VeraCrypt-like security without external dependencies.
    /// 
    /// v2 Format: [Header][Version][Salt][IterationCount][Size][Encrypted Metadata][Encrypted Data Blocks]
    /// v3 Format: [Header][Version][Salt][IterationCount][Size][ManifestOffset][Encrypted Metadata][Encrypted Data Blocks][Manifest Footer]
    /// v4 Format: [Static Header 16B][Public Bootstrap Header][Encrypted Private Header][Encrypted Data Blocks][VaultManifest Footer]
    ///
    /// v4 introduces:
    /// - Static header with explicit header size field
    /// - Minimal public bootstrap material for KDF bootstrapping only
    /// - Authenticated encrypted private header containing payload metadata,
    ///   payload hash, and optional attestation fields
    /// - Legacy cleartext-v4 headers remain readable for backwards compatibility
    /// - VaultManifest remains encrypted in the footer (same as v3)
    /// </summary>
    public sealed class PhantomContainerService : IDisposable
    {
        private sealed class V4ManifestSection
        {
            public required int ManifestSize { get; init; }
            public required byte[] ManifestJsonBytes { get; init; }
            public required byte[] StoredHmac { get; init; }
            public required ContainerManifest ContainerManifest { get; init; }
            public required long PayloadStartOffset { get; init; }
            public bool UsesEncryptedPrivateHeader { get; init; }
        }

        private sealed class V4BootstrapSection
        {
            public required int HeaderSize { get; init; }
            public required byte[] HeaderJsonBytes { get; init; }
            public required V4PublicBootstrapHeader BootstrapHeader { get; init; }
            public required byte[] Nonce { get; init; }
            public required byte[] Tag { get; init; }
            public required byte[] Ciphertext { get; init; }
            public required long PayloadStartOffset { get; init; }
        }

        private sealed class V4PublicBootstrapHeader
        {
            public string HeaderMode { get; init; } = "private";
            public string KdfAlgorithm { get; init; } = "Argon2id";
            public int KdfIterations { get; init; }
            public int KdfMemoryKb { get; init; }
            public string Salt { get; init; } = string.Empty;
            public int PrivateHeaderCiphertextSize { get; init; }
        }

        private sealed class AuthenticatedV4ContainerContext : IDisposable
        {
            public required V4ManifestSection ManifestSection { get; init; }
            public required byte[] ContainerKey { get; init; }
            public required byte[] HmacKey { get; init; }

            public long FooterOffset => ComputeVaultManifestOffset(ManifestSection.PayloadStartOffset, ManifestSection.ContainerManifest.PayloadSize);

            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(ContainerKey);
                CryptographicOperations.ZeroMemory(HmacKey);
            }
        }

        private sealed class VaultManifestFooterSection
        {
            public required long FooterOffset { get; init; }
            public required int CiphertextSize { get; init; }
            public required byte[] Nonce { get; init; }
            public required byte[] Tag { get; init; }
            public required byte[] Ciphertext { get; init; }
        }

        // ── Shared constants ──────────────────────────────────────────
        private const string MagicHeader = "PHANTOM1";
        private const int HeaderMagicSize = 8;
        private const int VersionFieldSize = 4;
        private const int SaltSize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int BlockSize = 1024 * 1024; // 1MB blocks
        private const int DefaultIterations = 6; // Align with EncryptionService default Argon2id profile
        private const int DefaultMemoryKb = 256 * 1024; // Align with EncryptionService default Argon2id profile
        private const int SecureWipePassCount = 3;
        private const string ManifestMarker = "MNFST";
        private const int ManifestMarkerSize = 5;
        private const int CurrentVersion = 4;
        private const int MaxSupportedVersion = 4;

        // ── v3 legacy constants ───────────────────────────────────────
        private const int V3_HeaderSize = 8;
        private const int V3_IterationCountSize = 4;
        private const int V3_ContainerSizeSize = 8;
        private const int V3_ManifestOffsetSize = 8;

        // ── v4 constants ──────────────────────────────────────────────
        private const int V4_HeaderSizeFieldSize = 4;
        private const int V4_StaticHeaderTotalSize = 16; // magic(8) + version(4) + headerSize(4)
        private const int V4_ManifestSizeFieldSize = 4;
        private const int V4_HmacSize = 32; // HMAC-SHA256
        private static readonly byte[] V4_HmacDomainInfo = Encoding.UTF8.GetBytes("PhantomContainer.ManifestHMAC.v4");
        private const string V4_PrivateHeaderMode = "private";

        private static readonly JsonSerializerOptions ContainerManifestJsonOptions = new()
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly EncryptionService _encryptionService;
        private bool _disposed;

        public PhantomContainerService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        /// <summary>
        /// Creates an encrypted container file using the v4 format.
        /// Layout: [Static Header 16B][Public Bootstrap Header][Encrypted Private Header][Encrypted Data Blocks][VaultManifest Footer]
        /// The payload hash in the private header is backpatched after all blocks are written.
        /// </summary>
        public async Task CreateContainerAsync(
            string containerPath,
            long sizeBytes,
            string? password,
            string? keyfilePath,
            VaultManifest? manifest = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await CreateContainerFromStreamAsync(
                containerPath,
                payloadStream: null,
                sizeBytes,
                password,
                keyfilePath,
                manifest,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates or rewrites an encrypted container using payload bytes from the provided stream.
        /// Any remaining unused payload capacity is filled with zeros before encryption.
        /// </summary>
        public async Task CreateContainerFromStreamAsync(
            string containerPath,
            Stream? payloadStream,
            long sizeBytes,
            string? password,
            string? keyfilePath,
            VaultManifest? manifest = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath))
                throw new ArgumentException("Container path is required", nameof(containerPath));
            if (sizeBytes <= 0)
                throw new ArgumentException("Container size must be positive", nameof(sizeBytes));

            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            byte[] containerKey = await DeriveContainerKeyAsync(password, keyfilePath, salt, DefaultIterations, DefaultMemoryKb);
            byte[] hmacKey = DeriveHmacKey(containerKey);

            try
            {
                progress?.Report(0.0);

                var dir = Path.GetDirectoryName(containerPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // ReadWrite needed for backpatching PayloadHash + HMAC
                using var fs = new FileStream(containerPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

                // ── Static Header (16 bytes) ────────────────────────────
                await fs.WriteAsync(Encoding.ASCII.GetBytes(MagicHeader), cancellationToken);
                await fs.WriteAsync(BitConverter.GetBytes(CurrentVersion), cancellationToken);       // 4
                await fs.WriteAsync(BitConverter.GetBytes(V4_StaticHeaderTotalSize), cancellationToken); // 16

                // ── Public bootstrap + encrypted private header ─────────
                var containerManifest = new ContainerManifest
                {
                    ContainerId = Guid.NewGuid(),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    EncryptionAlgorithm = "AES-256-GCM",
                    KdfAlgorithm = "Argon2id",
                    KdfIterations = DefaultIterations,
                    KdfMemoryKb = DefaultMemoryKb,
                    Salt = Convert.ToBase64String(salt),
                    PayloadSize = sizeBytes,
                    PayloadHash = Convert.ToBase64String(new byte[32]), // placeholder — same length as real hash
                };

                var bootstrapHeader = new V4PublicBootstrapHeader
                {
                    KdfAlgorithm = containerManifest.KdfAlgorithm,
                    KdfIterations = containerManifest.KdfIterations,
                    KdfMemoryKb = containerManifest.KdfMemoryKb,
                    Salt = containerManifest.Salt,
                    PrivateHeaderCiphertextSize = 0, // backpatched below
                };

                byte[] privateManifestJson = JsonSerializer.SerializeToUtf8Bytes(containerManifest, ContainerManifestJsonOptions);
                byte[] bootstrapJson = JsonSerializer.SerializeToUtf8Bytes(bootstrapHeader, ContainerManifestJsonOptions);
                var privateManifestEnvelope = _encryptionService.Encrypt(privateManifestJson, containerKey, bootstrapJson);
                bootstrapHeader = new V4PublicBootstrapHeader
                {
                    KdfAlgorithm = containerManifest.KdfAlgorithm,
                    KdfIterations = containerManifest.KdfIterations,
                    KdfMemoryKb = containerManifest.KdfMemoryKb,
                    Salt = containerManifest.Salt,
                    PrivateHeaderCiphertextSize = privateManifestEnvelope.Ciphertext.Length,
                };
                bootstrapJson = JsonSerializer.SerializeToUtf8Bytes(bootstrapHeader, ContainerManifestJsonOptions);
                privateManifestEnvelope = _encryptionService.Encrypt(privateManifestJson, containerKey, bootstrapJson);

                long manifestSizePos = fs.Position;
                await fs.WriteAsync(BitConverter.GetBytes(bootstrapJson.Length), cancellationToken);

                long manifestJsonPos = fs.Position;
                await fs.WriteAsync(bootstrapJson, cancellationToken);

                long privateHeaderNoncePos = fs.Position;
                await fs.WriteAsync(privateManifestEnvelope.Nonce, cancellationToken);
                await fs.WriteAsync(privateManifestEnvelope.Tag, cancellationToken);
                long privateHeaderCiphertextPos = fs.Position;
                await fs.WriteAsync(privateManifestEnvelope.Ciphertext, cancellationToken);

                progress?.Report(0.1);

                // ── Encrypted Data Blocks ───────────────────────────────
                using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long totalBlocks = (sizeBytes + BlockSize - 1) / BlockSize;
                byte[] payloadBlock = new byte[Math.Min(BlockSize, sizeBytes)];

                for (long blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int currentBlockSize = (int)Math.Min(BlockSize, sizeBytes - (blockIndex * BlockSize));
                    if (currentBlockSize < payloadBlock.Length)
                        payloadBlock = new byte[currentBlockSize];

                    Array.Clear(payloadBlock, 0, currentBlockSize);
                    if (payloadStream != null)
                    {
                        int totalRead = 0;
                        while (totalRead < currentBlockSize)
                        {
                            int read = await payloadStream.ReadAsync(payloadBlock.AsMemory(totalRead, currentBlockSize - totalRead), cancellationToken).ConfigureAwait(false);
                            if (read == 0)
                            {
                                break;
                            }

                            totalRead += read;
                        }
                    }

                    var encryptedBlock = _encryptionService.Encrypt(
                        payloadBlock.AsSpan(0, currentBlockSize),
                        containerKey,
                        Encoding.UTF8.GetBytes($"block-{blockIndex}"));

                    // Accumulate hash over encrypted bytes on disk
                    payloadHasher.AppendData(encryptedBlock.Nonce);
                    payloadHasher.AppendData(encryptedBlock.Tag);
                    payloadHasher.AppendData(encryptedBlock.Ciphertext);

                    await fs.WriteAsync(encryptedBlock.Nonce, cancellationToken);
                    await fs.WriteAsync(encryptedBlock.Tag, cancellationToken);
                    await fs.WriteAsync(encryptedBlock.Ciphertext, cancellationToken);

                    progress?.Report(0.1 + 0.7 * (blockIndex + 1) / totalBlocks);
                }

                // ── VaultManifest Footer (optional) ─────────────────────
                if (manifest != null)
                {
                    await WriteVaultManifestFooterAsync(fs, fs.Position, manifest, containerKey, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(0.9);

                // ── Backpatch PayloadHash + encrypted private header ────
                byte[] payloadHash = payloadHasher.GetHashAndReset();
                containerManifest.PayloadHash = Convert.ToBase64String(payloadHash);
                byte[] finalPrivateManifestJson = JsonSerializer.SerializeToUtf8Bytes(containerManifest, ContainerManifestJsonOptions);
                if (finalPrivateManifestJson.Length != privateManifestJson.Length)
                    throw new InvalidOperationException("Encrypted private header JSON size changed during backpatch — this is a bug");

                var finalPrivateManifestEnvelope = _encryptionService.Encrypt(finalPrivateManifestJson, containerKey, bootstrapJson);
                if (finalPrivateManifestEnvelope.Ciphertext.Length != privateManifestEnvelope.Ciphertext.Length)
                    throw new InvalidOperationException("Encrypted private header size changed during backpatch — this is a bug");

                fs.Seek(privateHeaderNoncePos, SeekOrigin.Begin);
                await fs.WriteAsync(finalPrivateManifestEnvelope.Nonce, cancellationToken);
                await fs.WriteAsync(finalPrivateManifestEnvelope.Tag, cancellationToken);
                await fs.WriteAsync(finalPrivateManifestEnvelope.Ciphertext, cancellationToken);

                await fs.FlushAsync(cancellationToken);
                progress?.Report(1.0);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(containerKey);
                CryptographicOperations.ZeroMemory(hmacKey);
            }
        }

        /// <summary>
        /// Opens and decrypts a container (v3 or v4), writing decrypted data to the target path.
        /// v4: Verifies HMAC + payload hash before returning.
        /// v3: Falls back to legacy header + encrypted metadata + block decryption.
        /// </summary>
        public async Task<string> OpenContainerAsync(
            string containerPath,
            string targetPath,
            string? password,
            string? keyfilePath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath))
                throw new ArgumentException("Container path is required", nameof(containerPath));
            if (!File.Exists(containerPath))
                throw new FileNotFoundException("Container not found", containerPath);

            using var fileStream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Read and verify magic header
            byte[] headerBytes = new byte[HeaderMagicSize];
            await fileStream.ReadExactlyAsync(headerBytes, cancellationToken);
            if (Encoding.ASCII.GetString(headerBytes) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            // Read version
            byte[] versionBytes = new byte[VersionFieldSize];
            await fileStream.ReadExactlyAsync(versionBytes, cancellationToken);
            int version = BitConverter.ToInt32(versionBytes);
            if (version < 1 || version > MaxSupportedVersion)
                throw new InvalidOperationException($"Unsupported container version: {version}");

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            await using var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (version >= 4)
            {
                await OpenContainerV4Async(fileStream, outputStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await OpenContainerV3Async(fileStream, version, outputStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            }

            return targetPath;
        }

        /// <summary>
        /// Opens and decrypts a container directly into the provided stream.
        /// This is the preferred access path for nested containers to avoid plaintext files on disk.
        /// </summary>
        public async Task OpenContainerToStreamAsync(
            string containerPath,
            Stream outputStream,
            string? password,
            string? keyfilePath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath))
                throw new ArgumentException("Container path is required", nameof(containerPath));
            if (!File.Exists(containerPath))
                throw new FileNotFoundException("Container not found", containerPath);
            if (outputStream == null || !outputStream.CanWrite)
                throw new ArgumentException("Output stream must be writable", nameof(outputStream));

            using var fileStream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] headerBytes = new byte[HeaderMagicSize];
            await fileStream.ReadExactlyAsync(headerBytes, cancellationToken);
            if (Encoding.ASCII.GetString(headerBytes) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            byte[] versionBytes = new byte[VersionFieldSize];
            await fileStream.ReadExactlyAsync(versionBytes, cancellationToken);
            int version = BitConverter.ToInt32(versionBytes);
            if (version < 1 || version > MaxSupportedVersion)
                throw new InvalidOperationException($"Unsupported container version: {version}");

            if (version >= 4)
            {
                await OpenContainerV4Async(fileStream, outputStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await OpenContainerV3Async(fileStream, version, outputStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the declared payload size from the container manifest/header.
        /// New encrypted-private-header containers require the authenticated overload.
        /// </summary>
        public async Task<long> GetPayloadSizeAsync(string containerPath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var fileStream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] headerBytes = new byte[HeaderMagicSize];
            await fileStream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            if (Encoding.ASCII.GetString(headerBytes) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            byte[] versionBytes = new byte[VersionFieldSize];
            await fileStream.ReadExactlyAsync(versionBytes, cancellationToken).ConfigureAwait(false);
            int version = BitConverter.ToInt32(versionBytes);

            if (version >= 4)
            {
                var manifestSection = await ReadV4ManifestSectionAsync(fileStream, cancellationToken).ConfigureAwait(false);
                return manifestSection.ContainerManifest.PayloadSize;
            }

            byte[] salt = new byte[SaltSize];
            await fileStream.ReadExactlyAsync(salt, cancellationToken).ConfigureAwait(false);

            if (version >= 2)
            {
                byte[] iterationBytes = new byte[V3_IterationCountSize];
                await fileStream.ReadExactlyAsync(iterationBytes, cancellationToken).ConfigureAwait(false);
            }

            byte[] sizeBytes = new byte[V3_ContainerSizeSize];
            await fileStream.ReadExactlyAsync(sizeBytes, cancellationToken).ConfigureAwait(false);
            return BitConverter.ToInt64(sizeBytes);
        }

        public async Task<long> GetPayloadSizeAsync(
            string containerPath,
            string? password,
            string? keyfilePath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var fileStream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] headerBytes = new byte[HeaderMagicSize];
            await fileStream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            if (Encoding.ASCII.GetString(headerBytes) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            byte[] versionBytes = new byte[VersionFieldSize];
            await fileStream.ReadExactlyAsync(versionBytes, cancellationToken).ConfigureAwait(false);
            int version = BitConverter.ToInt32(versionBytes);

            if (version >= 4)
            {
                using var context = await AuthenticateV4ContainerAsync(fileStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
                return context.ManifestSection.ContainerManifest.PayloadSize;
            }

            return await GetPayloadSizeAsync(containerPath, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> OpenContainerV4Async(
            FileStream fs, string targetPath, string? password, string? keyfilePath, CancellationToken ct)
        {
            await using var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await OpenContainerV4Async(fs, outputStream, password, keyfilePath, ct).ConfigureAwait(false);
            return targetPath;
        }

        private async Task OpenContainerV4Async(
            FileStream fs, Stream outputStream, string? password, string? keyfilePath, CancellationToken ct)
        {
            using var context = await AuthenticateV4ContainerAsync(fs, password, keyfilePath, ct).ConfigureAwait(false);
            // Decrypt blocks while accumulating payload hash
            long payloadSize = context.ManifestSection.ContainerManifest.PayloadSize;
            long totalBlocks = (payloadSize + BlockSize - 1) / BlockSize;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            for (long blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
            {
                ct.ThrowIfCancellationRequested();

                byte[] blockNonce = new byte[NonceSize];
                await fs.ReadExactlyAsync(blockNonce, ct);

                byte[] blockTag = new byte[TagSize];
                await fs.ReadExactlyAsync(blockTag, ct);

                int currentBlockSize = (int)Math.Min(BlockSize, payloadSize - (blockIndex * BlockSize));
                byte[] blockCiphertext = new byte[currentBlockSize];
                await fs.ReadExactlyAsync(blockCiphertext, ct);

                // Hash the raw encrypted bytes
                payloadHasher.AppendData(blockNonce);
                payloadHasher.AppendData(blockTag);
                payloadHasher.AppendData(blockCiphertext);

                // Decrypt
                byte[] blockPlaintext = _encryptionService.Decrypt(
                    blockCiphertext, blockNonce, blockTag, context.ContainerKey,
                    Encoding.UTF8.GetBytes($"block-{blockIndex}"));

                await outputStream.WriteAsync(blockPlaintext.AsMemory(0, Math.Min(blockPlaintext.Length, currentBlockSize)), ct);
            }

            await outputStream.FlushAsync(ct);

            // Verify payload hash
            byte[] computedHash = payloadHasher.GetHashAndReset();
            byte[] expectedHash = ParsePayloadHash(context.ManifestSection.ContainerManifest.PayloadHash);
            if (!CryptographicOperations.FixedTimeEquals(computedHash, expectedHash))
            {
                throw new CryptographicException("Payload integrity check failed — container data may be corrupted or tampered");
            }
        }

        private async Task<string> OpenContainerV3Async(
            FileStream fileStream, int version, string targetPath, string? password, string? keyfilePath, CancellationToken cancellationToken)
        {
            await using var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await OpenContainerV3Async(fileStream, version, outputStream, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            return targetPath;
        }

        private async Task OpenContainerV3Async(
            FileStream fileStream, int version, Stream outputStream, string? password, string? keyfilePath, CancellationToken cancellationToken)
        {
            // v3 header continuation: magic+version already consumed
            byte[] salt = new byte[SaltSize];
            await fileStream.ReadExactlyAsync(salt, cancellationToken);

            int iterations = DefaultIterations;
            if (version >= 2)
            {
                byte[] iterationBytes = new byte[V3_IterationCountSize];
                await fileStream.ReadExactlyAsync(iterationBytes, cancellationToken);
                iterations = BitConverter.ToInt32(iterationBytes);
            }

            byte[] sizeBytes = new byte[V3_ContainerSizeSize];
            await fileStream.ReadExactlyAsync(sizeBytes, cancellationToken);
            long containerSize = BitConverter.ToInt64(sizeBytes);

            if (version >= 3)
            {
                byte[] manifestOffsetBytes = new byte[V3_ManifestOffsetSize];
                await fileStream.ReadExactlyAsync(manifestOffsetBytes, cancellationToken);
            }

            byte[] containerKey = await DeriveContainerKeyAsync(password, keyfilePath, salt, iterations, DefaultMemoryKb);

            try
            {
                // Read encrypted metadata
                byte[] metadataSizeBytes = new byte[4];
                await fileStream.ReadExactlyAsync(metadataSizeBytes, cancellationToken);
                int metadataSize = BitConverter.ToInt32(metadataSizeBytes);

                byte[] metadataNonce = new byte[NonceSize];
                await fileStream.ReadExactlyAsync(metadataNonce, cancellationToken);

                byte[] metadataTag = new byte[TagSize];
                await fileStream.ReadExactlyAsync(metadataTag, cancellationToken);

                byte[] metadataCiphertext = new byte[metadataSize];
                await fileStream.ReadExactlyAsync(metadataCiphertext, cancellationToken);

                try
                {
                    _encryptionService.Decrypt(metadataCiphertext, metadataNonce, metadataTag, containerKey, Array.Empty<byte>());
                }
                catch (CryptographicException)
                {
                    throw new UnauthorizedAccessException("Invalid password or keyfile");
                }

                long totalBlocks = (containerSize + BlockSize - 1) / BlockSize;
                for (long blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] blockNonce = new byte[NonceSize];
                    await fileStream.ReadExactlyAsync(blockNonce, cancellationToken);

                    byte[] blockTag = new byte[TagSize];
                    await fileStream.ReadExactlyAsync(blockTag, cancellationToken);

                    int currentBlockSize = (int)Math.Min(BlockSize, containerSize - (blockIndex * BlockSize));
                    byte[] blockCiphertext = new byte[currentBlockSize];
                    await fileStream.ReadExactlyAsync(blockCiphertext, cancellationToken);

                    byte[] blockPlaintext = _encryptionService.Decrypt(
                        blockCiphertext, blockNonce, blockTag, containerKey,
                        Encoding.UTF8.GetBytes($"block-{blockIndex}"));

                    await outputStream.WriteAsync(blockPlaintext.AsMemory(0, Math.Min(blockPlaintext.Length, currentBlockSize)), cancellationToken);
                }

                await outputStream.FlushAsync(cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(containerKey);
            }
        }

        /// <summary>
        /// Closes a container by securely deleting the decrypted file.
        /// Overwrites file contents with random data before deletion to prevent recovery.
        /// </summary>
        public async Task CloseContainerAsync(string targetPath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!File.Exists(targetPath))
                return;

            await SecureDeleteFileAsync(targetPath, cancellationToken);
        }

        /// <summary>
        /// Securely deletes a file by overwriting its contents multiple times before deletion.
        /// This helps prevent data recovery from the physical storage medium.
        /// </summary>
        private static async Task SecureDeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                return;

            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                if (fileSize > 0)
                {
                    // Overwrite file contents multiple times with different patterns
                    using var fileStream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        FileOptions.WriteThrough); // Bypass OS cache

                    byte[] buffer = new byte[Math.Min(BlockSize, fileSize)];

                    for (int pass = 0; pass < SecureWipePassCount; pass++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        fileStream.Position = 0;
                        long remaining = fileSize;

                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int bytesToWrite = (int)Math.Min(buffer.Length, remaining);

                            // Use different patterns for each pass
                            switch (pass)
                            {
                                case 0:
                                    // Pass 1: Random data
                                    RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
                                    break;
                                case 1:
                                    // Pass 2: Complement of zeros (0xFF)
                                    Array.Fill(buffer, (byte)0xFF, 0, bytesToWrite);
                                    break;
                                case 2:
                                    // Pass 3: Random data again
                                    RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
                                    break;
                            }

                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesToWrite), cancellationToken);
                            remaining -= bytesToWrite;
                        }

                        await fileStream.FlushAsync(cancellationToken);
                    }

                    // Zero the buffer before releasing
                    CryptographicOperations.ZeroMemory(buffer);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // If secure overwrite fails, still attempt regular deletion
            }

            // Finally, delete the file
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
                // File might be locked; try with a slight delay
                await Task.Delay(100, cancellationToken);
                File.Delete(filePath);
            }
        }

        private async Task<byte[]> DeriveContainerKeyAsync(string? password, string? keyfilePath, byte[] salt, int iterations, int memoryCostKb)
        {
            byte[] passwordKey = Array.Empty<byte>();
            byte[] keyfileKey = Array.Empty<byte>();
            byte[] combinedKey = Array.Empty<byte>();

            try
            {
                // Derive key from password using Argon2id via EncryptionService
                if (!string.IsNullOrEmpty(password))
                {
                    passwordKey = _encryptionService.DeriveKey(password.AsSpan(), salt, 32, memoryCostKb, iterations);
                }
                else
                {
                    // Use a zero key if no password (keyfile-only mode)
                    passwordKey = new byte[32];
                }

                // Derive key from keyfile using HKDF
                if (!string.IsNullOrEmpty(keyfilePath))
                {
                    byte[] keyfileBytes = await CompositeKeyfilePath.ReadCombinedBytesAsync(keyfilePath, required: true).ConfigureAwait(false);
                    try
                    {
                        // Use HKDF to derive a key from the keyfile contents
                        // This is more secure than simple concatenation
                        keyfileKey = HKDF.DeriveKey(
                            HashAlgorithmName.SHA256,
                            keyfileBytes,
                            32, // output length
                            salt,
                            Encoding.UTF8.GetBytes("PhantomContainer.Keyfile.v2"));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(keyfileBytes);
                    }
                }
                else
                {
                    keyfileKey = new byte[32];
                }

                // Combine password key and keyfile key using HKDF
                // This ensures proper cryptographic mixing of both secrets
                byte[] combinedInput = new byte[passwordKey.Length + keyfileKey.Length];
                try
                {
                    Buffer.BlockCopy(passwordKey, 0, combinedInput, 0, passwordKey.Length);
                    Buffer.BlockCopy(keyfileKey, 0, combinedInput, passwordKey.Length, keyfileKey.Length);

                    combinedKey = HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        combinedInput,
                        32, // output length
                        salt,
                        Encoding.UTF8.GetBytes("PhantomContainer.Combined.v2"));

                    return combinedKey;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(combinedInput);
                }
            }
            finally
            {
                // Zero intermediate keys
                if (passwordKey.Length > 0)
                    CryptographicOperations.ZeroMemory(passwordKey);
                if (keyfileKey.Length > 0)
                    CryptographicOperations.ZeroMemory(keyfileKey);
            }
        }

        private static string ComputeHeaderChecksum(byte[] salt, long size, int iterations)
        {
            using var sha256 = SHA256.Create();
            sha256.TransformBlock(salt, 0, salt.Length, null, 0);
            byte[] sizeBytes = BitConverter.GetBytes(size);
            sha256.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);
            byte[] iterBytes = BitConverter.GetBytes(iterations);
            sha256.TransformFinalBlock(iterBytes, 0, iterBytes.Length);
            return Convert.ToBase64String(sha256.Hash!);
        }

        /// <summary>
        /// Synchronous key derivation used for manifest operations on an existing container.
        /// Same derivation as DeriveContainerKeyAsync but uses synchronous file I/O.
        /// </summary>
        private byte[] DeriveContainerKey(string? password, string? keyfilePath, byte[] salt, int iterations, int memoryCostKb)
        {
            byte[] passwordKey = Array.Empty<byte>();
            byte[] keyfileKey = Array.Empty<byte>();

            try
            {
                if (!string.IsNullOrEmpty(password))
                    passwordKey = _encryptionService.DeriveKey(password.AsSpan(), salt, 32, memoryCostKb, iterations);
                else
                    passwordKey = new byte[32];

                if (!string.IsNullOrEmpty(keyfilePath))
                {
                    byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
                    try
                    {
                        keyfileKey = HKDF.DeriveKey(
                            HashAlgorithmName.SHA256,
                            keyfileBytes,
                            32,
                            salt,
                            Encoding.UTF8.GetBytes("PhantomContainer.Keyfile.v2"));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(keyfileBytes);
                    }
                }
                else
                {
                    keyfileKey = new byte[32];
                }

                byte[] combinedInput = new byte[passwordKey.Length + keyfileKey.Length];
                try
                {
                    Buffer.BlockCopy(passwordKey, 0, combinedInput, 0, passwordKey.Length);
                    Buffer.BlockCopy(keyfileKey, 0, combinedInput, passwordKey.Length, keyfileKey.Length);

                    return HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        combinedInput,
                        32,
                        salt,
                        Encoding.UTF8.GetBytes("PhantomContainer.Combined.v2"));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(combinedInput);
                }
            }
            finally
            {
                if (passwordKey.Length > 0) CryptographicOperations.ZeroMemory(passwordKey);
                if (keyfileKey.Length > 0) CryptographicOperations.ZeroMemory(keyfileKey);
            }
        }

        /// <summary>
        /// Derives the HMAC-SHA256 key from the container key using HKDF domain separation.
        /// </summary>
        private static byte[] DeriveHmacKey(byte[] containerKey)
        {
            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                containerKey,
                32,
                salt: Array.Empty<byte>(),
                V4_HmacDomainInfo);
        }

        /// <summary>
        /// Reads the unauthenticated public container header from a v4 container.
        /// Legacy v4 containers expose the full manifest and can still be read here.
        /// New v4 containers expose only bootstrap data, so this returns null for them.
        /// </summary>
        public static ContainerManifest? ReadContainerManifest(string containerPath)
        {
            if (string.IsNullOrEmpty(containerPath) || !File.Exists(containerPath))
                return null;

            using var fs = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Read and verify magic
            Span<byte> magicBuf = stackalloc byte[HeaderMagicSize];
            fs.ReadExactly(magicBuf);
            if (Encoding.ASCII.GetString(magicBuf) != MagicHeader)
                return null;

            // Read version
            Span<byte> versionBuf = stackalloc byte[VersionFieldSize];
            fs.ReadExactly(versionBuf);
            int version = BitConverter.ToInt32(versionBuf);
            if (version < 4)
                return null; // Container manifests only exist in v4+

            try
            {
                return ReadV4ManifestSection(fs).ContainerManifest;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Computes the file offset where the VaultManifest footer starts in a v4 container.
        /// </summary>
        private static long ComputeVaultManifestOffset(long payloadStartOffset, long payloadSize)
        {
            long totalBlocks = (payloadSize + BlockSize - 1) / BlockSize;
            // Each encrypted block on disk: Nonce + Tag + ciphertext(blockSize or remainder)
            // Total encrypted area = sum of per-block overhead + payloadSize
            return payloadStartOffset + totalBlocks * (NonceSize + TagSize) + payloadSize;
        }

        private static int ValidateFooterCiphertextSize(int ciphertextSize, long fileLength, long footerOffset)
        {
            if (ciphertextSize <= 0)
                throw new InvalidOperationException("Invalid vault manifest footer size");

            long remainingBytes = fileLength - footerOffset - ManifestMarkerSize - sizeof(int);
            long requiredBytes = NonceSize + TagSize + (long)ciphertextSize;
            if (remainingBytes < requiredBytes)
                throw new InvalidOperationException("Vault manifest footer is truncated");

            return ciphertextSize;
        }

        private static VaultManifestFooterSection? ReadVaultManifestFooter(FileStream fs, long footerOffset)
        {
            if (footerOffset >= fs.Length)
                return null;

            fs.Seek(footerOffset, SeekOrigin.Begin);

            Span<byte> markerBuf = stackalloc byte[ManifestMarkerSize];
            fs.ReadExactly(markerBuf);
            if (Encoding.ASCII.GetString(markerBuf) != ManifestMarker)
                return null;

            Span<byte> sizeBuf = stackalloc byte[sizeof(int)];
            fs.ReadExactly(sizeBuf);
            int ciphertextSize = ValidateFooterCiphertextSize(BitConverter.ToInt32(sizeBuf), fs.Length, footerOffset);

            byte[] nonce = new byte[NonceSize];
            fs.ReadExactly(nonce);

            byte[] tag = new byte[TagSize];
            fs.ReadExactly(tag);

            byte[] ciphertext = new byte[ciphertextSize];
            fs.ReadExactly(ciphertext);

            return new VaultManifestFooterSection
            {
                FooterOffset = footerOffset,
                CiphertextSize = ciphertextSize,
                Nonce = nonce,
                Tag = tag,
                Ciphertext = ciphertext
            };
        }

        private void WriteVaultManifestFooter(FileStream fs, long footerOffset, VaultManifest manifest, byte[] containerKey)
        {
            fs.SetLength(footerOffset);
            fs.Seek(footerOffset, SeekOrigin.Begin);

            string vaultJson = JsonSerializer.Serialize(manifest);
            byte[] vaultBytes = Encoding.UTF8.GetBytes(vaultJson);
            var encrypted = _encryptionService.Encrypt(vaultBytes, containerKey);

            fs.Write(Encoding.ASCII.GetBytes(ManifestMarker));
            fs.Write(BitConverter.GetBytes(encrypted.Ciphertext.Length));
            fs.Write(encrypted.Nonce);
            fs.Write(encrypted.Tag);
            fs.Write(encrypted.Ciphertext);
        }

        private async Task WriteVaultManifestFooterAsync(FileStream fs, long footerOffset, VaultManifest manifest, byte[] containerKey, CancellationToken cancellationToken)
        {
            fs.SetLength(footerOffset);
            fs.Seek(footerOffset, SeekOrigin.Begin);

            string vaultJson = JsonSerializer.Serialize(manifest);
            byte[] vaultBytes = Encoding.UTF8.GetBytes(vaultJson);
            var encrypted = _encryptionService.Encrypt(vaultBytes, containerKey);

            await fs.WriteAsync(Encoding.ASCII.GetBytes(ManifestMarker), cancellationToken).ConfigureAwait(false);
            await fs.WriteAsync(BitConverter.GetBytes(encrypted.Ciphertext.Length), cancellationToken).ConfigureAwait(false);
            await fs.WriteAsync(encrypted.Nonce, cancellationToken).ConfigureAwait(false);
            await fs.WriteAsync(encrypted.Tag, cancellationToken).ConfigureAwait(false);
            await fs.WriteAsync(encrypted.Ciphertext, cancellationToken).ConfigureAwait(false);
        }

        private sealed class LegacyV4Section
        {
            public required V4ManifestSection ManifestSection { get; init; }
        }

        private static bool TryParseBootstrapHeader(byte[] headerJsonBytes, out V4PublicBootstrapHeader? bootstrapHeader)
        {
            try
            {
                bootstrapHeader = JsonSerializer.Deserialize<V4PublicBootstrapHeader>(headerJsonBytes, ContainerManifestJsonOptions);
                if (bootstrapHeader != null &&
                    string.Equals(bootstrapHeader.HeaderMode, V4_PrivateHeaderMode, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(bootstrapHeader.Salt) &&
                    bootstrapHeader.KdfIterations > 0 &&
                    bootstrapHeader.KdfMemoryKb > 0 &&
                    bootstrapHeader.PrivateHeaderCiphertextSize > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Fall back to legacy parsing.
            }

            bootstrapHeader = null;
            return false;
        }

        private static async Task<V4BootstrapSection> ReadV4BootstrapSectionAsync(FileStream fs, CancellationToken cancellationToken)
        {
            await ReadAndValidateV4HeaderSizeAsync(fs, cancellationToken).ConfigureAwait(false);

            byte[] headerSizeBytes = new byte[V4_ManifestSizeFieldSize];
            await fs.ReadExactlyAsync(headerSizeBytes, cancellationToken).ConfigureAwait(false);
            int headerSize = ValidateManifestSize(BitConverter.ToInt32(headerSizeBytes));

            byte[] headerJsonBytes = new byte[headerSize];
            await fs.ReadExactlyAsync(headerJsonBytes, cancellationToken).ConfigureAwait(false);
            if (!TryParseBootstrapHeader(headerJsonBytes, out var bootstrapHeader) || bootstrapHeader == null)
            {
                throw new InvalidOperationException("Container does not use an encrypted private header.");
            }

            byte[] nonce = new byte[NonceSize];
            await fs.ReadExactlyAsync(nonce, cancellationToken).ConfigureAwait(false);

            byte[] tag = new byte[TagSize];
            await fs.ReadExactlyAsync(tag, cancellationToken).ConfigureAwait(false);

            byte[] ciphertext = new byte[bootstrapHeader.PrivateHeaderCiphertextSize];
            await fs.ReadExactlyAsync(ciphertext, cancellationToken).ConfigureAwait(false);

            return new V4BootstrapSection
            {
                HeaderSize = headerSize,
                HeaderJsonBytes = headerJsonBytes,
                BootstrapHeader = bootstrapHeader,
                Nonce = nonce,
                Tag = tag,
                Ciphertext = ciphertext,
                PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + headerSize + NonceSize + TagSize + bootstrapHeader.PrivateHeaderCiphertextSize,
            };
        }

        private static V4BootstrapSection ReadV4BootstrapSection(FileStream fs)
        {
            ReadAndValidateV4HeaderSize(fs);

            Span<byte> headerSizeBuf = stackalloc byte[V4_ManifestSizeFieldSize];
            fs.ReadExactly(headerSizeBuf);
            int headerSize = ValidateManifestSize(BitConverter.ToInt32(headerSizeBuf));

            byte[] headerJsonBytes = new byte[headerSize];
            fs.ReadExactly(headerJsonBytes);
            if (!TryParseBootstrapHeader(headerJsonBytes, out var bootstrapHeader) || bootstrapHeader == null)
            {
                throw new InvalidOperationException("Container does not use an encrypted private header.");
            }

            byte[] nonce = new byte[NonceSize];
            fs.ReadExactly(nonce);

            byte[] tag = new byte[TagSize];
            fs.ReadExactly(tag);

            byte[] ciphertext = new byte[bootstrapHeader.PrivateHeaderCiphertextSize];
            fs.ReadExactly(ciphertext);

            return new V4BootstrapSection
            {
                HeaderSize = headerSize,
                HeaderJsonBytes = headerJsonBytes,
                BootstrapHeader = bootstrapHeader,
                Nonce = nonce,
                Tag = tag,
                Ciphertext = ciphertext,
                PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + headerSize + NonceSize + TagSize + bootstrapHeader.PrivateHeaderCiphertextSize,
            };
        }

        private static async Task<LegacyV4Section> ReadLegacyV4ManifestSectionAsync(FileStream fs, CancellationToken cancellationToken)
        {
            await ReadAndValidateV4HeaderSizeAsync(fs, cancellationToken).ConfigureAwait(false);

            byte[] manifestSizeBytes = new byte[V4_ManifestSizeFieldSize];
            await fs.ReadExactlyAsync(manifestSizeBytes, cancellationToken).ConfigureAwait(false);
            int manifestSize = ValidateManifestSize(BitConverter.ToInt32(manifestSizeBytes));

            byte[] manifestJsonBytes = new byte[manifestSize];
            await fs.ReadExactlyAsync(manifestJsonBytes, cancellationToken).ConfigureAwait(false);

            if (TryParseBootstrapHeader(manifestJsonBytes, out _))
            {
                throw new InvalidOperationException("Encrypted private-header containers must be authenticated first.");
            }

            byte[] storedHmac = new byte[V4_HmacSize];
            await fs.ReadExactlyAsync(storedHmac, cancellationToken).ConfigureAwait(false);

            var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                ?? throw new InvalidOperationException("Failed to parse container manifest");

            if (containerManifest.PayloadSize <= 0)
                throw new InvalidOperationException("Invalid container payload size");

            return new LegacyV4Section
            {
                ManifestSection = new V4ManifestSection
                {
                    ManifestSize = manifestSize,
                    ManifestJsonBytes = manifestJsonBytes,
                    StoredHmac = storedHmac,
                    ContainerManifest = containerManifest,
                    PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + manifestSize + V4_HmacSize,
                    UsesEncryptedPrivateHeader = false,
                }
            };
        }

        private static LegacyV4Section ReadLegacyV4ManifestSection(FileStream fs)
        {
            ReadAndValidateV4HeaderSize(fs);

            Span<byte> manifestSizeBuf = stackalloc byte[V4_ManifestSizeFieldSize];
            fs.ReadExactly(manifestSizeBuf);
            int manifestSize = ValidateManifestSize(BitConverter.ToInt32(manifestSizeBuf));

            byte[] manifestJsonBytes = new byte[manifestSize];
            fs.ReadExactly(manifestJsonBytes);

            if (TryParseBootstrapHeader(manifestJsonBytes, out _))
            {
                throw new InvalidOperationException("Encrypted private-header containers must be authenticated first.");
            }

            byte[] storedHmac = new byte[V4_HmacSize];
            fs.ReadExactly(storedHmac);

            var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                ?? throw new InvalidOperationException("Failed to parse container manifest");

            if (containerManifest.PayloadSize <= 0)
                throw new InvalidOperationException("Invalid container payload size");

            return new LegacyV4Section
            {
                ManifestSection = new V4ManifestSection
                {
                    ManifestSize = manifestSize,
                    ManifestJsonBytes = manifestJsonBytes,
                    StoredHmac = storedHmac,
                    ContainerManifest = containerManifest,
                    PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + manifestSize + V4_HmacSize,
                    UsesEncryptedPrivateHeader = false,
                }
            };
        }

        private async Task<AuthenticatedV4ContainerContext?> TryAuthenticatePrivateHeaderAsync(
            FileStream fs,
            string? password,
            string? keyfilePath,
            CancellationToken cancellationToken)
        {
            long originalPosition = fs.Position;
            try
            {
                var bootstrapSection = await ReadV4BootstrapSectionAsync(fs, cancellationToken).ConfigureAwait(false);
                byte[] salt = Convert.FromBase64String(bootstrapSection.BootstrapHeader.Salt);
                byte[] containerKey = await DeriveContainerKeyAsync(
                    password,
                    keyfilePath,
                    salt,
                    bootstrapSection.BootstrapHeader.KdfIterations,
                    bootstrapSection.BootstrapHeader.KdfMemoryKb).ConfigureAwait(false);

                try
                {
                    byte[] manifestJsonBytes = _encryptionService.Decrypt(
                        bootstrapSection.Ciphertext,
                        bootstrapSection.Nonce,
                        bootstrapSection.Tag,
                        containerKey,
                        bootstrapSection.HeaderJsonBytes);

                    var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                        ?? throw new InvalidOperationException("Failed to parse encrypted container header");

                    if (containerManifest.PayloadSize <= 0)
                        throw new InvalidOperationException("Invalid container payload size");

                    return new AuthenticatedV4ContainerContext
                    {
                        ManifestSection = new V4ManifestSection
                        {
                            ManifestSize = bootstrapSection.HeaderSize,
                            ManifestJsonBytes = manifestJsonBytes,
                            StoredHmac = Array.Empty<byte>(),
                            ContainerManifest = containerManifest,
                            PayloadStartOffset = bootstrapSection.PayloadStartOffset,
                            UsesEncryptedPrivateHeader = true,
                        },
                        ContainerKey = containerKey,
                        HmacKey = Array.Empty<byte>(),
                    };
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(containerKey);
                    throw;
                }
            }
            catch (InvalidOperationException)
            {
                fs.Seek(originalPosition, SeekOrigin.Begin);
                return null;
            }
        }

        private AuthenticatedV4ContainerContext? TryAuthenticatePrivateHeader(
            FileStream fs,
            string? password,
            string? keyfilePath)
        {
            long originalPosition = fs.Position;
            try
            {
                var bootstrapSection = ReadV4BootstrapSection(fs);
                byte[] salt = Convert.FromBase64String(bootstrapSection.BootstrapHeader.Salt);
                byte[] containerKey = DeriveContainerKey(
                    password,
                    keyfilePath,
                    salt,
                    bootstrapSection.BootstrapHeader.KdfIterations,
                    bootstrapSection.BootstrapHeader.KdfMemoryKb);

                try
                {
                    byte[] manifestJsonBytes = _encryptionService.Decrypt(
                        bootstrapSection.Ciphertext,
                        bootstrapSection.Nonce,
                        bootstrapSection.Tag,
                        containerKey,
                        bootstrapSection.HeaderJsonBytes);

                    var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                        ?? throw new InvalidOperationException("Failed to parse encrypted container header");

                    if (containerManifest.PayloadSize <= 0)
                        throw new InvalidOperationException("Invalid container payload size");

                    return new AuthenticatedV4ContainerContext
                    {
                        ManifestSection = new V4ManifestSection
                        {
                            ManifestSize = bootstrapSection.HeaderSize,
                            ManifestJsonBytes = manifestJsonBytes,
                            StoredHmac = Array.Empty<byte>(),
                            ContainerManifest = containerManifest,
                            PayloadStartOffset = bootstrapSection.PayloadStartOffset,
                            UsesEncryptedPrivateHeader = true,
                        },
                        ContainerKey = containerKey,
                        HmacKey = Array.Empty<byte>(),
                    };
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(containerKey);
                    throw;
                }
            }
            catch (InvalidOperationException)
            {
                fs.Seek(originalPosition, SeekOrigin.Begin);
                return null;
            }
        }

        private static async Task<V4ManifestSection> ReadV4ManifestSectionAsync(FileStream fs, CancellationToken cancellationToken)
        {
            await ReadAndValidateV4HeaderSizeAsync(fs, cancellationToken).ConfigureAwait(false);

            byte[] manifestSizeBytes = new byte[V4_ManifestSizeFieldSize];
            await fs.ReadExactlyAsync(manifestSizeBytes, cancellationToken).ConfigureAwait(false);
            int manifestSize = ValidateManifestSize(BitConverter.ToInt32(manifestSizeBytes));

            byte[] manifestJsonBytes = new byte[manifestSize];
            await fs.ReadExactlyAsync(manifestJsonBytes, cancellationToken).ConfigureAwait(false);

            if (TryParseBootstrapHeader(manifestJsonBytes, out _))
            {
                throw new UnauthorizedAccessException("Container private header requires authentication.");
            }

            byte[] storedHmac = new byte[V4_HmacSize];
            await fs.ReadExactlyAsync(storedHmac, cancellationToken).ConfigureAwait(false);

            var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                ?? throw new InvalidOperationException("Failed to parse container manifest");

            if (containerManifest.PayloadSize <= 0)
                throw new InvalidOperationException("Invalid container payload size");

            return new V4ManifestSection
            {
                ManifestSize = manifestSize,
                ManifestJsonBytes = manifestJsonBytes,
                StoredHmac = storedHmac,
                ContainerManifest = containerManifest,
                PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + manifestSize + V4_HmacSize,
                UsesEncryptedPrivateHeader = false,
            };
        }

        private static V4ManifestSection ReadV4ManifestSection(FileStream fs)
        {
            ReadAndValidateV4HeaderSize(fs);

            Span<byte> manifestSizeBuf = stackalloc byte[V4_ManifestSizeFieldSize];
            fs.ReadExactly(manifestSizeBuf);
            int manifestSize = ValidateManifestSize(BitConverter.ToInt32(manifestSizeBuf));

            byte[] manifestJsonBytes = new byte[manifestSize];
            fs.ReadExactly(manifestJsonBytes);

            if (TryParseBootstrapHeader(manifestJsonBytes, out _))
            {
                throw new UnauthorizedAccessException("Container private header requires authentication.");
            }

            byte[] storedHmac = new byte[V4_HmacSize];
            fs.ReadExactly(storedHmac);

            var containerManifest = JsonSerializer.Deserialize<ContainerManifest>(manifestJsonBytes, ContainerManifestJsonOptions)
                ?? throw new InvalidOperationException("Failed to parse container manifest");

            if (containerManifest.PayloadSize <= 0)
                throw new InvalidOperationException("Invalid container payload size");

            return new V4ManifestSection
            {
                ManifestSize = manifestSize,
                ManifestJsonBytes = manifestJsonBytes,
                StoredHmac = storedHmac,
                ContainerManifest = containerManifest,
                PayloadStartOffset = V4_StaticHeaderTotalSize + V4_ManifestSizeFieldSize + manifestSize + V4_HmacSize,
                UsesEncryptedPrivateHeader = false,
            };
        }

        private async Task<AuthenticatedV4ContainerContext> AuthenticateV4ContainerAsync(FileStream fs, string? password, string? keyfilePath, CancellationToken cancellationToken)
        {
            var manifestSection = await TryAuthenticatePrivateHeaderAsync(fs, password, keyfilePath, cancellationToken).ConfigureAwait(false);
            if (manifestSection != null)
            {
                return manifestSection;
            }

            var legacySection = await ReadLegacyV4ManifestSectionAsync(fs, cancellationToken).ConfigureAwait(false);

            byte[] salt = Convert.FromBase64String(legacySection.ManifestSection.ContainerManifest.Salt);
            byte[] containerKey = await DeriveContainerKeyAsync(
                password,
                keyfilePath,
                salt,
                legacySection.ManifestSection.ContainerManifest.KdfIterations,
                legacySection.ManifestSection.ContainerManifest.KdfMemoryKb).ConfigureAwait(false);
            byte[] hmacKey = DeriveHmacKey(containerKey);

            byte[] computedHmac = HMACSHA256.HashData(hmacKey, legacySection.ManifestSection.ManifestJsonBytes);
            if (!CryptographicOperations.FixedTimeEquals(computedHmac, legacySection.ManifestSection.StoredHmac))
            {
                CryptographicOperations.ZeroMemory(containerKey);
                CryptographicOperations.ZeroMemory(hmacKey);
                throw new UnauthorizedAccessException("Invalid password or keyfile");
            }

            return new AuthenticatedV4ContainerContext
            {
                ManifestSection = legacySection.ManifestSection,
                ContainerKey = containerKey,
                HmacKey = hmacKey,
            };
        }

        private AuthenticatedV4ContainerContext AuthenticateV4Container(FileStream fs, string? password, string? keyfilePath)
        {
            var privateHeaderContext = TryAuthenticatePrivateHeader(fs, password, keyfilePath);
            if (privateHeaderContext != null)
            {
                return privateHeaderContext;
            }

            var legacySection = ReadLegacyV4ManifestSection(fs);

            byte[] salt = Convert.FromBase64String(legacySection.ManifestSection.ContainerManifest.Salt);
            byte[] containerKey = DeriveContainerKey(
                password,
                keyfilePath,
                salt,
                legacySection.ManifestSection.ContainerManifest.KdfIterations,
                legacySection.ManifestSection.ContainerManifest.KdfMemoryKb);
            byte[] hmacKey = DeriveHmacKey(containerKey);

            byte[] computedHmac = HMACSHA256.HashData(hmacKey, legacySection.ManifestSection.ManifestJsonBytes);
            if (!CryptographicOperations.FixedTimeEquals(computedHmac, legacySection.ManifestSection.StoredHmac))
            {
                CryptographicOperations.ZeroMemory(containerKey);
                CryptographicOperations.ZeroMemory(hmacKey);
                throw new UnauthorizedAccessException("Invalid password or keyfile");
            }

            return new AuthenticatedV4ContainerContext
            {
                ManifestSection = legacySection.ManifestSection,
                ContainerKey = containerKey,
                HmacKey = hmacKey,
            };
        }

        /// <summary>
        /// Reads and decrypts the embedded VaultManifest from a container (v3 or v4).
        /// v4: Verifies HMAC before decrypting the footer.
        /// v3: Falls back to legacy offset-based manifest read.
        /// Returns null if no manifest is embedded.
        /// </summary>
        public VaultManifest? ReadManifestFromContainer(string containerPath, string? password, string? keyfilePath)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath) || !File.Exists(containerPath))
                return null;

            using var fs = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Read magic + version
            Span<byte> magic = stackalloc byte[HeaderMagicSize];
            fs.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            Span<byte> versionBuf = stackalloc byte[VersionFieldSize];
            fs.ReadExactly(versionBuf);
            int version = BitConverter.ToInt32(versionBuf);

            if (version >= 4)
                return ReadManifestFromContainerV4(fs, password, keyfilePath);

            return ReadManifestFromContainerV3(fs, version, password, keyfilePath);
        }

        private VaultManifest? ReadManifestFromContainerV4(FileStream fs, string? password, string? keyfilePath)
        {
            using var context = AuthenticateV4Container(fs, password, keyfilePath);
            try
            {
                var footer = ReadVaultManifestFooter(fs, context.FooterOffset);
                if (footer == null)
                    return null; // No footer present

                byte[] manifestBytes = _encryptionService.Decrypt(footer.Ciphertext, footer.Nonce, footer.Tag, context.ContainerKey, Array.Empty<byte>());
                return JsonSerializer.Deserialize<VaultManifest>(Encoding.UTF8.GetString(manifestBytes));
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Invalid password or keyfile");
            }
        }

        private VaultManifest? ReadManifestFromContainerV3(FileStream fs, int version, string? password, string? keyfilePath)
        {
            // v3 header continuation: magic + version already consumed
            byte[] salt = new byte[SaltSize];
            fs.ReadExactly(salt);

            int iterations = DefaultIterations;
            if (version >= 2)
            {
                Span<byte> iterBuf = stackalloc byte[V3_IterationCountSize];
                fs.ReadExactly(iterBuf);
                iterations = BitConverter.ToInt32(iterBuf);
            }

            Span<byte> sizeBuf2 = stackalloc byte[V3_ContainerSizeSize];
            fs.ReadExactly(sizeBuf2);
            // containerSize not needed for manifest read

            long manifestOffset = 0;
            if (version >= 3)
            {
                Span<byte> manifestOffsetBuf = stackalloc byte[V3_ManifestOffsetSize];
                fs.ReadExactly(manifestOffsetBuf);
                manifestOffset = BitConverter.ToInt64(manifestOffsetBuf);
            }

            if (version < 3 || manifestOffset <= 0)
                return null;

            byte[] containerKey = DeriveContainerKey(password, keyfilePath, salt, iterations, DefaultMemoryKb);
            try
            {
                var footer = ReadVaultManifestFooter(fs, manifestOffset)
                    ?? throw new InvalidOperationException("Manifest marker not found at expected offset");

                byte[] manifestBytes = _encryptionService.Decrypt(footer.Ciphertext, footer.Nonce, footer.Tag, containerKey, Array.Empty<byte>());
                return JsonSerializer.Deserialize<VaultManifest>(Encoding.UTF8.GetString(manifestBytes));
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Invalid password or keyfile");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(containerKey);
            }
        }

        /// <summary>
        /// Updates the embedded VaultManifest footer in a container (v3 or v4).
        /// v4: Verifies HMAC first, computes deterministic footer offset, truncates + rewrites footer.
        /// v3: Falls back to legacy backpatch-offset behavior.
        /// </summary>
        public void UpdateManifestInContainer(string containerPath, VaultManifest manifest, string? password, string? keyfilePath)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath))
                throw new ArgumentException("Container path is required", nameof(containerPath));
            if (!File.Exists(containerPath))
                throw new FileNotFoundException("Container not found", containerPath);

            using var fs = new FileStream(containerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            // Read magic + version
            Span<byte> magic = stackalloc byte[HeaderMagicSize];
            fs.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic) != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            Span<byte> versionBuf = stackalloc byte[VersionFieldSize];
            fs.ReadExactly(versionBuf);
            int version = BitConverter.ToInt32(versionBuf);

            if (version >= 4)
            {
                UpdateManifestInContainerV4(fs, manifest, password, keyfilePath);
                return;
            }

            UpdateManifestInContainerV3(fs, version, manifest, password, keyfilePath);
        }

        private void UpdateManifestInContainerV4(FileStream fs, VaultManifest manifest, string? password, string? keyfilePath)
        {
            using var context = AuthenticateV4Container(fs, password, keyfilePath);
            WriteVaultManifestFooter(fs, context.FooterOffset, manifest, context.ContainerKey);
            fs.Flush();
        }

        private void UpdateManifestInContainerV3(FileStream fs, int version, VaultManifest manifest, string? password, string? keyfilePath)
        {
            // v3 header continuation: magic + version already consumed
            if (version < 3)
                throw new InvalidOperationException("Cannot update manifest in a v2 container — upgrade the container first");

            byte[] salt = new byte[SaltSize];
            fs.ReadExactly(salt);

            int iterations = DefaultIterations;
            if (version >= 2)
            {
                Span<byte> iterBuf = stackalloc byte[V3_IterationCountSize];
                fs.ReadExactly(iterBuf);
                iterations = BitConverter.ToInt32(iterBuf);
            }

            Span<byte> sizeBuf = stackalloc byte[V3_ContainerSizeSize];
            fs.ReadExactly(sizeBuf);
            // containerSize not needed for update

            long manifestOffset = 0;
            if (version >= 3)
            {
                Span<byte> manifestOffsetBuf = stackalloc byte[V3_ManifestOffsetSize];
                fs.ReadExactly(manifestOffsetBuf);
                manifestOffset = BitConverter.ToInt64(manifestOffsetBuf);
            }

            byte[] containerKey = DeriveContainerKey(password, keyfilePath, salt, iterations, DefaultMemoryKb);
            try
            {
                long writeOffset = manifestOffset > 0 ? manifestOffset : fs.Length;
                WriteVaultManifestFooter(fs, writeOffset, manifest, containerKey);

                // Backpatch manifest offset in header
                long offsetFieldPos = HeaderMagicSize + VersionFieldSize + SaltSize + V3_IterationCountSize + V3_ContainerSizeSize;
                fs.Seek(offsetFieldPos, SeekOrigin.Begin);
                fs.Write(BitConverter.GetBytes(writeOffset));

                fs.Flush();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(containerKey);
            }
        }

        private static int ValidateManifestSize(int manifestSize)
        {
            if (manifestSize <= 0 || manifestSize > 64 * 1024)
                throw new InvalidOperationException("Invalid container manifest size");

            return manifestSize;
        }

        private static byte[] ParsePayloadHash(string payloadHash)
        {
            byte[] parsedHash;
            try
            {
                parsedHash = Convert.FromBase64String(payloadHash);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Container payload hash is invalid", ex);
            }

            if (parsedHash.Length != 32)
                throw new InvalidOperationException("Container payload hash has an unexpected length");

            return parsedHash;
        }

        private static async ValueTask ReadAndValidateV4HeaderSizeAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] headerSizeBytes = new byte[V4_HeaderSizeFieldSize];
            await stream.ReadExactlyAsync(headerSizeBytes, cancellationToken).ConfigureAwait(false);
            int headerSize = BitConverter.ToInt32(headerSizeBytes);
            if (headerSize != V4_StaticHeaderTotalSize)
                throw new InvalidOperationException("Invalid v4 container header size");
        }

        private static void ReadAndValidateV4HeaderSize(Stream stream)
        {
            Span<byte> headerSizeBytes = stackalloc byte[V4_HeaderSizeFieldSize];
            stream.ReadExactly(headerSizeBytes);
            int headerSize = BitConverter.ToInt32(headerSizeBytes);
            if (headerSize != V4_StaticHeaderTotalSize)
                throw new InvalidOperationException("Invalid v4 container header size");
        }

        private static bool TryReadAndValidateV4HeaderSize(Stream stream)
        {
            try
            {
                ReadAndValidateV4HeaderSize(stream);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Note: EncryptionService is injected and should not be disposed here
            // as it may be shared across multiple consumers.
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhantomContainerService));
        }
    }
}
