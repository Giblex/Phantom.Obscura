using System;
using System.IO;
using System.Security.Cryptography;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{
    /// <summary>
    /// Prototype file-backed key protector for non-Windows platforms.
    /// It stores a random envelope key in a file under the application's
    /// data folder and uses AES-GCM to wrap/unwrap small secrets. This is
    /// provided as a pragmatic fallback for platforms without DPAPI.
    /// </summary>
    internal class FileKeyProtector : IKeyProtector
    {
        private readonly string _keyPath;
        private readonly object _sync = new object();

        public FileKeyProtector(string keyPath)
        {
            _keyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
            EnsureKeyExists();
        }

        private void EnsureKeyExists()
        {
            lock (_sync)
            {
                if (File.Exists(_keyPath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_keyPath) ?? ".");
                using var rng = RandomNumberGenerator.Create();
                var key = new byte[32];
                rng.GetBytes(key);
                File.WriteAllBytes(_keyPath, key);
                // try to set restrictive permissions where possible (best-effort)
                try { File.SetAttributes(_keyPath, FileAttributes.ReadOnly); } catch { }
            }
        }

        private byte[] GetEnvelopeKey()
        {
            lock (_sync)
            {
                return File.ReadAllBytes(_keyPath);
            }
        }

        public byte[] Protect(byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            var key = GetEnvelopeKey();
            try
            {
                // Use ReadOnlySpan<byte> constructor with explicit tag size (16 bytes)
                using var aes = new AesGcm(key.AsSpan(), 16);
                var nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[16];
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
                using var ms = new MemoryStream();
                // format: nonce(12) | tag(16) | ciphertext
                ms.Write(nonce, 0, nonce.Length);
                ms.Write(tag, 0, tag.Length);
                ms.Write(ciphertext, 0, ciphertext.Length);
                return ms.ToArray();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public byte[] Unprotect(byte[] protectedBlob)
        {
            if (protectedBlob == null) throw new ArgumentNullException(nameof(protectedBlob));
            var key = GetEnvelopeKey();
            try
            {
                if (protectedBlob.Length < 28) throw new InvalidDataException("Invalid protected blob");
                var nonce = new byte[12];
                var tag = new byte[16];
                Buffer.BlockCopy(protectedBlob, 0, nonce, 0, 12);
                Buffer.BlockCopy(protectedBlob, 12, tag, 0, 16);
                var ciphertext = new byte[protectedBlob.Length - 28];
                Buffer.BlockCopy(protectedBlob, 28, ciphertext, 0, ciphertext.Length);
                // Use ReadOnlySpan<byte> constructor with explicit tag size (16 bytes)
                using var aes = new AesGcm(key.AsSpan(), 16);
                var plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
