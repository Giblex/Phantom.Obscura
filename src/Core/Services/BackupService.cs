using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Security;

namespace PhantomVault.Core.Services
{

    public sealed class BackupService
    {
        private readonly EncryptionService _encryptionService;
        private readonly PhantomContainerService? _containerService;

        public BackupService(EncryptionService encryptionService, PhantomContainerService? containerService = null)
        {
            _encryptionService = encryptionService;
            _containerService = containerService;
        }

        public async Task<string> CreateBackupAsync(string manifestPath, string? passphrase, string? keyfilePath, string backupDirectory)
            => await CreateBackupAsync(manifestPath, passphrase, keyfilePath, backupDirectory, useEncryption: true).ConfigureAwait(false);

        /// <summary>
        /// Creates a manifest backup. When <paramref name="useEncryption"/> is true the backup is
        /// sealed with a passphrase/keyfile-derived Argon2id + AES-GCM key. When false the backup is
        /// sealed with Windows DPAPI (CurrentUser) instead — bound to the current OS user, no passphrase
        /// required to restore. The backup is NEVER written in plaintext; on non-Windows platforms the
        /// DPAPI request transparently falls back to passphrase encryption.
        /// </summary>
        public async Task<string> CreateBackupAsync(string manifestPath, string? passphrase, string? keyfilePath, string backupDirectory, bool useEncryption)
        {
            if (string.IsNullOrEmpty(manifestPath)) throw new ArgumentException("Manifest path must be provided", nameof(manifestPath));
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Manifest file not found", manifestPath);
            KeyfileGuard.Require(keyfilePath, nameof(keyfilePath));

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

            var timestamp = DateTimeOffset.UtcNow;
            string json;

            // DPAPI-only path: seal the manifest with the current Windows user's data-protection key.
            // Never produces plaintext. Falls back to passphrase encryption off Windows.
            if (!useEncryption && OperatingSystem.IsWindows())
            {
                byte[] entropy = Encoding.UTF8.GetBytes("PhantomVault.Backup.DPAPI:" + Path.GetFileName(manifestPath));
                byte[] sealedBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);

                var dpapiPayload = new
                {
                    name = Path.GetFileName(manifestPath),
                    createdUtc = timestamp,
                    protection = "dpapi",
                    ciphertext = Convert.ToBase64String(sealedBytes)
                };
                json = JsonSerializer.Serialize(dpapiPayload, new JsonSerializerOptions { WriteIndented = false });
                Array.Clear(sealedBytes, 0, sealedBytes.Length);
                Array.Clear(plainBytes, 0, plainBytes.Length);

                Directory.CreateDirectory(backupDirectory);
                string dpapiFileName = $"manifest-backup-{timestamp:yyyyMMddHHmmss}.bak";
                string dpapiPath = Path.Combine(backupDirectory, dpapiFileName);
                await File.WriteAllTextAsync(dpapiPath, json).ConfigureAwait(false);
                return dpapiPath;
            }

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

            byte[] salt = _encryptionService.GenerateSalt();
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt);

            byte[] aad = Encoding.UTF8.GetBytes(Path.GetFileName(manifestPath));
            var enc = _encryptionService.Encrypt(plainBytes, key, aad);

            var payload = new
            {
                name = Path.GetFileName(manifestPath),
                createdUtc = timestamp,
                salt = Convert.ToBase64String(salt),
                nonce = Convert.ToBase64String(enc.Nonce),
                tag = Convert.ToBase64String(enc.Tag),
                ciphertext = Convert.ToBase64String(enc.Ciphertext)
            };
            json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            Directory.CreateDirectory(backupDirectory);
            string backupFileName = $"manifest-backup-{timestamp:yyyyMMddHHmmss}.bak";
            string backupPath = Path.Combine(backupDirectory, backupFileName);
            await File.WriteAllTextAsync(backupPath, json).ConfigureAwait(false);

            Array.Clear(key, 0, key.Length);
            Array.Clear(plainBytes, 0, plainBytes.Length);

            return backupPath;
        }

        public async Task RestoreBackupAsync(string backupFilePath, string manifestDestinationPath, string? passphrase, string? keyfilePath)
        {
            if (string.IsNullOrEmpty(backupFilePath)) throw new ArgumentException("Backup file path must be provided", nameof(backupFilePath));
            if (!File.Exists(backupFilePath)) throw new FileNotFoundException("Backup file not found", backupFilePath);
            if (string.IsNullOrEmpty(manifestDestinationPath)) throw new ArgumentException("Manifest destination path must be provided", nameof(manifestDestinationPath));

            KeyfileGuard.Require(keyfilePath, nameof(keyfilePath));

            string payloadJson = await File.ReadAllTextAsync(backupFilePath).ConfigureAwait(false);
            payloadJson = payloadJson.Trim();

            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            // DPAPI-sealed backups (BackupUseEncryption = off) carry a "protection":"dpapi" marker
            // and no salt/nonce/tag. Unwrap with the current Windows user's data-protection key.
            if (root.TryGetProperty("protection", out var protectionProp) &&
                string.Equals(protectionProp.GetString(), "dpapi", StringComparison.OrdinalIgnoreCase))
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("This backup was sealed with Windows DPAPI and can only be restored on the originating Windows user account.");
                }

                string dpapiCipher = root.GetProperty("ciphertext").GetString() ?? throw new FormatException("Backup payload missing ciphertext");
                byte[] dpapiSealed = Convert.FromBase64String(dpapiCipher);
                byte[] dpapiEntropy = Encoding.UTF8.GetBytes("PhantomVault.Backup.DPAPI:" + Path.GetFileName(manifestDestinationPath));
                byte[] dpapiPlain = ProtectedData.Unprotect(dpapiSealed, dpapiEntropy, DataProtectionScope.CurrentUser);

                string? dpapiDir = Path.GetDirectoryName(manifestDestinationPath);
                if (!string.IsNullOrEmpty(dpapiDir))
                {
                    Directory.CreateDirectory(dpapiDir);
                }
                await File.WriteAllBytesAsync(manifestDestinationPath, dpapiPlain).ConfigureAwait(false);
                Array.Clear(dpapiPlain, 0, dpapiPlain.Length);
                return;
            }

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

                    if (creation == DateTime.MinValue) creation = File.GetLastWriteTimeUtc(file);
                    if (creation < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {

                }
            }
        }

        public static (byte[] privateKey, byte[] publicKey) GenerateSigningKeyPair()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privateKey = ecdsa.ExportECPrivateKey();
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            return (privateKey, publicKey);
        }

        public async Task<string> CreateSignedBackupAsync(
            string manifestPath,
            string? passphrase,
            string? keyfilePath,
            string backupDirectory,
            byte[] signingPrivateKey)
        {

            var backupPath = await CreateBackupAsync(manifestPath, passphrase, keyfilePath, backupDirectory);

            var backupBytes = await File.ReadAllBytesAsync(backupPath);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(signingPrivateKey, out _);
            var signature = ecdsa.SignData(backupBytes, HashAlgorithmName.SHA256);

            await using var fs = File.Open(backupPath, FileMode.Append, FileAccess.Write);
            await fs.WriteAsync(signature);

            var lengthBytes = BitConverter.GetBytes(signature.Length);
            await fs.WriteAsync(lengthBytes);

            return backupPath;
        }

        public bool VerifyBackupSignature(string backupPath, byte[] publicKey)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("Backup file not found", backupPath);

            try
            {
                var allBytes = File.ReadAllBytes(backupPath);

                if (allBytes.Length < 4)
                    return false;

                var sigLength = BitConverter.ToInt32(allBytes, allBytes.Length - 4);

                if (sigLength < 64 || sigLength > 256 || sigLength + 4 > allBytes.Length)
                    return false;

                var signatureStart = allBytes.Length - 4 - sigLength;
                var signature = allBytes[signatureStart..(allBytes.Length - 4)];

                var backupData = allBytes[..signatureStart];

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return ecdsa.VerifyData(backupData, signature, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"Backup signature verification failed: {ex.Message}");
                return false;
            }
        }

        public async Task RestoreSignedBackupAsync(
            string backupFilePath,
            string manifestDestinationPath,
            string? passphrase,
            string? keyfilePath,
            byte[] publicKey)
        {

            if (!VerifyBackupSignature(backupFilePath, publicKey))
            {
                throw new InvalidOperationException(
                    "Backup signature verification failed. The backup file may have been tampered with or corrupted. " +
                    "Restoration aborted for security reasons.");
            }

            var allBytes = await File.ReadAllBytesAsync(backupFilePath);
            var sigLength = BitConverter.ToInt32(allBytes, allBytes.Length - 4);
            var signatureStart = allBytes.Length - 4 - sigLength;
            var backupData = allBytes[..signatureStart];

            var tempBackupPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempBackupPath, backupData);

                await RestoreBackupAsync(tempBackupPath, manifestDestinationPath, passphrase, keyfilePath);
            }
            finally
            {

                if (File.Exists(tempBackupPath))
                {
                    try { File.Delete(tempBackupPath); } catch {  }
                }
            }
        }
    }
}

