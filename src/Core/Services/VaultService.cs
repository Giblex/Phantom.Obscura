using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PhantomVault.Core.Options;
using System.Linq;
using System.Text;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides functionality to create, mount and unmount encrypted
    /// container files using the custom PhantomContainer format.
    /// This eliminates external dependencies and provides reliable
    /// cross-platform encrypted container support.
    /// </summary>
    public sealed class VaultService
    {
        private readonly VaultOptions _options;
        private readonly PhantomContainerService _containerService;
        private readonly EncryptionService _encryptionService;

        public VaultService() : this(new VaultOptions(), new EncryptionService())
        {
        }

        public VaultService(VaultOptions options) : this(options, new EncryptionService())
        {
        }

        public VaultService(VaultOptions options, EncryptionService encryptionService)
        {
            _options = options;
            _encryptionService = encryptionService;
            _containerService = new PhantomContainerService(encryptionService);
        }

        /// <summary>
        /// Creates a new encrypted container file using PhantomContainer format.
        /// Because container creation may be long‑running, the method accepts 
        /// a progress callback and cancellation token.
        /// </summary>
        /// <param name="containerPath">Absolute path to the new container.</param>
        /// <param name="sizeBytes">Container size in bytes.</param>
        /// <param name="passphrase">User passphrase.</param>
        /// <param name="keyfilePath">Optional path to a keyfile.</param>
        /// <param name="progress">Progress reporting callback.</param>
        /// <param name="cancellationToken">Cancellation token to abort creation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateVaultAsync(string containerPath, long sizeBytes, string? passphrase, string? keyfilePath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(containerPath)) throw new ArgumentException("Container path must be provided", nameof(containerPath));
            if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

            // At least one authentication method (passphrase or keyfile) must be provided
            if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(keyfilePath))
            {
                throw new ArgumentException("Either a passphrase or keyfile must be provided");
            }

            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(containerPath)!);

            // Delete existing file if it exists (from previous failed attempts)
            if (File.Exists(containerPath))
            {
                File.Delete(containerPath);
            }

            // Use PhantomContainerService to create encrypted container
            await _containerService.CreateContainerAsync(
                containerPath,
                sizeBytes,
                passphrase,
                keyfilePath,
                null,
                progress,
                cancellationToken
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates container access and creates a logical session handle without
        /// extracting plaintext vault contents to the host filesystem.
        /// </summary>
        public async Task<string> MountVaultAsync(string containerPath, string mountName, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);
            if (string.IsNullOrEmpty(mountName)) throw new ArgumentException("Mount name must be provided", nameof(mountName));

            // Validate that the caller can decrypt the container without materializing plaintext on disk.
            await using (var validationStream = Stream.Null)
            {
                await _containerService.OpenContainerToStreamAsync(containerPath, validationStream, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false);
            }

            // Return a logical session handle for lifecycle tracking and cleanup.
            string sessionRoot = Path.Combine(Path.GetTempPath(), "PhantomVaultSessions");
            Directory.CreateDirectory(sessionRoot);
            string sessionPath = Path.Combine(sessionRoot, $"{SanitizeMountName(mountName)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionPath);
            return sessionPath;
        }

        /// <summary>
        /// Closes an opened container and cleans up temporary files.
        /// </summary>
        public async Task DismountVaultAsync(string mountPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(mountPath)) throw new ArgumentException("Mount path must be provided", nameof(mountPath));
            
            // Delete the temporary directory and all its contents
            if (Directory.Exists(mountPath))
            {
                try
                {
                    Directory.Delete(mountPath, recursive: true);
                }
                catch (Exception ex)
                {
                    // Log but don't throw - dismount should be best-effort
                    Debug.WriteLine($"Warning: Could not delete temp directory {mountPath}: {ex.Message}");
                }
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Opens the encrypted vault payload inside the container directly into memory.
        /// </summary>
        public async Task<Stream> OpenVaultPayloadStreamAsync(string containerPath, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);

            var payloadStream = new MemoryStream();
            await _containerService.OpenContainerToStreamAsync(containerPath, payloadStream, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false);
            payloadStream.Position = 0;
            return payloadStream;
        }

        /// <summary>
        /// Re-encrypts a vault payload back into its container without creating plaintext files on disk.
        /// </summary>
        public async Task SaveVaultPayloadAsync(string containerPath, Stream payloadStream, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(containerPath)) throw new ArgumentException("Container path must be provided", nameof(containerPath));
            if (payloadStream == null || !payloadStream.CanRead) throw new ArgumentException("Payload stream must be readable", nameof(payloadStream));

            long existingPayloadSize = File.Exists(containerPath)
                ? await _containerService.GetPayloadSizeAsync(containerPath, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false)
                : payloadStream.Length;

            if (payloadStream.CanSeek)
            {
                payloadStream.Position = 0;
            }

            long requiredPayloadSize = payloadStream.CanSeek ? payloadStream.Length : existingPayloadSize;
            long finalPayloadSize = Math.Max(existingPayloadSize, requiredPayloadSize);
            string tempPath = Path.Combine(Path.GetDirectoryName(containerPath) ?? AppContext.BaseDirectory, $"{Path.GetFileName(containerPath)}.{Guid.NewGuid():N}.tmp");

            await _containerService.CreateContainerFromStreamAsync(
                tempPath,
                payloadStream,
                finalPayloadSize,
                passphrase,
                keyfilePath,
                manifest: null,
                progress: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (File.Exists(containerPath))
            {
                File.Delete(containerPath);
            }

            File.Move(tempPath, containerPath);
        }

        private static string SanitizeMountName(string mountName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(mountName.Length);
            foreach (char ch in mountName)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.Length == 0 ? "vault" : builder.ToString();
        }
    }
}
