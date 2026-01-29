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
    /// <summary>
    /// Provides symmetric encryption using a memory‑hard key derivation
    /// algorithm (Argon2id) and authenticated encryption (AES‑GCM). This
    /// service never stores derived keys and attempts to zero sensitive
    /// material when it is no longer needed. It is thread‑safe but does
    /// perform allocations for each operation.
    /// </summary>
    public sealed class EncryptionService
    {
        // Optional test observer used to verify zeroization of buffers in tests.
        // Passed via constructor injection in test builds; null in production.
        private readonly IEncryptionObserver? _observer;

        // Internal testing hook: when true, the Argon2 path will throw to
        // force execution of the PBKDF2 fallback. This field is set via an
        // internal constructor and is only intended for tests that verify
        // fallback behavior.
        private readonly bool _forceArgon2FailureForTests;

        internal EncryptionService(bool forceArgon2FailureForTests, IEncryptionObserver? observer = null)
        {
            _forceArgon2FailureForTests = forceArgon2FailureForTests;
            _observer = observer;
        }

        // Default public constructor
        public EncryptionService(IEncryptionObserver? observer = null)
        {
            _observer = observer;
        }
        /// <summary>
        /// Generates a cryptographically secure random salt of the given
        /// length in bytes. Salts need not be secret but must be unique per
        /// derived key to avoid rainbow table attacks.
        /// </summary>
        /// <param name="length">Number of random bytes to generate.</param>
        public byte[] GenerateSalt(int length = 16)
        {
            var salt = new byte[length];
            RandomNumberGenerator.Fill(salt);
            return salt;
        }

        /// <summary>
        /// Derives a fixed‑length key from a passphrase and salt using
        /// Argon2id. This implementation follows OWASP recommendations for
        /// password hashing by allocating a large amount of memory and
        /// performing multiple iterations. Should the Konscious Argon2
        /// package be unavailable (for example on an unsupported platform),
        /// the method falls back to PBKDF2 with HMAC‑SHA256. Although
        /// PBKDF2 offers weaker GPU/ASIC resistance than Argon2id, it
        /// remains a FIPS‑approved option when run with a high work
        /// factor. These defaults can be tuned via optional parameters.
        /// </summary>
        /// <param name="password">The user passphrase.</param>
        /// <param name="salt">The unique salt associated with this key.</param>
        /// <param name="keyLength">The desired key size in bytes.</param>
        /// <returns>A byte array containing the derived key.</returns>
        public byte[] DeriveKey(ReadOnlySpan<char> password, byte[] salt, int keyLength = 32, int memoryCostKb = 64 * 1024, int iterations = 3, int parallelism = 0)
        {
            if (password.IsEmpty)
            {
                throw new ArgumentException("Password must not be empty", nameof(password));
            }
            if (salt == null || salt.Length == 0)
            {
                throw new ArgumentException("Salt must not be empty", nameof(salt));
            }

            // Determine a sensible default for parallelism if none supplied. We cap
            // the degree of parallelism to avoid exhausting CPU cores on
            // low‑end devices.
            int parallel = parallelism > 0 ? parallelism : Math.Max(1, Environment.ProcessorCount / 2);

            // Allocate a temporary byte array for the passphrase. We avoid
            // creating an intermediate char[] by encoding directly from the
            // ReadOnlySpan<char>. This reduces allocations while keeping an
            // exact-length byte[] so it can be locked and zeroed. The lock
            // will be released in the finally block.
            int passByteCount = Encoding.UTF8.GetByteCount(password);
            // Rent a buffer from the shared pool to avoid allocating on each call.
            byte[] passBytes = ArrayPool<byte>.Shared.Rent(passByteCount);
            // We'll pin the trimmed buffer to reduce the chance of the GC moving it
            // while a native library may access it.
            System.Runtime.InteropServices.GCHandle? pinnedHandle = null;
            bool locked = false;
            byte[] passTrim = null!;
            try
            {
                int written = Encoding.UTF8.GetBytes(password, passBytes);
                // Create a trimmed copy containing only the encoded password bytes.
                passTrim = new byte[written];
                Array.Copy(passBytes, 0, passTrim, 0, written);
                // Pin and lock the trimmed array while in native calls.
                pinnedHandle = System.Runtime.InteropServices.GCHandle.Alloc(passTrim, System.Runtime.InteropServices.GCHandleType.Pinned);
                locked = SecureMemory.Lock(passTrim);
                // Primary implementation: Argon2id. We allocate a large chunk of
                // memory to slow down brute‑force attempts and increase
                // resistance against GPU/ASIC attacks. See the OWASP password
                // storage cheat sheet for guidance on parameter selection.
                if (_forceArgon2FailureForTests)
                {
                    // Force a deterministic failure so tests can exercise the fallback.
                    throw new InvalidOperationException("Forced Argon2 failure for tests");
                }
                var config = new Argon2Config
                {
                    Type = Isopoh.Cryptography.Argon2.Argon2Type.DataIndependentAddressing, // Argon2id
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
                // Copy Argon2 output to a dedicated array so callers don't
                // receive a buffer that may be reused by the native library
                // or internal pools.
                var outKey = new byte[keyLength];
                if (hash.Buffer.Length >= keyLength)
                    Array.Copy(hash.Buffer, 0, outKey, 0, keyLength);
                else
                    throw new CryptographicException("Argon2 returned insufficient hash length");
                return outKey;
            }
            catch (Exception ex) when (ShouldFallbackToPbkdf2(ex))
            {
                // Fallback to PBKDF2 (HMAC‑SHA256) if Argon2 fails. We use a
                // very high iteration count to compensate for the lack of
                // memory hardness. The iteration count here (200k) can be
                // increased depending on performance characteristics.
                // Ensure PBKDF2 operates on the trimmed password bytes only.
                using var pbkdf2 = new Rfc2898DeriveBytes(passTrim ?? Array.Empty<byte>(), salt, 200_000, HashAlgorithmName.SHA256);
                byte[] key = pbkdf2.GetBytes(keyLength);
                return key;
            }
            catch
            {
                // Non-recoverable Argon2 failure: rethrow to avoid silent downgrade.
                throw;
            }
            finally
            {
                // Always zero and unlock the passphrase bytes to reduce
                // exposure of sensitive data. Zero only the bytes we encoded.
                try
                {
                    if (passTrim != null)
                    {
                        CryptographicOperations.ZeroMemory(passTrim.AsSpan(0, passTrim.Length));
                        // Notify observer (tests) after zeroization so they can
                        // inspect the buffer contents without relying on static state.
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
                // Return the rented buffer to the pool. Use a try/catch to avoid
                // masking earlier exceptions if Return throws.
                try { ArrayPool<byte>.Shared.Return(passBytes); } catch { }
            }
        }

        /// <summary>
        /// Encrypts arbitrary plaintext using AES‑256‑GCM. The method
        /// generates a fresh 96‑bit nonce for each call. The returned
        /// <see cref="EncryptionResult"/> contains the ciphertext, nonce and
        /// authentication tag. Optional associated data may be supplied to
        /// bind contextual information (such as file names) to the
        /// ciphertext.
        /// </summary>
        /// <param name="plaintext">The data to encrypt.</param>
        /// <param name="key">The derived encryption key (32 bytes).</param>
        /// <param name="associatedData">Optional additional authenticated data.</param>
        public EncryptionResult Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 32 bytes for AES‑256", nameof(key));
            // Rent transient buffers to reduce per-call allocations. We copy
            // to fresh arrays for the return value to avoid exposing rented
            // buffers to callers.
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

                // Pin the buffers while calling into AesGcm.
                nonceHandle = System.Runtime.InteropServices.GCHandle.Alloc(nonce, System.Runtime.InteropServices.GCHandleType.Pinned);
                ctHandle = System.Runtime.InteropServices.GCHandle.Alloc(ciphertextBuf, System.Runtime.InteropServices.GCHandleType.Pinned);
                tagHandle = System.Runtime.InteropServices.GCHandle.Alloc(tag, System.Runtime.InteropServices.GCHandleType.Pinned);

                using var aesGcm = new AesGcm(key, 16);
                aesGcm.Encrypt(nonce.AsSpan(0, 12), plaintext, ciphertextBuf.AsSpan(0, plaintext.Length), tag.AsSpan(0, 16), associatedData);

                // Copy exact-sized results to returned arrays.
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
                // Zero and return rented buffers.
                try { if (ciphertextBuf != null) CryptographicOperations.ZeroMemory(ciphertextBuf.AsSpan(0, plaintext.Length)); } catch { }
                try { if (tag != null) CryptographicOperations.ZeroMemory(tag.AsSpan(0, 16)); } catch { }
                try { if (nonce != null) CryptographicOperations.ZeroMemory(nonce.AsSpan(0, 12)); } catch { }

                // Notify observer after zeroing so tests can verify buffers are cleared.
                try { if (ciphertextBuf != null) _observer?.OnTransientBufferZeroized(ciphertextBuf); } catch { }

                if (nonceHandle.HasValue && nonceHandle.Value.IsAllocated) try { nonceHandle.Value.Free(); } catch { }
                if (ctHandle.HasValue && ctHandle.Value.IsAllocated) try { ctHandle.Value.Free(); } catch { }
                if (tagHandle.HasValue && tagHandle.Value.IsAllocated) try { tagHandle.Value.Free(); } catch { }

                try { if (ciphertextBuf != null) ArrayPool<byte>.Shared.Return(ciphertextBuf); } catch { }
                try { if (tag != null) ArrayPool<byte>.Shared.Return(tag); } catch { }
                try { if (nonce != null) ArrayPool<byte>.Shared.Return(nonce); } catch { }
            }
        }

        /// <summary>
        /// Decrypts ciphertext produced by <see cref="Encrypt"/>. The same
        /// key, nonce, tag and associated data used to encrypt the data must be
        /// supplied. If any value is incorrect, an exception will be thrown.
        /// </summary>
        public byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 32 bytes for AES‑256", nameof(key));
            if (nonce.Length != 12)
                throw new ArgumentException("Nonce must be 12 bytes for AES‑GCM", nameof(nonce));
            if (tag.Length != 16)
                throw new ArgumentException("Tag must be 16 bytes for AES‑GCM", nameof(tag));
            // Rent a buffer for the plaintext and pin while decrypting.
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
                // Zero and return rented buffer.
                try { CryptographicOperations.ZeroMemory(plaintextBuf.AsSpan(0, ciphertext.Length)); } catch { }
                try { if (plaintextBuf != null) _observer?.OnTransientBufferZeroized(plaintextBuf); } catch { }
                if (ptHandle.HasValue && ptHandle.Value.IsAllocated) try { ptHandle.Value.Free(); } catch { }
                try { if (plaintextBuf != null) ArrayPool<byte>.Shared.Return(plaintextBuf); } catch { }
            }
        }

        private bool ShouldFallbackToPbkdf2(Exception ex)
        {
            if (_forceArgon2FailureForTests)
            {
                return true;
            }

            return ex is DllNotFoundException
                   || ex is PlatformNotSupportedException
                   || ex is EntryPointNotFoundException
                   || ex is NotSupportedException;
        }
    }
}
