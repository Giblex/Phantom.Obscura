using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Handles cryptographic key rotation for vaults.
    /// Generates a new keyfile, derives a new key, re-encrypts the vault database,
    /// and updates the manifest metadata.
    /// </summary>
    public sealed class RekeyService
    {
        private readonly EncryptionService _encryptionService;
        private readonly ManifestService _manifestService;
        private readonly LayeredEncryptionService _layeredEncryptionService;
        private readonly KeyfileGeneratorService _keyfileGenerator;

        public RekeyService(
            EncryptionService encryptionService,
            ManifestService manifestService,
            LayeredEncryptionService layeredEncryptionService,
            KeyfileGeneratorService keyfileGenerator)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _layeredEncryptionService = layeredEncryptionService ?? throw new ArgumentNullException(nameof(layeredEncryptionService));
            _keyfileGenerator = keyfileGenerator ?? throw new ArgumentNullException(nameof(keyfileGenerator));
        }

        public bool IsRotationRequired(VaultManifest manifest, int? dayThreshold = null)
        {
            if (manifest == null) return false;
            if (manifest.KeyRotationPending) return true;
            var threshold = dayThreshold ?? manifest.DaysUntilRotationRequired;
            if (threshold <= 0) return false;
            var age = (DateTimeOffset.UtcNow - manifest.LastKeyRotation).TotalDays;
            return age >= threshold;
        }

        public async Task<RekeyResult> PerformRekeyAsync(
            string vaultPath,
            string manifestPath,
            string currentKeyfilePath,
            string? currentPassphrase,
            string? usbSerial,
            IProgress<RekeyProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RekeyResult();

            try
            {
                progress?.Report(new RekeyProgress("Reading current manifest...", 0));
                var manifest = _manifestService.ReadManifest(manifestPath, currentPassphrase, currentKeyfilePath, usbSerial);

                progress?.Report(new RekeyProgress("Generating new keyfile...", 10));
                string newKeyfilePath = Path.Combine(Path.GetDirectoryName(currentKeyfilePath)!, "vault.key.new");
                await _keyfileGenerator.GenerateKeyfileAsync(newKeyfilePath, 4096);

                progress?.Report(new RekeyProgress("Deriving new encryption key...", 20));
                byte[] oldSalt = Convert.FromBase64String(manifest.SaltBase64);
                byte[] oldKey = _encryptionService.DeriveKey((currentPassphrase ?? string.Empty).AsSpan(), oldSalt);

                byte[] newSalt = _encryptionService.GenerateSalt(32);
                string keyfileContent = await File.ReadAllTextAsync(newKeyfilePath, cancellationToken);
                string combinedSecret = (currentPassphrase ?? string.Empty) + keyfileContent;
                byte[] newKey = _encryptionService.DeriveKey(combinedSecret.AsSpan(), newSalt);

                progress?.Report(new RekeyProgress("Re-encrypting vault database...", 40));
                await RekeyVaultDatabaseAsync(vaultPath, oldKey, newKey, progress, cancellationToken);

                progress?.Report(new RekeyProgress("Updating manifest...", 90));
                manifest.SaltBase64 = Convert.ToBase64String(newSalt);
                manifest.LastKeyRotation = DateTimeOffset.UtcNow;
                manifest.KeyRotationCount += 1;
                manifest.KeyRotationPending = false;

                _manifestService.WriteManifest(manifest, manifestPath, currentPassphrase, newKeyfilePath, usbSerial);

                progress?.Report(new RekeyProgress("Cleaning up...", 100));

                result.Success = true;
                result.NewKeyfilePath = newKeyfilePath;
                result.RotationTimestamp = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
            }

            return result;
        }

        private async Task RekeyVaultDatabaseAsync(
            string vaultPath,
            byte[] oldKey,
            byte[] newKey,
            IProgress<RekeyProgress>? progress,
            CancellationToken cancellationToken)
        {
            // NOTE: This is a simplified placeholder. Real implementation should
            // deserialize the vault, decrypt each record, re-encrypt, and serialize.

            // Read vault ciphertext
            byte[] encryptedVault = await File.ReadAllBytesAsync(vaultPath, cancellationToken);

            // Parse metadata: [nonce|tag|ciphertext] (simplified layout)
            int metadataSize = 12 + 16; // nonce + tag for AES-GCM
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            Array.Copy(encryptedVault, 0, nonce, 0, 12);
            Array.Copy(encryptedVault, 12, tag, 0, 16);
            byte[] ciphertext = new byte[encryptedVault.Length - metadataSize];
            Array.Copy(encryptedVault, metadataSize, ciphertext, 0, ciphertext.Length);

            // Decrypt with old key
            byte[] plainVault = _encryptionService.Decrypt(ciphertext, nonce, tag, oldKey);

            progress?.Report(new RekeyProgress("Encrypting with new key...", 70));

            // Encrypt with new key
            var newEncResult = _encryptionService.Encrypt(plainVault, newKey);

            // Write back atomically
            string tempPath = vaultPath + ".new";
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(newEncResult.Nonce, cancellationToken);
                await fs.WriteAsync(newEncResult.Tag, cancellationToken);
                await fs.WriteAsync(newEncResult.Ciphertext, cancellationToken);
                await fs.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, vaultPath, overwrite: true);

            CryptographicOperations.ZeroMemory(plainVault);
        }

        /// <summary>
        /// Legacy synchronous rekey hook used by UI flows.
        /// Delegates to <see cref="PerformRekeyAsync"/> on the thread-pool.
        /// </summary>
        public bool RekeyVault(
            string manifestPath,
            string currentPassphrase,
            string newPassphrase,
            string? currentKeyfilePath,
            string? newKeyfilePath)
        {
            if (string.IsNullOrEmpty(currentKeyfilePath))
                return false;

            var vaultPath = manifestPath.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase)
                ? manifestPath
                : Path.ChangeExtension(manifestPath, ".vault");
            var result = Task.Run(() =>
                PerformRekeyAsync(vaultPath, manifestPath, currentKeyfilePath, currentPassphrase, usbSerial: null))
                .GetAwaiter().GetResult();

            return result.Success;
        }
    }

    public class RekeyResult
    {
        public bool Success { get; set; }
        public Exception? Error { get; set; }
        public string? NewKeyfilePath { get; set; }
        public string? BackupKeyfilePath { get; set; }
        public DateTimeOffset RotationTimestamp { get; set; }
    }

    public class RekeyProgress
    {
        public string Message { get; }
        public int PercentComplete { get; }

        public RekeyProgress(string message, int percentComplete)
        {
            Message = message;
            PercentComplete = percentComplete;
        }
    }
}
