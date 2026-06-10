using System;
using System.Security;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{

    public sealed class EncryptionService
    {

        private readonly IEncryptionObserver? _observer;

        public EncryptionService(IEncryptionObserver? observer = null)
        {
            _observer = observer;
        }

        public byte[] GenerateSalt(int length = 16)
        {
            var salt = new byte[length];
            RandomNumberGenerator.Fill(salt);
            return salt;
        }

        public byte[] DeriveKey(ReadOnlySpan<char> password, byte[] salt, int keyLength = 32, int memoryCostKb = 256 * 1024, int iterations = 6, int parallelism = 0)
        {
            if (password.IsEmpty)
            {
                throw new ArgumentException("Password must not be empty", nameof(password));
            }
            if (salt == null || salt.Length == 0)
            {
                throw new ArgumentException("Salt must not be empty", nameof(salt));
            }

            int parallel = parallelism > 0 ? parallelism : Math.Max(1, Environment.ProcessorCount / 2);

            int passByteCount = Encoding.UTF8.GetByteCount(password);

            byte[] passBytes = ArrayPool<byte>.Shared.Rent(passByteCount);

            System.Runtime.InteropServices.GCHandle? pinnedHandle = null;
            bool locked = false;
            byte[] passTrim = null!;
            try
            {
                int written = Encoding.UTF8.GetBytes(password, passBytes);

                passTrim = new byte[written];
                Array.Copy(passBytes, 0, passTrim, 0, written);

                pinnedHandle = System.Runtime.InteropServices.GCHandle.Alloc(passTrim, System.Runtime.InteropServices.GCHandleType.Pinned);
                locked = SecureMemory.Lock(passTrim);

                var config = new Argon2Config
                {
                    Type = Isopoh.Cryptography.Argon2.Argon2Type.HybridAddressing,
                    Version = Argon2Version.Nineteen,
                    TimeCost = iterations,
                    MemoryCost = memoryCostKb,
                    Lanes = parallel,
                    Threads = parallel,
                    Password = passTrim,
                    Salt = salt,
                    HashLength = keyLength
                };
                using var argon2 = new Argon2(config);
                using var hash = argon2.Hash();

                var outKey = new byte[keyLength];
                if (hash.Buffer.Length >= keyLength)
                    Array.Copy(hash.Buffer, 0, outKey, 0, keyLength);
                else
                    throw new CryptographicException("Argon2 returned insufficient hash length");
                return outKey;
            }

            finally
            {

                try
                {
                    if (passTrim != null)
                    {
                        CryptographicOperations.ZeroMemory(passTrim.AsSpan(0, passTrim.Length));

                        try { _observer?.OnPasswordBufferZeroized(passTrim); } catch { }
                    }
                }
                catch { }
                if (locked)
                {
                    try { SecureMemory.Unlock(passTrim); } catch { }
                }
                if (pinnedHandle.HasValue && pinnedHandle.Value.IsAllocated)
                {
                    try { pinnedHandle.Value.Free(); } catch { }
                }

                try { ArrayPool<byte>.Shared.Return(passBytes, clearArray: true); } catch { }
            }
        }

        public EncryptionResult Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 32 bytes for AES‑256", nameof(key));

            byte[] nonce = ArrayPool<byte>.Shared.Rent(12);
            byte[] ciphertextBuf = ArrayPool<byte>.Shared.Rent(plaintext.Length);
            byte[] tag = ArrayPool<byte>.Shared.Rent(16);

            System.Runtime.InteropServices.GCHandle? nonceHandle = null;
            System.Runtime.InteropServices.GCHandle? ctHandle = null;
            System.Runtime.InteropServices.GCHandle? tagHandle = null;
            try
            {
                try
                {
                    RandomNumberGenerator.Fill(nonce);
                }
                catch (Exception ex)
                {
                    throw new CryptographicException("Failed to generate cryptographically secure nonce for AES-GCM.", ex);
                }

                nonceHandle = System.Runtime.InteropServices.GCHandle.Alloc(nonce, System.Runtime.InteropServices.GCHandleType.Pinned);
                ctHandle = System.Runtime.InteropServices.GCHandle.Alloc(ciphertextBuf, System.Runtime.InteropServices.GCHandleType.Pinned);
                tagHandle = System.Runtime.InteropServices.GCHandle.Alloc(tag, System.Runtime.InteropServices.GCHandleType.Pinned);

                using var aesGcm = new AesGcm(key, 16);
                aesGcm.Encrypt(nonce.AsSpan(0, 12), plaintext, ciphertextBuf.AsSpan(0, plaintext.Length), tag.AsSpan(0, 16), associatedData);

                var ciphertext = new byte[plaintext.Length];
                Array.Copy(ciphertextBuf, 0, ciphertext, 0, plaintext.Length);
                var nonceOut = new byte[12];
                Array.Copy(nonce, 0, nonceOut, 0, 12);
                var tagOut = new byte[16];
                Array.Copy(tag, 0, tagOut, 0, 16);

                return new EncryptionResult(ciphertext, nonceOut, tagOut);
            }
            finally
            {

                try { if (ciphertextBuf != null) CryptographicOperations.ZeroMemory(ciphertextBuf); } catch { }
                try { if (tag != null) CryptographicOperations.ZeroMemory(tag.AsSpan(0, 16)); } catch { }
                try { if (nonce != null) CryptographicOperations.ZeroMemory(nonce.AsSpan(0, 12)); } catch { }

                try { if (ciphertextBuf != null) _observer?.OnTransientBufferZeroized(ciphertextBuf); } catch { }

                if (nonceHandle.HasValue && nonceHandle.Value.IsAllocated) try { nonceHandle.Value.Free(); } catch { }
                if (ctHandle.HasValue && ctHandle.Value.IsAllocated) try { ctHandle.Value.Free(); } catch { }
                if (tagHandle.HasValue && tagHandle.Value.IsAllocated) try { tagHandle.Value.Free(); } catch { }

                try { if (ciphertextBuf != null) ArrayPool<byte>.Shared.Return(ciphertextBuf, clearArray: true); } catch { }
                try { if (tag != null) ArrayPool<byte>.Shared.Return(tag, clearArray: true); } catch { }
                try { if (nonce != null) ArrayPool<byte>.Shared.Return(nonce, clearArray: true); } catch { }
            }
        }

        public byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 32 bytes for AES‑256", nameof(key));
            if (nonce.Length != 12)
                throw new ArgumentException("Nonce must be 12 bytes for AES‑GCM", nameof(nonce));
            if (tag.Length != 16)
                throw new ArgumentException("Tag must be 16 bytes for AES‑GCM", nameof(tag));

            byte[] plaintextBuf = ArrayPool<byte>.Shared.Rent(ciphertext.Length);
            System.Runtime.InteropServices.GCHandle? ptHandle = null;
            try
            {
                ptHandle = System.Runtime.InteropServices.GCHandle.Alloc(plaintextBuf, System.Runtime.InteropServices.GCHandleType.Pinned);
                using var aesGcm = new AesGcm(key, 16);
                aesGcm.Decrypt(nonce.Slice(0, 12), ciphertext, tag.Slice(0, 16), plaintextBuf.AsSpan(0, ciphertext.Length), associatedData);

                var outPlain = new byte[ciphertext.Length];
                Array.Copy(plaintextBuf, 0, outPlain, 0, ciphertext.Length);
                return outPlain;
            }
            finally
            {

                try { CryptographicOperations.ZeroMemory(plaintextBuf); } catch { }
                try { if (plaintextBuf != null) _observer?.OnTransientBufferZeroized(plaintextBuf); } catch { }
                if (ptHandle.HasValue && ptHandle.Value.IsAllocated) try { ptHandle.Value.Free(); } catch { }
                try { if (plaintextBuf != null) ArrayPool<byte>.Shared.Return(plaintextBuf, clearArray: true); } catch { }
            }
        }

    }
}

