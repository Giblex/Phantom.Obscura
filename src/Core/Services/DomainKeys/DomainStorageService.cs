using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Service for reading/writing domain-specific encrypted stores.
    ///
    /// Each domain has its own encrypted store file:
    /// - obscura.store.encrypted → encrypted with K_obscura
    /// - attestor.store.encrypted → encrypted with K_attestor
    /// - recovery.store.encrypted → encrypted with K_recovery
    ///
    /// Storage separation ensures:
    /// - Different keys for each domain
    /// - Different files (can't accidentally mix)
    /// - Different parsers (type safety)
    ///
    /// This makes later process isolation trivial since each process
    /// only needs its own domain key.
    /// </summary>
    public sealed class DomainStorageService : IDisposable
    {
        private const string ObscuraStoreFilename = "obscura.store.encrypted";
        private const string AttestorStoreFilename = "attestor.store.encrypted";
        private const string RecoveryStoreFilename = "recovery.store.encrypted";

        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly IDomainKeyProvider _keyProvider;
        private readonly EncryptionService _encryptionService;
        private bool _disposed;

        public DomainStorageService(IDomainKeyProvider keyProvider, EncryptionService encryptionService)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        #region Obscura Store

        /// <summary>
        /// Loads the Obscura store from the specified USB path.
        /// </summary>
        public async Task<ObscuraStore?> LoadObscuraStoreAsync(
            string usbRootPath,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            var filePath = Path.Combine(usbRootPath, "obscura", ObscuraStoreFilename);
            if (!File.Exists(filePath))
                return null;

            return await LoadStoreAsync<ObscuraStore>(
                filePath,
                _keyProvider.GetObscuraKey().ToArray(),
                ct);
        }

        /// <summary>
        /// Saves the Obscura store to the specified USB path.
        /// </summary>
        public async Task SaveObscuraStoreAsync(
            string usbRootPath,
            ObscuraStore store,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            if (store == null)
                throw new ArgumentNullException(nameof(store));

            store.ModifiedUtc = DateTimeOffset.UtcNow;

            var dirPath = Path.Combine(usbRootPath, "obscura");
            Directory.CreateDirectory(dirPath);

            var filePath = Path.Combine(dirPath, ObscuraStoreFilename);
            await SaveStoreAsync(filePath, store, _keyProvider.GetObscuraKey().ToArray(), ct);
        }

        #endregion

        #region Attestor Store

        /// <summary>
        /// Loads the Attestor store from the specified USB path.
        /// </summary>
        public async Task<AttestorStore?> LoadAttestorStoreAsync(
            string usbRootPath,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            var filePath = Path.Combine(usbRootPath, "attestor", AttestorStoreFilename);
            if (!File.Exists(filePath))
                return null;

            return await LoadStoreAsync<AttestorStore>(
                filePath,
                _keyProvider.GetAttestorKey().ToArray(),
                ct);
        }

        /// <summary>
        /// Saves the Attestor store to the specified USB path.
        /// </summary>
        public async Task SaveAttestorStoreAsync(
            string usbRootPath,
            AttestorStore store,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            if (store == null)
                throw new ArgumentNullException(nameof(store));

            store.ModifiedUtc = DateTimeOffset.UtcNow;

            var dirPath = Path.Combine(usbRootPath, "attestor");
            Directory.CreateDirectory(dirPath);

            var filePath = Path.Combine(dirPath, AttestorStoreFilename);
            await SaveStoreAsync(filePath, store, _keyProvider.GetAttestorKey().ToArray(), ct);
        }

        #endregion

        #region Recovery Store

        /// <summary>
        /// Loads the Recovery store from the specified USB path.
        /// </summary>
        public async Task<RecoveryStore?> LoadRecoveryStoreAsync(
            string usbRootPath,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            var filePath = Path.Combine(usbRootPath, "recovery", RecoveryStoreFilename);
            if (!File.Exists(filePath))
                return null;

            return await LoadStoreAsync<RecoveryStore>(
                filePath,
                _keyProvider.GetRecoveryKey().ToArray(),
                ct);
        }

        /// <summary>
        /// Saves the Recovery store to the specified USB path.
        /// </summary>
        public async Task SaveRecoveryStoreAsync(
            string usbRootPath,
            RecoveryStore store,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            if (store == null)
                throw new ArgumentNullException(nameof(store));

            store.ModifiedUtc = DateTimeOffset.UtcNow;

            var dirPath = Path.Combine(usbRootPath, "recovery");
            Directory.CreateDirectory(dirPath);

            var filePath = Path.Combine(dirPath, RecoveryStoreFilename);
            await SaveStoreAsync(filePath, store, _keyProvider.GetRecoveryKey().ToArray(), ct);
        }

        #endregion

        #region Internal Implementation

        private async Task<T?> LoadStoreAsync<T>(
            string filePath,
            byte[] domainKey,
            CancellationToken ct) where T : class
        {
            byte[] encryptedData = await File.ReadAllBytesAsync(filePath, ct);

            if (encryptedData.Length < NonceSize + TagSize + 1)
                throw new InvalidOperationException("Encrypted store file is corrupted");

            // Extract nonce, ciphertext, tag
            byte[] nonce = encryptedData.AsSpan(0, NonceSize).ToArray();
            byte[] tag = encryptedData.AsSpan(encryptedData.Length - TagSize, TagSize).ToArray();
            byte[] ciphertext = encryptedData.AsSpan(NonceSize, encryptedData.Length - NonceSize - TagSize).ToArray();

            // Additional authenticated data = filename (prevents file swapping attacks)
            byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));

            byte[] plaintext;
            try
            {
                plaintext = _encryptionService.Decrypt(
                    ciphertext,
                    nonce,
                    tag,
                    domainKey,
                    aad);
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Invalid domain key or corrupted store");
            }

            try
            {
                var json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<T>(json, GetJsonOptions());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private async Task SaveStoreAsync<T>(
            string filePath,
            T store,
            byte[] domainKey,
            CancellationToken ct) where T : class
        {
            var json = JsonSerializer.Serialize(store, GetJsonOptions());
            var plaintext = Encoding.UTF8.GetBytes(json);

            try
            {
                // Additional authenticated data = filename (prevents file swapping attacks)
                byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));

                var result = _encryptionService.Encrypt(plaintext, domainKey, aad);

                // Format: nonce || ciphertext || tag
                var encryptedData = new byte[result.Nonce.Length + result.Ciphertext.Length + result.Tag.Length];
                Buffer.BlockCopy(result.Nonce, 0, encryptedData, 0, result.Nonce.Length);
                Buffer.BlockCopy(result.Ciphertext, 0, encryptedData, result.Nonce.Length, result.Ciphertext.Length);
                Buffer.BlockCopy(result.Tag, 0, encryptedData, result.Nonce.Length + result.Ciphertext.Length, result.Tag.Length);

                // Write atomically (write to temp, then rename)
                var tempPath = filePath + ".tmp";
                await File.WriteAllBytesAsync(tempPath, encryptedData, ct);

                // Atomic rename (overwrites existing)
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static JsonSerializerOptions GetJsonOptions() => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false, // Minimize file size
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private void EnsureUnlocked()
        {
            if (!_keyProvider.IsUnlocked)
                throw new InvalidOperationException("Domain keys not available. Unlock first.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DomainStorageService));
        }

        public void Dispose()
        {
            _disposed = true;
        }

        #endregion

        #region Store Existence Checks

        /// <summary>
        /// Checks if the USB has a complete Phantom USB structure.
        /// </summary>
        public bool HasPhantomStructure(string usbRootPath)
        {
            return Directory.Exists(Path.Combine(usbRootPath, "obscura"))
                && Directory.Exists(Path.Combine(usbRootPath, "attestor"))
                && Directory.Exists(Path.Combine(usbRootPath, "recovery"));
        }

        /// <summary>
        /// Creates the initial directory structure for a new Phantom USB.
        /// </summary>
        public void InitializeUsbStructure(string usbRootPath)
        {
            Directory.CreateDirectory(Path.Combine(usbRootPath, "obscura"));
            Directory.CreateDirectory(Path.Combine(usbRootPath, "attestor"));
            Directory.CreateDirectory(Path.Combine(usbRootPath, "recovery"));
        }

        /// <summary>
        /// Checks if individual stores exist.
        /// </summary>
        public (bool Obscura, bool Attestor, bool Recovery) CheckStoresExist(string usbRootPath)
        {
            return (
                File.Exists(Path.Combine(usbRootPath, "obscura", ObscuraStoreFilename)),
                File.Exists(Path.Combine(usbRootPath, "attestor", AttestorStoreFilename)),
                File.Exists(Path.Combine(usbRootPath, "recovery", RecoveryStoreFilename))
            );
        }

        #endregion
    }
}
