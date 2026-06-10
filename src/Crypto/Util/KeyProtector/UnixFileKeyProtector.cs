using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{

    [UnsupportedOSPlatform("windows")]
    internal sealed class UnixFileKeyProtector : IKeyProtector
    {
        private readonly string _keyPath;
        private readonly object _sync = new object();

        public UnixFileKeyProtector(string keyPath)
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
                var key = new byte[32];
                RandomNumberGenerator.Fill(key);
                try
                {
                    File.WriteAllBytes(_keyPath, key);

                    File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
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
                using var aes = new AesGcm(key.AsSpan(), 16);
                var nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[16];
                aes.Encrypt(nonce, plaintext, ciphertext, tag);

                using var ms = new MemoryStream(12 + 16 + ciphertext.Length);
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
            if (protectedBlob.Length < 28) throw new InvalidDataException("Protected blob is too short.");

            var key = GetEnvelopeKey();
            try
            {
                var nonce = new byte[12];
                var tag = new byte[16];
                Buffer.BlockCopy(protectedBlob, 0, nonce, 0, 12);
                Buffer.BlockCopy(protectedBlob, 12, tag, 0, 16);
                var ciphertext = new byte[protectedBlob.Length - 28];
                Buffer.BlockCopy(protectedBlob, 28, ciphertext, 0, ciphertext.Length);

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

