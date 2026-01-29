using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Service for generating cryptographically secure keyfiles for vault protection.
    /// Keyfiles add an additional layer of security beyond passwords.
    /// </summary>
    public class KeyfileGeneratorService
    {
        private const int DEFAULT_KEYFILE_SIZE_KB = 64;
        private const int MINIMUM_KEYFILE_SIZE_BYTES = 1024; // 1KB minimum
        private const int MAXIMUM_KEYFILE_SIZE_KB = 1024; // 1MB maximum

        /// <summary>
        /// Generate a cryptographically secure keyfile with random data.
        /// </summary>
        /// <param name="path">File path where keyfile will be saved</param>
        /// <param name="sizeKB">Size of keyfile in kilobytes (default: 64KB)</param>
        /// <exception cref="ArgumentException">If path is invalid or size is out of range</exception>
        /// <exception cref="IOException">If file cannot be written</exception>
        public void GenerateKeyfile(string path, int sizeKB = DEFAULT_KEYFILE_SIZE_KB)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Keyfile path cannot be null or empty", nameof(path));

            if (sizeKB < 1)
                throw new ArgumentException("Keyfile size must be at least 1KB", nameof(sizeKB));

            if (sizeKB > MAXIMUM_KEYFILE_SIZE_KB)
                throw new ArgumentException($"Keyfile size cannot exceed {MAXIMUM_KEYFILE_SIZE_KB}KB", nameof(sizeKB));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Generate cryptographically secure random bytes
            int sizeBytes = sizeKB * 1024;
            byte[] keyfileData = new byte[sizeBytes];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyfileData);
            }

            // Write to file with restrictive permissions
            File.WriteAllBytes(path, keyfileData);

            // Set file to read-only for additional protection
            var fileInfo = new FileInfo(path);
            fileInfo.IsReadOnly = true;
        }

        /// <summary>
        /// Generate a keyfile asynchronously.
        /// </summary>
        public async Task GenerateKeyfileAsync(string path, int sizeKB = DEFAULT_KEYFILE_SIZE_KB)
        {
            await Task.Run(() => GenerateKeyfile(path, sizeKB));
        }

        /// <summary>
        /// Validate that a file meets keyfile requirements.
        /// </summary>
        /// <param name="path">Path to keyfile to validate</param>
        /// <returns>True if file is valid keyfile, false otherwise</returns>
        public bool ValidateKeyfile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!File.Exists(path))
                return false;

            try
            {
                var fileInfo = new FileInfo(path);

                // Check minimum size requirement
                if (fileInfo.Length < MINIMUM_KEYFILE_SIZE_BYTES)
                    return false;

                // Check maximum size requirement
                if (fileInfo.Length > MAXIMUM_KEYFILE_SIZE_KB * 1024)
                    return false;

                // Verify file is readable
                using (var fs = File.OpenRead(path))
                {
                    return fs.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get information about a keyfile without reading its contents.
        /// </summary>
        /// <param name="path">Path to keyfile</param>
        /// <returns>Keyfile information or null if invalid</returns>
        public KeyfileInfo? GetKeyfileInfo(string path)
        {
            if (!ValidateKeyfile(path))
                return null;

            var fileInfo = new FileInfo(path);
            return new KeyfileInfo
            {
                Path = path,
                SizeBytes = fileInfo.Length,
                SizeKB = (int)(fileInfo.Length / 1024),
                Created = fileInfo.CreationTimeUtc,
                IsReadOnly = fileInfo.IsReadOnly
            };
        }

        /// <summary>
        /// Securely delete a keyfile by overwriting with random data before deletion.
        /// </summary>
        /// <param name="path">Path to keyfile to delete</param>
        /// <param name="overwritePasses">Number of overwrite passes (default: 3)</param>
        public void SecureDeleteKeyfile(string path, int overwritePasses = 3)
        {
            if (!File.Exists(path))
                return;

            var fileInfo = new FileInfo(path);
            long fileSize = fileInfo.Length;

            // Remove read-only flag if set
            if (fileInfo.IsReadOnly)
                fileInfo.IsReadOnly = false;

            // Overwrite file with random data multiple times
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] randomData = new byte[4096]; // 4KB buffer

                for (int pass = 0; pass < overwritePasses; pass++)
                {
                    using (var fs = File.OpenWrite(path))
                    {
                        fs.SetLength(fileSize);
                        long remaining = fileSize;

                        while (remaining > 0)
                        {
                            int bytesToWrite = (int)Math.Min(randomData.Length, remaining);
                            rng.GetBytes(randomData);
                            fs.Write(randomData, 0, bytesToWrite);
                            remaining -= bytesToWrite;
                        }

                        fs.Flush(flushToDisk: true);
                    }
                }
            }

            // Finally delete the file
            File.Delete(path);
        }

        /// <summary>
        /// Read keyfile bytes for use in encryption.
        /// </summary>
        /// <param name="path">Path to keyfile</param>
        /// <returns>Keyfile bytes or null if invalid</returns>
        public byte[]? ReadKeyfile(string path)
        {
            if (!ValidateKeyfile(path))
                return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Combine keyfile with password for enhanced security.
        /// </summary>
        /// <param name="password">User password</param>
        /// <param name="keyfilePath">Path to keyfile</param>
        /// <returns>Combined authentication data</returns>
        public byte[] CombinePasswordAndKeyfile(byte[] password, string keyfilePath)
        {
            var keyfileBytes = ReadKeyfile(keyfilePath);
            if (keyfileBytes == null)
                throw new ArgumentException("Invalid keyfile", nameof(keyfilePath));

            // Combine using HMAC-SHA256
            using (var hmac = new HMACSHA256(password))
            {
                return hmac.ComputeHash(keyfileBytes);
            }
        }
    }

    /// <summary>
    /// Information about a keyfile.
    /// </summary>
    public class KeyfileInfo
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int SizeKB { get; set; }
        public DateTime Created { get; set; }
        public bool IsReadOnly { get; set; }
    }
}
