using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{

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
        /// In-app (unlocked) load. The recovery store is an envelope; it is opened via the
        /// vault-key wrap so the running app can manage codes without entering one.
        /// Standalone, locked recovery must use <see cref="OpenRecoveryStoreWithCodeAsync"/>.
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

            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            using var opened = RecoveryStoreEnvelope.OpenWithVaultKey(bytes, _keyProvider.GetRecoveryKey());
            return opened.Store;
        }

        /// <summary>
        /// STANDALONE recovery entry point. Opens the recovery store using a single recovery
        /// code with NO unlock and NO vault key — this is what makes recovery work when the
        /// user has lost access. The caller owns the returned handle and MUST dispose it; the
        /// handle exposes the RSK needed to persist mutations via
        /// <see cref="PersistRecoveryEnvelopeAsync"/> while the vault remains locked.
        /// </summary>
        public async Task<RecoveryStoreEnvelope.OpenedStore?> OpenRecoveryStoreWithCodeAsync(
            string usbRootPath,
            string recoveryCode,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var filePath = Path.Combine(usbRootPath, "recovery", RecoveryStoreFilename);
            if (!File.Exists(filePath))
                return null;

            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            return RecoveryStoreEnvelope.OpenWithCode(bytes, recoveryCode);
        }

        /// <summary>
        /// Seed a brand-new recovery store at setup. Wraps the RSK once per recovery code (for
        /// locked, standalone recovery) and once under the vault recovery key (for in-app
        /// management). Requires the vault to be unlocked, which it always is during setup.
        /// </summary>
        public async Task SeedRecoveryStoreAsync(
            string usbRootPath,
            RecoveryStore store,
            IReadOnlyList<string> plaintextRecoveryCodes,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            EnsureUnlocked();

            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (plaintextRecoveryCodes == null || plaintextRecoveryCodes.Count == 0)
                throw new ArgumentException("Recovery codes are required to seed the recovery store.", nameof(plaintextRecoveryCodes));

            store.ModifiedUtc = DateTimeOffset.UtcNow;

            var envelopeBytes = RecoveryStoreEnvelope.Seal(store, plaintextRecoveryCodes, _keyProvider.GetRecoveryKey());
            await WriteRecoveryEnvelopeAsync(usbRootPath, envelopeBytes, ct);
        }

        /// <summary>
        /// In-app (unlocked) update. Re-encrypts the body under the existing RSK (obtained via
        /// the vault-key wrap) while preserving the header — code wraps stay valid.
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

            var filePath = Path.Combine(usbRootPath, "recovery", RecoveryStoreFilename);
            if (!File.Exists(filePath))
                throw new InvalidOperationException(
                    "Recovery store has not been seeded. Use SeedRecoveryStoreAsync during setup before updating.");

            store.ModifiedUtc = DateTimeOffset.UtcNow;

            var existing = await File.ReadAllBytesAsync(filePath, ct);
            using var opened = RecoveryStoreEnvelope.OpenWithVaultKey(existing, _keyProvider.GetRecoveryKey());
            var resealed = opened.ResealBody(store);
            await WriteRecoveryEnvelopeAsync(usbRootPath, resealed, ct);
        }

        /// <summary>
        /// Persist a recovery envelope produced while the vault is LOCKED (e.g. after a standalone
        /// recovery session marks a code used / appends an audit entry). Writes raw envelope bytes
        /// atomically; no unlock required.
        /// </summary>
        public async Task PersistRecoveryEnvelopeAsync(
            string usbRootPath,
            byte[] envelopeBytes,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (envelopeBytes == null || envelopeBytes.Length == 0)
                throw new ArgumentException("Envelope bytes are required.", nameof(envelopeBytes));

            await WriteRecoveryEnvelopeAsync(usbRootPath, envelopeBytes, ct);
        }

        private static async Task WriteRecoveryEnvelopeAsync(
            string usbRootPath,
            byte[] envelopeBytes,
            CancellationToken ct)
        {
            var dirPath = Path.Combine(usbRootPath, "recovery");
            Directory.CreateDirectory(dirPath);

            var filePath = Path.Combine(dirPath, RecoveryStoreFilename);
            var tempPath = filePath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, envelopeBytes, ct);
            File.Move(tempPath, filePath, overwrite: true);
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

            byte[] nonce = encryptedData.AsSpan(0, NonceSize).ToArray();
            byte[] tag = encryptedData.AsSpan(encryptedData.Length - TagSize, TagSize).ToArray();
            byte[] ciphertext = encryptedData.AsSpan(NonceSize, encryptedData.Length - NonceSize - TagSize).ToArray();

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

                byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));

                var result = _encryptionService.Encrypt(plaintext, domainKey, aad);

                var encryptedData = new byte[result.Nonce.Length + result.Ciphertext.Length + result.Tag.Length];
                Buffer.BlockCopy(result.Nonce, 0, encryptedData, 0, result.Nonce.Length);
                Buffer.BlockCopy(result.Ciphertext, 0, encryptedData, result.Nonce.Length, result.Ciphertext.Length);
                Buffer.BlockCopy(result.Tag, 0, encryptedData, result.Nonce.Length + result.Ciphertext.Length, result.Tag.Length);

                var tempPath = filePath + ".tmp";
                await File.WriteAllBytesAsync(tempPath, encryptedData, ct);

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
            WriteIndented = false,
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

        public bool HasPhantomStructure(string usbRootPath)
        {
            return Directory.Exists(Path.Combine(usbRootPath, "obscura"))
                && Directory.Exists(Path.Combine(usbRootPath, "attestor"))
                && Directory.Exists(Path.Combine(usbRootPath, "recovery"));
        }

        public void InitializeUsbStructure(string usbRootPath)
        {
            Directory.CreateDirectory(Path.Combine(usbRootPath, "obscura"));
            Directory.CreateDirectory(Path.Combine(usbRootPath, "attestor"));
            Directory.CreateDirectory(Path.Combine(usbRootPath, "recovery"));
        }

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

