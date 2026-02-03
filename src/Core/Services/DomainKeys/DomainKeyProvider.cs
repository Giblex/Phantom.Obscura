using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Provides cryptographically isolated domain keys using HKDF derivation.
    ///
    /// Security model:
    /// - Master key derived from password + salt via Argon2id (external)
    /// - Master key passed to Initialize(), domain keys derived, master zeroed
    /// - Each domain gets: HKDF-SHA256(master, salt, "phantom.{domain}.v1")
    /// - Domain keys are memory-locked and zeroed on disposal/lock
    ///
    /// This is the foundation for domain separation. Even without process isolation,
    /// services can only access their designated domain key.
    /// </summary>
    public sealed class DomainKeyProvider : IDomainKeyProvider
    {
        // Domain label versions - increment on breaking changes to force re-derivation
        private const string ObscuraDomainLabel = "phantom.obscura.v1";
        private const string AttestorDomainLabel = "phantom.attestor.v1";
        private const string RecoveryDomainLabel = "phantom.recovery.v1";

        private const int DomainKeyLength = 32; // 256 bits

        // Domain keys - pinned and locked in memory
        private byte[]? _obscuraKey;
        private byte[]? _attestorKey;
        private byte[]? _recoveryKey;

        // GC handles to prevent memory movement
        private GCHandle _obscuraHandle;
        private GCHandle _attestorHandle;
        private GCHandle _recoveryHandle;

        private bool _isUnlocked;
        private bool _disposed;
        private readonly object _lock = new();

        public bool IsUnlocked
        {
            get
            {
                lock (_lock)
                {
                    return _isUnlocked && !_disposed;
                }
            }
        }

        /// <summary>
        /// Initializes the provider by deriving domain keys from the master key.
        /// The master key is zeroed immediately after derivation.
        /// </summary>
        /// <param name="masterKey">Master key from Argon2id (will be zeroed)</param>
        /// <param name="salt">Salt for HKDF (typically from manifest)</param>
        public void Initialize(byte[] masterKey, byte[] salt)
        {
            if (masterKey == null || masterKey.Length < 32)
                throw new ArgumentException("Master key must be at least 32 bytes", nameof(masterKey));
            if (salt == null || salt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

            lock (_lock)
            {
                ThrowIfDisposed();

                if (_isUnlocked)
                    throw new InvalidOperationException("Provider is already initialized. Call Lock() first.");

                try
                {
                    // Derive domain keys using HKDF-SHA256
                    _obscuraKey = DeriveDomainKey(masterKey, salt, ObscuraDomainLabel);
                    _attestorKey = DeriveDomainKey(masterKey, salt, AttestorDomainLabel);
                    _recoveryKey = DeriveDomainKey(masterKey, salt, RecoveryDomainLabel);

                    // Pin keys to prevent GC movement
                    _obscuraHandle = GCHandle.Alloc(_obscuraKey, GCHandleType.Pinned);
                    _attestorHandle = GCHandle.Alloc(_attestorKey, GCHandleType.Pinned);
                    _recoveryHandle = GCHandle.Alloc(_recoveryKey, GCHandleType.Pinned);

                    // Lock memory pages (best effort)
                    TryLockMemory(_obscuraKey);
                    TryLockMemory(_attestorKey);
                    TryLockMemory(_recoveryKey);

                    _isUnlocked = true;
                }
                finally
                {
                    // CRITICAL: Zero the master key immediately
                    // Domain keys are now the only way to access domain data
                    CryptographicOperations.ZeroMemory(masterKey);
                }
            }
        }

        public ReadOnlySpan<byte> GetObscuraKey()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                ThrowIfNotUnlocked();
                return _obscuraKey.AsSpan();
            }
        }

        public ReadOnlySpan<byte> GetAttestorKey()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                ThrowIfNotUnlocked();
                return _attestorKey.AsSpan();
            }
        }

        public ReadOnlySpan<byte> GetRecoveryKey()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                ThrowIfNotUnlocked();
                return _recoveryKey.AsSpan();
            }
        }

        public void Lock()
        {
            lock (_lock)
            {
                if (!_isUnlocked)
                    return;

                ZeroAndFreeKey(ref _obscuraKey, ref _obscuraHandle);
                ZeroAndFreeKey(ref _attestorKey, ref _attestorHandle);
                ZeroAndFreeKey(ref _recoveryKey, ref _recoveryHandle);

                _isUnlocked = false;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                Lock();
                _disposed = true;
            }
        }

        /// <summary>
        /// Derives a domain-specific key using HKDF-SHA256.
        /// </summary>
        private static byte[] DeriveDomainKey(byte[] masterKey, byte[] salt, string domainLabel)
        {
            // HKDF: Extract-then-Expand
            // info = domain label ensures keys are cryptographically independent
            byte[] info = Encoding.UTF8.GetBytes(domainLabel);

            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey,
                DomainKeyLength,
                salt,
                info);
        }

        /// <summary>
        /// Attempts to lock memory pages to prevent swapping to disk.
        /// Best effort - may fail on some platforms.
        /// </summary>
        private static void TryLockMemory(byte[] buffer)
        {
            try
            {
                // Use SecureMemory if available, otherwise skip
                // This prevents the key from being written to swap/pagefile
                Utils.SecureMemory.Lock(buffer);
            }
            catch
            {
                // Best effort - continue without memory locking
            }
        }

        /// <summary>
        /// Zeros a key buffer and frees its GC handle.
        /// </summary>
        private static void ZeroAndFreeKey(ref byte[]? key, ref GCHandle handle)
        {
            if (key != null)
            {
                try
                {
                    Utils.SecureMemory.Unlock(key);
                }
                catch
                {
                    // Ignore unlock failures
                }

                CryptographicOperations.ZeroMemory(key);
                key = null;
            }

            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DomainKeyProvider));
        }

        private void ThrowIfNotUnlocked()
        {
            if (!_isUnlocked)
                throw new InvalidOperationException("Domain keys not available. Call Initialize() first.");
        }
    }
}
