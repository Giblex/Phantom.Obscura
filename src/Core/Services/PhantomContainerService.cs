using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Custom encrypted container format for PhantomVault.
    /// Provides VeraCrypt-like security without external dependencies.
    /// Format: [Header][Version][Salt][IterationCount][Size][Encrypted Metadata][Encrypted Data Blocks]
    /// </summary>
    public sealed class PhantomContainerService : IDisposable
    {
        private const string MagicHeader = "PHANTOM1";
        private const int HeaderSize = 8;
        private const int VersionSize = 4;
        private const int SaltSize = 32;
        private const int IterationCountSize = 4;
        private const int ContainerSizeSize = 8;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int BlockSize = 1024 * 1024; // 1MB blocks
        private const int DefaultIterations = 3; // Argon2 iterations
        private const int SecureWipePassCount = 3; // Number of overwrite passes for secure deletion

        private readonly EncryptionService _encryptionService;
        private bool _disposed;

        public PhantomContainerService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        /// <summary>
        /// Creates an encrypted container file with the specified size.
        /// </summary>
        public async Task CreateContainerAsync(
            string containerPath,
            long sizeBytes,
            string? password,
            string? keyfilePath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(containerPath))
                throw new ArgumentException("Container path is required", nameof(containerPath));
            if (sizeBytes <= 0)
                throw new ArgumentException("Container size must be positive", nameof(sizeBytes));

            // Generate random salt for this container
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            // Derive encryption key from password + keyfile using the random salt
            byte[] containerKey = await DeriveContainerKeyAsync(password, keyfilePath, salt, DefaultIterations);

            try
            {
                progress?.Report(0.0);

                // Create directory if needed
                var dir = Path.GetDirectoryName(containerPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var fileStream = new FileStream(containerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                // Write magic header
                await fileStream.WriteAsync(Encoding.ASCII.GetBytes(MagicHeader), cancellationToken);

                // Write version (2 - new format with iteration count)
                await fileStream.WriteAsync(BitConverter.GetBytes(2), cancellationToken);

                // Write salt for key derivation
                await fileStream.WriteAsync(salt, cancellationToken);

                // Write iteration count for future algorithm upgrades
                await fileStream.WriteAsync(BitConverter.GetBytes(DefaultIterations), cancellationToken);

                // Write container data size
                await fileStream.WriteAsync(BitConverter.GetBytes(sizeBytes), cancellationToken);
                
                progress?.Report(0.1);

                // Create encrypted metadata block
                var metadata = new
                {
                    Created = DateTime.UtcNow,
                    DataSize = sizeBytes,
                    BlockSize = BlockSize,
                    Iterations = DefaultIterations,
                    Checksum = ComputeHeaderChecksum(salt, sizeBytes, DefaultIterations)
                };
                
                string metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);
                byte[] metadataBytes = Encoding.UTF8.GetBytes(metadataJson);
                
                // Encrypt metadata
                var metadataEncrypted = _encryptionService.Encrypt(metadataBytes, containerKey);
                
                // Write encrypted metadata size, nonce, tag, then ciphertext
                await fileStream.WriteAsync(BitConverter.GetBytes(metadataEncrypted.Ciphertext.Length), cancellationToken);
                await fileStream.WriteAsync(metadataEncrypted.Nonce, cancellationToken);
                await fileStream.WriteAsync(metadataEncrypted.Tag, cancellationToken);
                await fileStream.WriteAsync(metadataEncrypted.Ciphertext, cancellationToken);
                
                progress?.Report(0.2);

                // Write encrypted zero blocks for the data area
                long totalBlocks = (sizeBytes + BlockSize - 1) / BlockSize;
                byte[] zeroBlock = new byte[Math.Min(BlockSize, sizeBytes)];
                
                for (long blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    int currentBlockSize = (int)Math.Min(BlockSize, sizeBytes - (blockIndex * BlockSize));
                    if (currentBlockSize < BlockSize)
                        zeroBlock = new byte[currentBlockSize];
                    
                    // Encrypt the zero block
                    byte[] blockNonce = new byte[NonceSize];
                    RandomNumberGenerator.Fill(blockNonce);
                    
                    var encryptedBlock = _encryptionService.Encrypt(
                        zeroBlock.AsSpan(0, currentBlockSize),
                        containerKey,
                        Encoding.UTF8.GetBytes($"block-{blockIndex}"));
                    
                    // Write block: nonce, tag, ciphertext
                    await fileStream.WriteAsync(encryptedBlock.Nonce, cancellationToken);
                    await fileStream.WriteAsync(encryptedBlock.Tag, cancellationToken);
                    await fileStream.WriteAsync(encryptedBlock.Ciphertext, cancellationToken);
                    
                    double progressValue = 0.2 + (0.8 * (blockIndex + 1) / totalBlocks);
                    progress?.Report(progressValue);
                }

                await fileStream.FlushAsync(cancellationToken);
                progress?.Report(1.0);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(containerKey);
            }
        }

        /// <summary>
        /// Opens and decrypts a container, writing the decrypted data to the target path.
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
            byte[] headerBytes = new byte[HeaderSize];
            await fileStream.ReadAsync(headerBytes, cancellationToken);
            string header = Encoding.ASCII.GetString(headerBytes);
            if (header != MagicHeader)
                throw new InvalidOperationException("Invalid container format");

            // Read version
            byte[] versionBytes = new byte[VersionSize];
            await fileStream.ReadAsync(versionBytes, cancellationToken);
            int version = BitConverter.ToInt32(versionBytes);
            if (version < 1 || version > 2)
                throw new InvalidOperationException($"Unsupported container version: {version}");

            // Read salt for key derivation
            byte[] salt = new byte[SaltSize];
            await fileStream.ReadAsync(salt, cancellationToken);

            // Read iteration count (version 2+) or use default for version 1
            int iterations = DefaultIterations;
            if (version >= 2)
            {
                byte[] iterationBytes = new byte[IterationCountSize];
                await fileStream.ReadAsync(iterationBytes, cancellationToken);
                iterations = BitConverter.ToInt32(iterationBytes);
            }

            // Read container size
            byte[] sizeBytes = new byte[ContainerSizeSize];
            await fileStream.ReadAsync(sizeBytes, cancellationToken);
            long containerSize = BitConverter.ToInt64(sizeBytes);

            // Derive key using the salt stored in the container
            byte[] containerKey = await DeriveContainerKeyAsync(password, keyfilePath, salt, iterations);

            try
            {
                // Read encrypted metadata
                byte[] metadataSizeBytes = new byte[4];
                await fileStream.ReadAsync(metadataSizeBytes, cancellationToken);
                int metadataSize = BitConverter.ToInt32(metadataSizeBytes);
                
                byte[] metadataNonce = new byte[NonceSize];
                await fileStream.ReadAsync(metadataNonce, cancellationToken);
                
                byte[] metadataTag = new byte[TagSize];
                await fileStream.ReadAsync(metadataTag, cancellationToken);
                
                byte[] metadataCiphertext = new byte[metadataSize];
                await fileStream.ReadAsync(metadataCiphertext, cancellationToken);
                
                // Decrypt metadata to verify password
                byte[] metadataPlaintext;
                try
                {
                    metadataPlaintext = _encryptionService.Decrypt(
                        metadataCiphertext,
                        metadataNonce,
                        metadataTag,
                        containerKey,
                        Array.Empty<byte>());
                }
                catch (CryptographicException)
                {
                    throw new UnauthorizedAccessException("Invalid password or keyfile");
                }
                
                // Create directory for target
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                
                // Decrypt data blocks to target file
                using var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                long totalBlocks = (containerSize + BlockSize - 1) / BlockSize;
                for (long blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Read encrypted block
                    byte[] blockNonce = new byte[NonceSize];
                    await fileStream.ReadAsync(blockNonce, cancellationToken);
                    
                    byte[] blockTag = new byte[TagSize];
                    await fileStream.ReadAsync(blockTag, cancellationToken);
                    
                    int currentBlockSize = (int)Math.Min(BlockSize, containerSize - (blockIndex * BlockSize));
                    // Account for encryption overhead
                    byte[] blockCiphertext = new byte[currentBlockSize];
                    await fileStream.ReadAsync(blockCiphertext, cancellationToken);
                    
                    // Decrypt block
                    byte[] blockPlaintext = _encryptionService.Decrypt(
                        blockCiphertext,
                        blockNonce,
                        blockTag,
                        containerKey,
                        Encoding.UTF8.GetBytes($"block-{blockIndex}"));
                    
                    // Write decrypted data
                    await outputStream.WriteAsync(blockPlaintext.AsMemory(0, Math.Min(blockPlaintext.Length, currentBlockSize)), cancellationToken);
                }
                
                await outputStream.FlushAsync(cancellationToken);
                return targetPath;
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

        private async Task<byte[]> DeriveContainerKeyAsync(string? password, string? keyfilePath, byte[] salt, int iterations)
        {
            byte[] passwordKey = Array.Empty<byte>();
            byte[] keyfileKey = Array.Empty<byte>();
            byte[] combinedKey = Array.Empty<byte>();

            try
            {
                // Derive key from password using Argon2id via EncryptionService
                if (!string.IsNullOrEmpty(password))
                {
                    passwordKey = _encryptionService.DeriveKey(password.AsSpan(), salt, 32, 64 * 1024, iterations);
                }
                else
                {
                    // Use a zero key if no password (keyfile-only mode)
                    passwordKey = new byte[32];
                }

                // Derive key from keyfile using HKDF
                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath);
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
