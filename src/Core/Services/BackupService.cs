using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides functionality for creating and pruning encrypted backup
    /// copies of the vault manifest. Backups are encrypted with a
    /// fresh random salt and nonce on each invocation and stored as
    /// self‑contained JSON payloads. The retention period for
    /// backups is configurable via the <see cref="VaultManifest"/>
    /// settings. This service does not persist any secrets on
    /// disk outside the backup files themselves; it relies on the
    /// caller to supply the passphrase when creating a backup.
    /// </summary>
    public sealed class BackupService
    {
        private readonly EncryptionService _encryptionService;
        private readonly PhantomContainerService? _containerService;

        public BackupService(EncryptionService encryptionService, PhantomContainerService? containerService = null)
        {
            _encryptionService = encryptionService;
            _containerService = containerService;
        }

        /// <summary>
        /// Creates an encrypted backup of the specified manifest file. The
        /// output file is written to the provided backup directory with a
        /// timestamped filename. If the backup directory does not exist,
        /// it will be created. The backup payload contains the salt,
        /// nonce, tag and ciphertext encoded in Base64 for ease of
        /// parsing and transport.
        /// </summary>
        /// <param name="manifestPath">Absolute path to the manifest file.</param>
        /// <param name="passphrase">User passphrase used to derive the encryption key. May be null when a keyfile is provided.</param>
        /// <param name="keyfilePath">Optional path to a keyfile; contents are appended to the passphrase for key derivation.</param>
        /// <param name="backupDirectory">Directory where backups should be stored.</param>
        /// <returns>The full path to the created backup file.</returns>
        public async Task<string> CreateBackupAsync(string manifestPath, string? passphrase, string? keyfilePath, string backupDirectory)
        {
            if (string.IsNullOrEmpty(manifestPath)) throw new ArgumentException("Manifest path must be provided", nameof(manifestPath));
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Manifest file not found", manifestPath);
            bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
            bool hasKeyfile = !string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath);
            if (!hasPassphrase && !hasKeyfile)
            {
                throw new ArgumentException("A passphrase or keyfile must be provided", nameof(passphrase));
            }

            // Read the manifest bytes. For .pvault containers, extract the
            // embedded manifest via PhantomContainerService rather than
            // reading the entire container file.
            byte[] plainBytes;
            if (manifestPath.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase) && _containerService != null)
            {
                var manifest = _containerService.ReadManifestFromContainer(manifestPath, passphrase, keyfilePath);
                if (manifest == null)
                    throw new InvalidOperationException("Failed to read manifest from container.");
                plainBytes = System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(manifest));
            }
            else
            {
                plainBytes = await File.ReadAllBytesAsync(manifestPath).ConfigureAwait(false);
            }

            // Combine passphrase with keyfile contents if provided to
            // increase entropy.
            string combinedSecret = passphrase ?? string.Empty;
            if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
            {
                byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath).ConfigureAwait(false);
                combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
            }

            if (combinedSecret.Length == 0)
            {
                throw new InvalidOperationException("Unable to derive encryption key because no passphrase or keyfile data was provided.");
            }

            // Derive a key using a new random salt. The salt is stored in
            // the backup payload so the key can be re‑derived during
            // restoration.
            byte[] salt = _encryptionService.GenerateSalt();
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

            // Use the manifest file name as additional authenticated data.
            byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(manifestPath));
            var enc = _encryptionService.Encrypt(plainBytes, key, aad);

            // Build JSON payload for the backup file. Include the salt,
            // nonce and authentication tag so decryption can be performed
            // later.
            var timestamp = DateTimeOffset.UtcNow;
            var payload = new
            {
                name = Path.GetFileName(manifestPath),
                createdUtc = timestamp,
                salt = Convert.ToBase64String(salt),
                nonce = Convert.ToBase64String(enc.Nonce),
                tag = Convert.ToBase64String(enc.Tag),
                ciphertext = Convert.ToBase64String(enc.Ciphertext)
            };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            // Ensure the backup directory exists.
            Directory.CreateDirectory(backupDirectory);
            string backupFileName = $"manifest-backup-{timestamp:yyyyMMddHHmmss}.bak";
            string backupPath = Path.Combine(backupDirectory, backupFileName);
            await File.WriteAllTextAsync(backupPath, json).ConfigureAwait(false);

            // Wipe key material from memory.
            Array.Clear(key, 0, key.Length);
            Array.Clear(plainBytes, 0, plainBytes.Length);

            return backupPath;
        }

        /// <summary>
        /// Restores an encrypted manifest backup created by <see cref="CreateBackupAsync"/>.
        /// The backup payload is decrypted using the supplied passphrase and optional keyfile
        /// and written to the target manifest path, overwriting any existing file.
        /// </summary>
        /// <param name="backupFilePath">Path to the encrypted backup file.</param>
        /// <param name="manifestDestinationPath">Destination path for the restored manifest.</param>
        /// <param name="passphrase">User passphrase used when the backup was created. May be null when a keyfile is provided.</param>
        /// <param name="keyfilePath">Optional path to a keyfile whose contents were combined with the passphrase.</param>
        public async Task RestoreBackupAsync(string backupFilePath, string manifestDestinationPath, string? passphrase, string? keyfilePath)
        {
            if (string.IsNullOrEmpty(backupFilePath)) throw new ArgumentException("Backup file path must be provided", nameof(backupFilePath));
            if (!File.Exists(backupFilePath)) throw new FileNotFoundException("Backup file not found", backupFilePath);
            if (string.IsNullOrEmpty(manifestDestinationPath)) throw new ArgumentException("Manifest destination path must be provided", nameof(manifestDestinationPath));

            bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
            bool hasKeyfile = !string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath);
            if (!hasPassphrase && !hasKeyfile)
            {
                throw new ArgumentException("A passphrase or keyfile must be provided", nameof(passphrase));
            }

            string payloadJson = await File.ReadAllTextAsync(backupFilePath).ConfigureAwait(false);
            payloadJson = payloadJson.Trim();

            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string saltBase64 = root.GetProperty("salt").GetString() ?? throw new FormatException("Backup payload missing salt");
            string nonceBase64 = root.GetProperty("nonce").GetString() ?? throw new FormatException("Backup payload missing nonce");
            string tagBase64 = root.GetProperty("tag").GetString() ?? throw new FormatException("Backup payload missing tag");
            string ciphertextBase64 = root.GetProperty("ciphertext").GetString() ?? throw new FormatException("Backup payload missing ciphertext");

            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] nonce = Convert.FromBase64String(nonceBase64);
            byte[] tag = Convert.FromBase64String(tagBase64);
            byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);

            string combinedSecret = passphrase ?? string.Empty;
            if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
            {
                byte[] keyfileBytes = await File.ReadAllBytesAsync(keyfilePath).ConfigureAwait(false);
                combinedSecret = combinedSecret + Convert.ToBase64String(keyfileBytes);
                Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
            }

            if (combinedSecret.Length == 0)
            {
                throw new InvalidOperationException("Unable to derive encryption key because no passphrase or keyfile data was provided.");
            }

            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);
            byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(manifestDestinationPath));
            byte[] manifestBytes = _encryptionService.Decrypt(ciphertext, nonce, tag, key, aad);

            string? directory = Path.GetDirectoryName(manifestDestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(manifestDestinationPath, manifestBytes).ConfigureAwait(false);

            Array.Clear(key, 0, key.Length);
            Array.Clear(manifestBytes, 0, manifestBytes.Length);
        }

        /// <summary>
        /// Removes backup files older than the specified retention period
        /// from the backup directory. If the directory does not exist or
        /// contains files that cannot be parsed for timestamps, those
        /// files are ignored. This method is synchronous and should not
        /// be called on a UI thread if the directory contains many files.
        /// </summary>
        /// <param name="backupDirectory">Directory containing backup files.</param>
        /// <param name="retentionDays">Number of days to retain backups. Files older than this are deleted.</param>
        public void PruneBackups(string backupDirectory, int retentionDays)
        {
            if (string.IsNullOrEmpty(backupDirectory) || retentionDays < 1) return;
            if (!Directory.Exists(backupDirectory)) return;
            var threshold = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);
            foreach (var file in Directory.GetFiles(backupDirectory))
            {
                try
                {
                    DateTime creation = File.GetCreationTimeUtc(file);
                    // Some file systems may not persist creation time. Fall back to last write time.
                    if (creation == DateTime.MinValue) creation = File.GetLastWriteTimeUtc(file);
                    if (creation < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore deletion failures (e.g. file in use). They will be retried on next prune.
                }
            }
        }

        /// <summary>
        /// Generates an ECDSA P-256 key pair for backup signature verification.
        /// This should be called once during vault creation.
        /// </summary>
        /// <returns>Tuple of (privateKey, publicKey) in DER format</returns>
        public static (byte[] privateKey, byte[] publicKey) GenerateSigningKeyPair()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privateKey = ecdsa.ExportECPrivateKey();
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            return (privateKey, publicKey);
        }

        /// <summary>
        /// Creates a signed backup by appending an ECDSA signature to the backup file.
        /// The signature is computed over the entire backup payload and appended with its length.
        /// </summary>
        /// <param name="manifestPath">Path to the manifest file to backup</param>
        /// <param name="passphrase">Encryption passphrase</param>
        /// <param name="keyfilePath">Optional keyfile path</param>
        /// <param name="backupDirectory">Directory to store backup</param>
        /// <param name="signingPrivateKey">ECDSA private key in DER format</param>
        /// <returns>Path to the signed backup file</returns>
        public async Task<string> CreateSignedBackupAsync(
            string manifestPath,
            string? passphrase,
            string? keyfilePath,
            string backupDirectory,
            byte[] signingPrivateKey)
        {
            // Create the backup (existing logic)
            var backupPath = await CreateBackupAsync(manifestPath, passphrase, keyfilePath, backupDirectory);

            // Read the backup contents
            var backupBytes = await File.ReadAllBytesAsync(backupPath);

            // Sign the backup data
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(signingPrivateKey, out _);
            var signature = ecdsa.SignData(backupBytes, HashAlgorithmName.SHA256);

            // Append signature + signature length to backup file
            await using var fs = File.Open(backupPath, FileMode.Append, FileAccess.Write);
            await fs.WriteAsync(signature);

            // Write signature length as last 4 bytes (allows easy extraction)
            var lengthBytes = BitConverter.GetBytes(signature.Length);
            await fs.WriteAsync(lengthBytes);

            return backupPath;
        }

        /// <summary>
        /// Verifies the ECDSA signature of a signed backup file.
        /// </summary>
        /// <param name="backupPath">Path to the signed backup file</param>
        /// <param name="publicKey">ECDSA public key in SubjectPublicKeyInfo format</param>
        /// <returns>True if signature is valid; false otherwise</returns>
        public bool VerifyBackupSignature(string backupPath, byte[] publicKey)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("Backup file not found", backupPath);

            try
            {
                var allBytes = File.ReadAllBytes(backupPath);

                // File must be at least 4 bytes (signature length marker)
                if (allBytes.Length < 4)
                    return false;

                // Read signature length from last 4 bytes
                var sigLength = BitConverter.ToInt32(allBytes, allBytes.Length - 4);

                // Validate signature length is reasonable
                if (sigLength < 64 || sigLength > 256 || sigLength + 4 > allBytes.Length)
                    return false;

                // Extract signature
                var signatureStart = allBytes.Length - 4 - sigLength;
                var signature = allBytes[signatureStart..(allBytes.Length - 4)];

                // Extract backup data (everything before signature)
                var backupData = allBytes[..signatureStart];

                // Verify signature
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return ecdsa.VerifyData(backupData, signature, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                // Log verification failure but don't throw - return false
                System.Diagnostics.Debug.WriteLine($"Backup signature verification failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores a signed backup after verifying its signature.
        /// Throws if signature verification fails.
        /// </summary>
        /// <param name="backupFilePath">Path to the signed backup file</param>
        /// <param name="manifestDestinationPath">Destination for restored manifest</param>
        /// <param name="passphrase">Decryption passphrase</param>
        /// <param name="keyfilePath">Optional keyfile path</param>
        /// <param name="publicKey">ECDSA public key for verification</param>
        public async Task RestoreSignedBackupAsync(
            string backupFilePath,
            string manifestDestinationPath,
            string? passphrase,
            string? keyfilePath,
            byte[] publicKey)
        {
            // Verify signature first
            if (!VerifyBackupSignature(backupFilePath, publicKey))
            {
                throw new InvalidOperationException(
                    "Backup signature verification failed. The backup file may have been tampered with or corrupted. " +
                    "Restoration aborted for security reasons.");
            }

            // Read all bytes and extract backup data (without signature)
            var allBytes = await File.ReadAllBytesAsync(backupFilePath);
            var sigLength = BitConverter.ToInt32(allBytes, allBytes.Length - 4);
            var signatureStart = allBytes.Length - 4 - sigLength;
            var backupData = allBytes[..signatureStart];

            // Create temporary unsigned backup file
            var tempBackupPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempBackupPath, backupData);

                // Restore using existing logic
                await RestoreBackupAsync(tempBackupPath, manifestDestinationPath, passphrase, keyfilePath);
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempBackupPath))
                {
                    try { File.Delete(tempBackupPath); } catch { /* Best effort */ }
                }
            }
        }
    }
}