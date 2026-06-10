using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{

    public class KeyfileGeneratorService
    {
        private const int DEFAULT_KEYFILE_SIZE_KB = 64;
        private const int MINIMUM_KEYFILE_SIZE_BYTES = 1024;
        private const int MAXIMUM_KEYFILE_SIZE_KB = 1024;

        public void GenerateKeyfile(string path, int sizeKB = DEFAULT_KEYFILE_SIZE_KB)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Keyfile path cannot be null or empty", nameof(path));

            if (sizeKB < 1)
                throw new ArgumentException("Keyfile size must be at least 1KB", nameof(sizeKB));

            if (sizeKB > MAXIMUM_KEYFILE_SIZE_KB)
                throw new ArgumentException($"Keyfile size cannot exceed {MAXIMUM_KEYFILE_SIZE_KB}KB", nameof(sizeKB));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            int sizeBytes = sizeKB * 1024;
            byte[] keyfileData = new byte[sizeBytes];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyfileData);
            }

            File.WriteAllBytes(path, keyfileData);

            var fileInfo = new FileInfo(path);
            fileInfo.IsReadOnly = true;
        }

        public async Task GenerateKeyfileAsync(string path, int sizeKB = DEFAULT_KEYFILE_SIZE_KB)
        {
            await Task.Run(() => GenerateKeyfile(path, sizeKB));
        }

        public bool ValidateKeyfile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!File.Exists(path))
                return false;

            try
            {
                var fileInfo = new FileInfo(path);

                if (fileInfo.Length < MINIMUM_KEYFILE_SIZE_BYTES)
                    return false;

                if (fileInfo.Length > MAXIMUM_KEYFILE_SIZE_KB * 1024)
                    return false;

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

        public void SecureDeleteKeyfile(string path, int overwritePasses = 3)
        {
            if (!File.Exists(path))
                return;

            var fileInfo = new FileInfo(path);
            long fileSize = fileInfo.Length;

            if (fileInfo.IsReadOnly)
                fileInfo.IsReadOnly = false;

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] randomData = new byte[4096];

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

            File.Delete(path);
        }

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

        public byte[] CombinePasswordAndKeyfile(byte[] password, string keyfilePath)
        {
            var keyfileBytes = ReadKeyfile(keyfilePath);
            if (keyfileBytes == null)
                throw new ArgumentException("Invalid keyfile", nameof(keyfilePath));

            using (var hmac = new HMACSHA256(password))
            {
                return hmac.ComputeHash(keyfileBytes);
            }
        }
    }

    public class KeyfileInfo
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int SizeKB { get; set; }
        public DateTime Created { get; set; }
        public bool IsReadOnly { get; set; }
    }
}

