using System;
using System.IO;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Creates and restores encrypted vault backups that bundle the manifest, policy, and root certificate.
    /// Uses Argon2id (via EncryptionService) to derive a key from passphrase/keyfile+PIN and AES-GCM for confidentiality.
    /// </summary>
    public sealed class SecureBackupManager
    {
        private readonly EncryptionService _encryptionService;

        public SecureBackupManager(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public string CreateBackup(string manifestPath, string policyPath, string rootCertPath, string passphrase, string? keyfilePath, string pin, string outputPath)
        {
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Manifest not found", manifestPath);
            if (!File.Exists(policyPath)) throw new FileNotFoundException("Policy file not found", policyPath);
            if (!File.Exists(rootCertPath)) throw new FileNotFoundException("Root certificate not found", rootCertPath);
            if (string.IsNullOrWhiteSpace(passphrase) && string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("Passphrase or keyfile is required.");
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException("PIN is required.");

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] policyBytes = File.ReadAllBytes(policyPath);
            byte[] rootCertBytes = File.ReadAllBytes(rootCertPath);

            // Build clear payload
            var payload = new
            {
                version = "1.0",
                createdUtc = DateTimeOffset.UtcNow,
                manifestFileName = Path.GetFileName(manifestPath),
                policyFileName = Path.GetFileName(policyPath),
                rootCertFileName = Path.GetFileName(rootCertPath),
                manifest = Convert.ToBase64String(manifestBytes),
                policy = Convert.ToBase64String(policyBytes),
                rootCert = Convert.ToBase64String(rootCertBytes)
            };

            string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Derive key from passphrase + optional keyfile + PIN
            var salt = _encryptionService.GenerateSalt();
            var combinedSecret = CombineSecrets(passphrase, keyfilePath, pin);
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt, keyLength: 32);

            var aad = Encoding.UTF8.GetBytes(Path.GetFileName(outputPath));
            var enc = _encryptionService.Encrypt(payloadBytes, key, aad);

            var envelope = new
            {
                version = "1.0",
                kdf = new
                {
                    salt = Convert.ToBase64String(salt),
                    memoryKb = 64 * 1024,
                    iterations = 3,
                    parallelism = 0
                },
                crypto = new
                {
                    nonce = Convert.ToBase64String(enc.Nonce),
                    tag = Convert.ToBase64String(enc.Tag),
                    ciphertext = Convert.ToBase64String(enc.Ciphertext),
                    aad = Convert.ToBase64String(aad)
                }
            };

            string envelopeJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputPath, envelopeJson);
            return outputPath;
        }

        public void RestoreBackup(string backupPath, string manifestDestinationPath, string policyDestinationPath, string rootCertDestinationPath, string passphrase, string? keyfilePath, string pin)
        {
            if (!File.Exists(backupPath)) throw new FileNotFoundException("Backup file not found", backupPath);
            if (string.IsNullOrWhiteSpace(passphrase) && string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("Passphrase or keyfile is required.");
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException("PIN is required.");

            string json = File.ReadAllText(backupPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var kdf = root.GetProperty("kdf");
            var salt = Convert.FromBase64String(kdf.GetProperty("salt").GetString() ?? throw new InvalidOperationException("Missing salt"));
            int mem = kdf.GetProperty("memoryKb").GetInt32();
            int iter = kdf.GetProperty("iterations").GetInt32();
            int par = kdf.GetProperty("parallelism").GetInt32();

            var combinedSecret = CombineSecrets(passphrase, keyfilePath, pin);
            byte[] key = _encryptionService.DeriveKey(combinedSecret.AsSpan(), salt, 32, mem, iter, par);

            var crypto = root.GetProperty("crypto");
            var nonce = Convert.FromBase64String(crypto.GetProperty("nonce").GetString() ?? throw new InvalidOperationException("Missing nonce"));
            var tag = Convert.FromBase64String(crypto.GetProperty("tag").GetString() ?? throw new InvalidOperationException("Missing tag"));
            var ciphertext = Convert.FromBase64String(crypto.GetProperty("ciphertext").GetString() ?? throw new InvalidOperationException("Missing ciphertext"));
            var aad = Convert.FromBase64String(crypto.GetProperty("aad").GetString() ?? throw new InvalidOperationException("Missing AAD"));

            var dec = _encryptionService.Decrypt(new ReadOnlySpan<byte>(ciphertext), nonce, tag, key, aad);
            var payloadJson = Encoding.UTF8.GetString(dec);
            var payload = JsonDocument.Parse(payloadJson).RootElement;

            byte[] manifestBytes = Convert.FromBase64String(payload.GetProperty("manifest").GetString()!);
            byte[] policyBytes = Convert.FromBase64String(payload.GetProperty("policy").GetString()!);
            byte[] rootCertBytes = Convert.FromBase64String(payload.GetProperty("rootCert").GetString()!);

            Directory.CreateDirectory(Path.GetDirectoryName(manifestDestinationPath) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(policyDestinationPath) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(rootCertDestinationPath) ?? ".");

            File.WriteAllBytes(manifestDestinationPath, manifestBytes);
            File.WriteAllBytes(policyDestinationPath, policyBytes);
            File.WriteAllBytes(rootCertDestinationPath, rootCertBytes);
        }

        private string CombineSecrets(string passphrase, string? keyfilePath, string pin)
        {
            var sb = new StringBuilder();
            sb.Append(passphrase ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(keyfilePath) && File.Exists(keyfilePath))
            {
                var keyfileBytes = File.ReadAllBytes(keyfilePath);
                sb.Append(Convert.ToBase64String(keyfileBytes));
                Array.Clear(keyfileBytes, 0, keyfileBytes.Length);
            }
            sb.Append(pin);
            return sb.ToString();
        }
    }
}
