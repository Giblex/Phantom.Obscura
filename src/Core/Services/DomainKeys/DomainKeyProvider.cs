using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.DomainKeys
{

    public sealed class DomainKeyProvider : IDomainKeyProvider
    {

        private const string ObscuraDomainLabel = "phantom.obscura.v1";
        private const string AttestorDomainLabel = "phantom.attestor.v1";
        private const string RecoveryDomainLabel = "phantom.recovery.v1";

        private const int DomainKeyLength = 32;

        private byte[]? _obscuraKey;
        private byte[]? _attestorKey;
        private byte[]? _recoveryKey;

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

                    _obscuraKey = DeriveDomainKey(masterKey, salt, ObscuraDomainLabel);
                    _attestorKey = DeriveDomainKey(masterKey, salt, AttestorDomainLabel);
                    _recoveryKey = DeriveDomainKey(masterKey, salt, RecoveryDomainLabel);

                    _obscuraHandle = GCHandle.Alloc(_obscuraKey, GCHandleType.Pinned);
                    _attestorHandle = GCHandle.Alloc(_attestorKey, GCHandleType.Pinned);
                    _recoveryHandle = GCHandle.Alloc(_recoveryKey, GCHandleType.Pinned);

                    TryLockMemory(_obscuraKey);
                    TryLockMemory(_attestorKey);
                    TryLockMemory(_recoveryKey);

                    _isUnlocked = true;
                }
                finally
                {

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

        private static byte[] DeriveDomainKey(byte[] masterKey, byte[] salt, string domainLabel)
        {

            byte[] info = Encoding.UTF8.GetBytes(domainLabel);

            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey,
                DomainKeyLength,
                salt,
                info);
        }

        private static void TryLockMemory(byte[] buffer)
        {
            try
            {

                Utils.SecureMemory.Lock(buffer);
            }
            catch
            {

            }
        }

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

