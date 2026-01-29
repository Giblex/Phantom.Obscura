using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.ZeroKnowledge
{
    /// <summary>
    /// Zero-knowledge vault service interface providing secure file access
    /// with automatic key management and secure cleanup. All cryptographic
    /// operations are abstracted away from UI code.
    /// </summary>
    public interface IZkVaultService
    {
        /// <summary>
        /// Unlocks the master key using the provided credentials.
        /// Must be called before any file operations.
        /// </summary>
        /// <param name="password">User password</param>
        /// <param name="keyfilePath">Optional path to keyfile</param>
        /// <param name="deviceId">Optional device binding identifier</param>
        /// <returns>True if unlock successful, false otherwise</returns>
        Task<bool> UnlockMasterKeyAsync(string password, string? keyfilePath = null, string? deviceId = null);

        /// <summary>
        /// Unlocks the vault using a pre-computed hybrid DEK (Data Encryption Key).
        /// This is used for Phase 2 post-quantum hybrid encryption where the DEK
        /// is derived from KEK ⊕ ML-KEM shared secret.
        /// </summary>
        /// <param name="hybridDek">Pre-computed hybrid DEK (32 bytes)</param>
        /// <returns>True if unlock successful, false otherwise</returns>
        Task<bool> UnlockWithHybridKeyAsync(byte[] hybridDek);

        /// <summary>
        /// Opens a file from the vault container as a read-only memory stream.
        /// Suitable for viewing files in-memory without writing to disk.
        /// </summary>
        /// <param name="vaultPath">Path to vault container or encrypted file</param>
        /// <param name="fileRelativePath">Entry name within container, or null for single files</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Read-only memory stream containing decrypted file data</returns>
        Task<Stream> OpenFileStreamForViewingAsync(string vaultPath, string? fileRelativePath = null, CancellationToken ct = default);

        /// <summary>
        /// Extracts a file from the vault to a secure temporary location.
        /// File is automatically deleted after TTL expires. Temp directory
        /// is restricted to current user and contents are securely wiped.
        /// </summary>
        /// <param name="containerPath">Path to vault container</param>
        /// <param name="fileRelativePath">Entry name within container</param>
        /// <param name="ttl">Time-to-live before automatic deletion</param>
        /// <returns>Path to temporary file that can be opened externally</returns>
        Task<string> ExtractFileToSecureTempAsync(string containerPath, string fileRelativePath, TimeSpan ttl);

        /// <summary>
        /// Lists all entries in a vault container.
        /// </summary>
        /// <param name="containerPath">Path to vault container file</param>
        /// <returns>Collection of entry names</returns>
        Task<IEnumerable<string>> ListContainerEntriesAsync(string containerPath);

        /// <summary>
        /// Encrypts a plaintext file using zero-knowledge encryption.
        /// Requires vault to be unlocked first. Uses VaultFileZk for single-file encryption.
        /// </summary>
        /// <param name="plaintextPath">Path to plaintext file to encrypt</param>
        /// <param name="encryptedOutputPath">Path where encrypted file should be written</param>
        /// <param name="ct">Cancellation token</param>
        Task EncryptFileAsync(string plaintextPath, string encryptedOutputPath, CancellationToken ct = default);

        /// <summary>
        /// Encrypts plaintext from a stream using zero-knowledge encryption and writes
        /// the encrypted output to <paramref name="encryptedOutputPath"/>. This avoids
        /// creating a plaintext file on disk and is preferred for in-memory data.
        /// </summary>
        /// <param name="plaintextStream">Stream containing plaintext data (readable, positioned at start)</param>
        /// <param name="encryptedOutputPath">Path where encrypted file should be written</param>
        /// <param name="ct">Cancellation token</param>
        Task EncryptStreamAsync(Stream plaintextStream, string encryptedOutputPath, CancellationToken ct = default);

        /// <summary>
        /// Locks the vault and securely wipes all master keys and sensitive
        /// data from memory. Should be called on logout or app close.
        /// </summary>
        Task LockAndWipeKeysAsync();

        /// <summary>
        /// Gets whether the vault is currently unlocked.
        /// </summary>
        bool IsUnlocked { get; }

        /// <summary>
        /// Cleans up orphaned temporary files from previous sessions.
        /// Should be called on application startup.
        /// </summary>
        Task CleanupOrphanedTempFilesAsync(TimeSpan maxAge);
    }
}
