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
                progress,
                cancellationToken
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens an encrypted container and extracts the vault file to a temporary location.
        /// Returns the path to the extracted vault file.
        /// </summary>
        public async Task<string> MountVaultAsync(string containerPath, string mountName, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);
            if (string.IsNullOrEmpty(mountName)) throw new ArgumentException("Mount name must be provided", nameof(mountName));

            // Create a temporary directory for this mount
            string tempDir = Path.Combine(Path.GetTempPath(), "PhantomVault", mountName);
            Directory.CreateDirectory(tempDir);
            
            // Extract the vault file from the container
            string vaultPath = Path.Combine(tempDir, "vault.pvault");
            await _containerService.OpenContainerAsync(containerPath, vaultPath, passphrase, keyfilePath, cancellationToken);
            
            return tempDir; // Return the directory path (like a mount point)
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
    }
}
