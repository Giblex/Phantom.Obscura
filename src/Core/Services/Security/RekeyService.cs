using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services.Security
{

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
            CancellationToken cancellationToken = default,
            string? newPassphrase = null,
            string? providedNewKeyfilePath = null)
        {
            var result = new RekeyResult();

            try
            {
                progress?.Report(new RekeyProgress("Reading current manifest...", 0));
                using var spCurrent = SecurePassword.FromString(currentPassphrase);
                var manifest = _manifestService.ReadManifestSecure(manifestPath, spCurrent, currentKeyfilePath, usbSerial);

                // If a specific keyfile path is provided, use it directly.
                // If not, generate a new keyfile at the standard rotation name.
                string newKeyfilePath;
                if (!string.IsNullOrEmpty(providedNewKeyfilePath))
                {
                    newKeyfilePath = providedNewKeyfilePath;
                    if (!File.Exists(newKeyfilePath))
                    {
                        progress?.Report(new RekeyProgress("Generating new keyfile...", 10));
                        await _keyfileGenerator.GenerateKeyfileAsync(newKeyfilePath);
                    }
                    else
                    {
                        progress?.Report(new RekeyProgress("Using provided keyfile...", 10));
                    }
                }
                else
                {
                    newKeyfilePath = Path.Combine(Path.GetDirectoryName(currentKeyfilePath)!, "vault.key.new");
                    progress?.Report(new RekeyProgress("Generating new keyfile...", 10));
                    await _keyfileGenerator.GenerateKeyfileAsync(newKeyfilePath);
                }

                progress?.Report(new RekeyProgress("Deriving new encryption key...", 20));

                string effectiveNewPassphrase = string.IsNullOrEmpty(newPassphrase)
                    ? (currentPassphrase ?? string.Empty)
                    : newPassphrase;

                progress?.Report(new RekeyProgress("Re-encrypting vault database...", 40));
                await RekeyVaultDatabaseAsync(
                    vaultPath,
                    currentPassphrase, currentKeyfilePath,
                    effectiveNewPassphrase, newKeyfilePath,
                    progress, cancellationToken);

                progress?.Report(new RekeyProgress("Updating manifest...", 90));
                manifest.KeyfilePath = newKeyfilePath;
                manifest.LastKeyRotation = DateTimeOffset.UtcNow;
                manifest.KeyRotationCount += 1;
                manifest.KeyRotationPending = false;

                using var spNew = SecurePassword.FromString(effectiveNewPassphrase);
                _manifestService.WriteManifestSecure(manifest, manifestPath, spNew, newKeyfilePath, usbSerial);

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

        /// <summary>
        /// Re-encrypts a PhantomContainer vault file by decrypting with the old credentials
        /// and re-creating it with the new credentials. Uses PhantomContainerService to
        /// correctly handle the container format rather than treating it as a raw blob.
        /// </summary>
        private async Task RekeyVaultDatabaseAsync(
            string vaultPath,
            string? oldPassphrase, string oldKeyfilePath,
            string? newPassphrase, string newKeyfilePath,
            IProgress<RekeyProgress>? progress,
            CancellationToken cancellationToken)
        {
            using var containerService = new PhantomContainerService(_encryptionService);

            // 1. Decrypt the existing container payload into a temporary stream
            await using var plaintextStream = new System.IO.MemoryStream();
            await containerService.OpenContainerToStreamAsync(
                vaultPath,
                plaintextStream,
                oldPassphrase,
                oldKeyfilePath,
                cancellationToken);

            progress?.Report(new RekeyProgress("Encrypting with new key...", 70));

            long payloadSize = plaintextStream.Length;
            plaintextStream.Position = 0;

            // 2. Atomically replace the container file with a new one encrypted under new credentials
            string tempPath = vaultPath + ".rekey";
            try
            {
                await containerService.CreateContainerFromStreamAsync(
                    tempPath,
                    plaintextStream,
                    payloadSize,
                    newPassphrase,
                    newKeyfilePath,
                    manifest: null,
                    progress: null,
                    cancellationToken);

                File.Move(tempPath, vaultPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { }
                throw;
            }
            finally
            {
                // Zero the decrypted payload
                var buf = plaintextStream.GetBuffer();
                CryptographicOperations.ZeroMemory(buf.AsSpan(0, (int)payloadSize));
            }
        }

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

            string? providedNewKeyfile =
                !string.IsNullOrEmpty(newKeyfilePath) &&
                !string.Equals(newKeyfilePath, currentKeyfilePath, StringComparison.OrdinalIgnoreCase)
                    ? newKeyfilePath
                    : null;

            var result = Task.Run(() =>
                PerformRekeyAsync(vaultPath, manifestPath, currentKeyfilePath, currentPassphrase,
                    usbSerial: null, progress: null, cancellationToken: default,
                    newPassphrase: newPassphrase, providedNewKeyfilePath: providedNewKeyfile))
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

