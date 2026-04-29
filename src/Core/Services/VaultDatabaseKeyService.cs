using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    public sealed class VaultDatabaseKeyService
    {
        private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("PhantomObscura.VaultDatabaseKey.v1");
        private readonly EncryptionService _encryptionService;

        public VaultDatabaseKeyService(EncryptionService encryptionService)
        {
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        public byte[] DeriveKey(VaultManifest manifest, string? passphrase, string? keyfilePath)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrWhiteSpace(manifest.SaltBase64))
                throw new SecurityException("Vault manifest salt is missing.");
            if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(keyfilePath))
                throw new SecurityException("Vault database key derivation requires passphrase or keyfile material.");

            byte[] salt;
            try
            {
                salt = Convert.FromBase64String(manifest.SaltBase64);
            }
            catch (FormatException ex)
            {
                throw new SecurityException("Vault manifest salt is not valid Base64.", ex);
            }

            byte[] secretMaterial = BuildSecretMaterial(passphrase, keyfilePath);
            string secretText = Convert.ToBase64String(secretMaterial);
            byte[]? stretchedKey = null;
            byte[]? bindingSalt = null;
            try
            {
                stretchedKey = _encryptionService.DeriveKey(secretText.AsSpan(), salt);
                bindingSalt = BuildBindingSalt(manifest, salt);
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, stretchedKey, 32, bindingSalt, HkdfInfo);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(secretMaterial);
                if (stretchedKey != null)
                    CryptographicOperations.ZeroMemory(stretchedKey);
                if (bindingSalt != null)
                    CryptographicOperations.ZeroMemory(bindingSalt);
            }
        }

        private static byte[] BuildSecretMaterial(string? passphrase, string? keyfilePath)
        {
            using var buffer = new MemoryStream();

            if (!string.IsNullOrEmpty(passphrase))
            {
                byte[] passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
                try
                {
                    buffer.Write(passphraseBytes, 0, passphraseBytes.Length);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passphraseBytes);
                }
            }

            if (!string.IsNullOrEmpty(keyfilePath))
            {
                byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
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
                throw new SecurityException("Vault database key derivation has no authentication material.");

            return buffer.ToArray();
        }

        private static byte[] BuildBindingSalt(VaultManifest manifest, byte[] manifestSalt)
        {
            string binding = string.Join(
                "|",
                manifest.VaultName ?? string.Empty,
                manifest.UsbBindingId ?? string.Empty,
                manifest.UsbBindingGuid ?? string.Empty,
                manifest.DeviceId ?? string.Empty,
                manifest.Guuid ?? string.Empty,
                manifest.RootContainerPath ?? string.Empty,
                manifest.ContainerPath ?? string.Empty,
                manifest.ObjectContainerPath ?? string.Empty);

            byte[] bindingBytes = Encoding.UTF8.GetBytes(binding);
            try
            {
                byte[] combined = new byte[manifestSalt.Length + bindingBytes.Length];
                Buffer.BlockCopy(manifestSalt, 0, combined, 0, manifestSalt.Length);
                Buffer.BlockCopy(bindingBytes, 0, combined, manifestSalt.Length, bindingBytes.Length);
                try
                {
                    return SHA256.HashData(combined);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(combined);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bindingBytes);
            }
        }
    }
}
