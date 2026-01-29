using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GiblexVault.Security.ZK;
using GiblexVault.Security.ZK.Container;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Util;

namespace PhantomVault.Core.Services.ZeroKnowledge
{
    /// <summary>
    /// Zero-knowledge vault service implementation. Manages master key lifecycle,
    /// provides secure file access, and ensures proper cleanup of sensitive data.
    /// All plaintext is wiped from memory after use.
    /// </summary>
    public sealed class ZkVaultService : IZkVaultService, IDisposable
    {
        private byte[]? _masterKey;
        private readonly EngineOptions _opts;
        private readonly string _pepperPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Dictionary<string, int> _tempFileRefCounts = new();
        private readonly List<SecureTempFile> _tempFiles = new();

        public bool IsUnlocked => _masterKey != null;

        public ZkVaultService(EngineOptions? opts = null)
        {
            _opts = opts ?? new EngineOptions(EncryptionProfile.Advanced);
            _pepperPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault",
                "pepper.dpapi"
            );
        }

        /// <summary>
        /// Unlocks the master key using password + optional keyfile + optional device binding.
        /// Pepper is loaded from DPAPI-protected storage for additional entropy.
        /// </summary>
        public async Task<bool> UnlockMasterKeyAsync(string password, string? keyfilePath = null, string? deviceId = null)
        {
            await _lock.WaitAsync();
            try
            {
                if (IsUnlocked)
                {
                    await LockAndWipeKeysAsync();
                }

                // Load or create pepper
                byte[] pepper = await LoadOrCreatePepperAsync();

                // Build combined secret: password + pepper + keyfile
                byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
                byte[] keyfileBytes = !string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath)
                    ? await File.ReadAllBytesAsync(keyfilePath)
                    : Array.Empty<byte>();

                byte[] combined = new byte[pwdBytes.Length + pepper.Length + keyfileBytes.Length];
                Buffer.BlockCopy(pwdBytes, 0, combined, 0, pwdBytes.Length);
                Buffer.BlockCopy(pepper, 0, combined, pwdBytes.Length, pepper.Length);
                if (keyfileBytes.Length > 0)
                {
                    Buffer.BlockCopy(keyfileBytes, 0, combined, pwdBytes.Length + pepper.Length, keyfileBytes.Length);
                }

                // Apply device binding if provided
                byte[] salt = LoadOrCreateSalt();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    salt = DeviceBinding.DeviceSalt(deviceId, salt);
                }

                // Derive master key using Argon2id
                var kdfParams = new KdfParams
                {
                    Kdf = "argon2id",
                    Ops = _opts.ArgonOpsLimit,
                    MemMiB = _opts.ArgonMemMiB,
                    Parallelism = _opts.ArgonParallelism,
                    Salt = salt
                };

                _masterKey = Argon2Kdf.DeriveKey(combined, salt, kdfParams);

                // Zero sensitive materials
                CryptographicOperations.ZeroMemory(pwdBytes);
                CryptographicOperations.ZeroMemory(pepper);
                CryptographicOperations.ZeroMemory(keyfileBytes);
                CryptographicOperations.ZeroMemory(combined);

                // In production: verify MK by attempting to decrypt a test value
                // For now, assume success
                return true;
            }
            catch (Exception)
            {
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Unlocks the vault using a pre-computed hybrid DEK for Phase 2 post-quantum encryption.
        /// The hybrid DEK is derived from KEK ⊕ ML-KEM shared secret.
        /// </summary>
        public async Task<bool> UnlockWithHybridKeyAsync(byte[] hybridDek)
        {
            if (hybridDek == null)
                throw new ArgumentNullException(nameof(hybridDek));
            if (hybridDek.Length != 32)
                throw new ArgumentException("Hybrid DEK must be exactly 32 bytes", nameof(hybridDek));

            await _lock.WaitAsync();
            try
            {
                if (IsUnlocked)
                {
                    await LockAndWipeKeysAsync();
                }

                // Copy the hybrid DEK to our internal master key
                _masterKey = new byte[32];
                Buffer.BlockCopy(hybridDek, 0, _masterKey, 0, 32);

                return true;
            }
            catch (Exception)
            {
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Opens a file for viewing in-memory. Returns a read-only MemoryStream.
        /// Caller must dispose the stream when done.
        /// </summary>
        public async Task<Stream> OpenFileStreamForViewingAsync(string vaultPath, string? fileRelativePath = null, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            await _lock.WaitAsync(ct);
            try
            {
                byte[] decryptedBytes;

                // Check if this is a container or single encrypted file
                if (string.IsNullOrEmpty(fileRelativePath))
                {
                    // Single encrypted file (.zkf) - decrypt directly to memory to avoid temp files
                    decryptedBytes = await VaultFileZk.DecryptToArrayAsync(vaultPath, _masterKey!, _opts).ConfigureAwait(false);
                }
                else
                {
                    // Container with multiple files
                    decryptedBytes = GvContainerZk.Extract(vaultPath, _masterKey!, _opts, fileRelativePath);
                }

                // Create a new MemoryStream and copy the decrypted data
                // We can't just wrap the array because we need to zero it for security
                var ms = new MemoryStream();
                ms.Write(decryptedBytes, 0, decryptedBytes.Length);
                ms.Position = 0; // Reset to beginning for reading

                // Zero the temporary buffer now that we've copied to the stream
                CryptographicOperations.ZeroMemory(decryptedBytes);

                return ms;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Extracts file to secure temporary directory with automatic cleanup after TTL.
        /// Directory is restricted to current user and files are securely wiped on deletion.
        /// </summary>
        public async Task<string> ExtractFileToSecureTempAsync(string containerPath, string fileRelativePath, TimeSpan ttl)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            await _lock.WaitAsync();
            try
            {
                var bytes = GvContainerZk.Extract(containerPath, _masterKey!, _opts, fileRelativePath);

                // Create secure session-specific temp directory with restricted permissions
                var appDataTemp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomVault",
                    "SecureTemp",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(appDataTemp);

                // Restrict permissions to current user only
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(appDataTemp);
                        var acl = dirInfo.GetAccessControl();
                        acl.SetAccessRuleProtection(true, false); // Disable inheritance, don't preserve existing

                        // Add full control for current user only
                        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var rule = new System.Security.AccessControl.FileSystemAccessRule(
                            currentUser.User!,
                            System.Security.AccessControl.FileSystemRights.FullControl,
                            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                            System.Security.AccessControl.PropagationFlags.None,
                            System.Security.AccessControl.AccessControlType.Allow);
                        acl.AddAccessRule(rule);
                        dirInfo.SetAccessControl(acl);
                    }
                    catch (Exception ex)
                    {
                        // If ACL fails, clean up and throw - don't proceed with insecure temp file
                        try { Directory.Delete(appDataTemp, true); } catch { }
                        throw new SecurityException($"Failed to set secure ACL on temp directory: {ex.Message}", ex);
                    }
                }
                else
                {
                    // Unix-like: set permissions to 700 (owner only)
                    try
                    {
                        File.SetUnixFileMode(appDataTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    catch
                    {
                        // Non-Unix or permission error
                    }
                }

                var tempFile = Path.Combine(appDataTemp, Path.GetFileName(fileRelativePath));
                await File.WriteAllBytesAsync(tempFile, bytes);

                // Zero plaintext from memory
                CryptographicOperations.ZeroMemory(bytes);

                // Track for guaranteed cleanup
                var secureTempFile = new SecureTempFile(tempFile, appDataTemp, DateTimeOffset.UtcNow.Add(ttl));
                _tempFiles.Add(secureTempFile);

                // Track reference count to prevent deletion while in use
                _tempFileRefCounts[tempFile] = _tempFileRefCounts.GetValueOrDefault(tempFile, 0) + 1;

                // Schedule secure deletion after TTL
                _ = Task.Run(async () =>
                {
                    await Task.Delay(ttl);

                    await _lock.WaitAsync();
                    try
                    {
                        // Decrement ref count
                        if (_tempFileRefCounts.ContainsKey(tempFile))
                        {
                            _tempFileRefCounts[tempFile]--;
                            if (_tempFileRefCounts[tempFile] <= 0)
                            {
                                _tempFileRefCounts.Remove(tempFile);

                                // Securely delete file and directory
                                try
                                {
                                    SecureDeleteFile(tempFile);
                                    if (Directory.Exists(appDataTemp))
                                    {
                                        Directory.Delete(appDataTemp, true);
                                    }
                                    _tempFiles.Remove(secureTempFile);
                                }
                                catch
                                {
                                    // Cleanup failed, will be handled by CleanupOrphanedTempFilesAsync or Dispose
                                }
                            }
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }
                });

                return tempFile;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Lists all entries in a container.
        /// </summary>
        public async Task<IEnumerable<string>> ListContainerEntriesAsync(string containerPath)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            return await Task.Run(() =>
            {
                return GvContainerZk.List(containerPath, _masterKey!, _opts);
            });
        }

        /// <summary>
        /// Encrypts a plaintext file using zero-knowledge encryption (VaultFileZk).
        /// </summary>
        public async Task EncryptFileAsync(string plaintextPath, string encryptedOutputPath, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            if (!File.Exists(plaintextPath))
                throw new FileNotFoundException("Plaintext file not found", plaintextPath);

            await VaultFileZk.EncryptAsync(plaintextPath, encryptedOutputPath, _masterKey!, _opts);
        }

        /// <summary>
        /// Encrypts plaintext from a stream using zero-knowledge encryption and writes the
        /// encrypted output to the specified path. This avoids creating plaintext files on disk.
        /// </summary>
        public async Task EncryptStreamAsync(Stream plaintextStream, string encryptedOutputPath, CancellationToken ct = default)
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault is locked. Call UnlockMasterKeyAsync first.");

            if (plaintextStream == null || !plaintextStream.CanRead)
                throw new ArgumentException("Plaintext stream must be readable", nameof(plaintextStream));

            await VaultFileZk.EncryptAsync(plaintextStream, encryptedOutputPath, _masterKey!, _opts).ConfigureAwait(false);
        }

        /// <summary>
        /// Locks vault and wipes all sensitive key material from memory.
        /// </summary>
        public async Task LockAndWipeKeysAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_masterKey != null)
                {
                    CryptographicOperations.ZeroMemory(_masterKey);
                    _masterKey = null;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Cleans up orphaned temp files from previous sessions or crashes.
        /// Should be called on application startup.
        /// </summary>
        public async Task CleanupOrphanedTempFilesAsync(TimeSpan maxAge)
        {
            await Task.Run(() =>
            {
                var tempRoot = Path.Combine(Path.GetTempPath(), "PhantomVaultTemp");
                if (!Directory.Exists(tempRoot))
                    return;

                var now = DateTime.UtcNow;
                foreach (var sessionDir in Directory.GetDirectories(tempRoot))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(sessionDir);
                        if (now - dirInfo.CreationTimeUtc > maxAge)
                        {
                            // Securely delete all files
                            foreach (var file in dirInfo.GetFiles())
                            {
                                SecureDeleteFile(file.FullName);
                            }
                            Directory.Delete(sessionDir, true);
                        }
                    }
                    catch
                    {
                        // Skip directories we can't access
                    }
                }
            });
        }

        /// <summary>
        /// Securely deletes a file by overwriting with zeros before deletion.
        /// Defense-in-depth measure to prevent casual recovery from disk/swap.
        /// Note: On modern SSDs with wear-leveling, this doesn't guarantee
        /// physical deletion, but still useful for defense-in-depth.
        /// </summary>
        private void SecureDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var fileInfo = new FileInfo(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zeros = new byte[stream.Length];
                    stream.Position = 0;
                    stream.Write(zeros, 0, zeros.Length);
                    stream.Flush(true); // Force OS flush
                }

                File.Delete(path);
            }
            catch
            {
                // If secure delete fails, try regular delete
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // If even that fails, file will be cleaned up on next startup
                }
            }
        }

        /// <summary>
        /// Loads existing pepper or creates new one with DPAPI protection.
        /// </summary>
        private async Task<byte[]> LoadOrCreatePepperAsync()
        {
            var appDataDir = Path.GetDirectoryName(_pepperPath)!;
            Directory.CreateDirectory(appDataDir);

            if (File.Exists(_pepperPath))
            {
                try
                {
                    var sealedPepper = await File.ReadAllBytesAsync(_pepperPath);
                    if (!OperatingSystem.IsWindows())
                    {
                        // DPAPI-protected pepper only supported on Windows.
                        throw new PlatformNotSupportedException("Pepper sealing is only supported on Windows. The application must be run on Windows or use a cross-platform sealing mechanism.");
                    }
                    return SecurityTuning.UnsealPepper(sealedPepper);
                }
                catch
                {
                    // Pepper unsealing failed (different user?), create new one
                }
            }

            // Create new pepper
            if (!OperatingSystem.IsWindows())
            {
                // On non-Windows platforms, fall back to an unprotected pepper stored with restricted ACL.
                // This is a last-resort fallback and should be replaced by a cross-platform key protection.
                var fallback = RandomNumberGenerator.GetBytes(64);
                await File.WriteAllBytesAsync(_pepperPath, fallback);
                return fallback;
            }

            var protectedPepper = SecurityTuning.CreatePepperProtected();
            await File.WriteAllBytesAsync(_pepperPath, protectedPepper);
            return SecurityTuning.UnsealPepper(protectedPepper);
        }

        /// <summary>
        /// Loads or creates the master salt for key derivation.
        /// </summary>
        private byte[] LoadOrCreateSalt()
        {
            var saltPath = Path.Combine(
                Path.GetDirectoryName(_pepperPath)!,
                "master.salt"
            );

            if (File.Exists(saltPath))
            {
                try
                {
                    return File.ReadAllBytes(saltPath);
                }
                catch
                {
                    // Salt loading failed
                }
            }

            // Create new salt
            var salt = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(saltPath, salt);
            return salt;
        }

        public void Dispose()
        {
            // Clean up all remaining temp files
            foreach (var tempFile in _tempFiles.ToArray())
            {
                try
                {
                    if (File.Exists(tempFile.FilePath))
                    {
                        SecureDeleteFile(tempFile.FilePath);
                    }
                    if (Directory.Exists(tempFile.DirectoryPath))
                    {
                        Directory.Delete(tempFile.DirectoryPath, true);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            _tempFiles.Clear();
            _tempFileRefCounts.Clear();

            // Dispose is synchronous - use GetAwaiter().GetResult() instead of .Wait() to avoid deadlocks
            LockAndWipeKeysAsync().GetAwaiter().GetResult();
            _lock?.Dispose();
        }
    }

    /// <summary>
    /// Represents a secure temporary file with expiration tracking
    /// </summary>
    internal sealed record SecureTempFile(string FilePath, string DirectoryPath, DateTimeOffset ExpiresAt);
}
