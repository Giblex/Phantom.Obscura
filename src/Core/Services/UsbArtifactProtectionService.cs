using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Protects USB-side metadata artifacts with per-file encryption derived from
    /// the vault manifest and the current authentication material.
    /// </summary>
    public sealed class UsbArtifactProtectionService
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PMETA1");
        private readonly EncryptionService _encryptionService;

        public UsbArtifactProtectionService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        public async Task WriteEncryptedJsonAsync<T>(
            string filePath,
            T value,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Artifact path is required.", nameof(filePath));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] plainBytes = JsonSerializer.SerializeToUtf8Bytes(value);
            try
            {
                byte[] fileKey = DeriveArtifactKey(manifest, passphrase, keyfilePath, purpose, Path.GetFileName(filePath));
                try
                {
                    byte[] aad = BuildAad(purpose, Path.GetFileName(filePath), manifest);
                    var result = _encryptionService.Encrypt(plainBytes, fileKey, aad);

                    await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await stream.WriteAsync(Magic, cancellationToken);
                    await stream.WriteAsync(result.Nonce, cancellationToken);
                    await stream.WriteAsync(result.Tag, cancellationToken);
                    await stream.WriteAsync(result.Ciphertext, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(fileKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);
            }
        }

        public T ReadEncryptedJson<T>(
            string filePath,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string purpose)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Artifact path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Encrypted USB artifact not found.", filePath);

            byte[] envelope = File.ReadAllBytes(filePath);
            try
            {
                if (envelope.Length < Magic.Length + 12 + 16)
                    throw new InvalidOperationException("Encrypted USB artifact is malformed.");

                for (int i = 0; i < Magic.Length; i++)
                {
                    if (envelope[i] != Magic[i])
                        throw new InvalidOperationException("Encrypted USB artifact header is invalid.");
                }

                byte[] nonce = envelope.AsSpan(Magic.Length, 12).ToArray();
                byte[] tag = envelope.AsSpan(Magic.Length + 12, 16).ToArray();
                byte[] ciphertext = envelope.AsSpan(Magic.Length + 28).ToArray();
                byte[] fileKey = DeriveArtifactKey(manifest, passphrase, keyfilePath, purpose, Path.GetFileName(filePath));

                try
                {
                    byte[] aad = BuildAad(purpose, Path.GetFileName(filePath), manifest);
                    byte[] plainBytes = _encryptionService.Decrypt(ciphertext, nonce, tag, fileKey, aad);
                    try
                    {
                        return JsonSerializer.Deserialize<T>(plainBytes)
                            ?? throw new InvalidOperationException("Encrypted USB artifact is empty.");
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plainBytes);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(fileKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(envelope);
            }
        }

        private byte[] DeriveArtifactKey(
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string purpose,
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(manifest.SaltBase64))
                throw new InvalidOperationException("Vault manifest salt is required to protect USB artifacts.");

            byte[] salt = Convert.FromBase64String(manifest.SaltBase64);
            byte[] secretMaterial = BuildSecretMaterial(passphrase, keyfilePath);

            try
            {
                byte[] secretDigest = SHA256.HashData(secretMaterial);
                return HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    salt,
                    32,
                    Encoding.UTF8.GetBytes($"PhantomVault.UsbArtifact.{purpose}.v1|{fileName}|{manifest.UsbBindingId}|{manifest.UsbBindingGuid}"),
                    secretDigest);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(secretMaterial);
            }
        }

        private static byte[] BuildAad(string purpose, string fileName, VaultManifest manifest)
        {
            return Encoding.UTF8.GetBytes(
                $"{purpose}|{fileName}|{manifest.UsbBindingId}|{manifest.UsbBindingGuid}|{manifest.DeviceId}|{manifest.Guuid}");
        }

        private static byte[] BuildSecretMaterial(string? passphrase, string? keyfilePath)
        {
            using var buffer = new MemoryStream();

            if (!string.IsNullOrEmpty(passphrase))
            {
                var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
                buffer.Write(passphraseBytes, 0, passphraseBytes.Length);
                CryptographicOperations.ZeroMemory(passphraseBytes);
            }

            if (!string.IsNullOrEmpty(keyfilePath))
            {
                var keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
                try
                {
                    buffer.Write(keyfileBytes, 0, keyfileBytes.Length);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }
            }

            if (buffer.Length == 0)
                throw new ArgumentException("USB artifact encryption requires a passphrase or keyfile.");

            return buffer.ToArray();
        }
    }
}
