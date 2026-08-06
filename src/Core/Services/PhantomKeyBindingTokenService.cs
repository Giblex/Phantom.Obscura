using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    public sealed class PhantomKeyBindingTokenService
    {
        private const int TokenVersion = 1;
        private const string Magic = "PHANTOMKEY-BINDING-TOKEN";
        private static readonly byte[] FileAadPrefix = Encoding.UTF8.GetBytes("PhantomVault.PhantomKey.BindingToken.File.v1");

        private readonly IPhantomKeyUsbBindingProvider _usbBindingProvider;

        public PhantomKeyBindingTokenService(IPhantomKeyUsbBindingProvider? usbBindingProvider = null)
        {
            _usbBindingProvider = usbBindingProvider ?? CreateDefaultUsbBindingProvider();
        }

        public string GetDefaultTokenPath(string driveRoot)
            => PhantomDeviceLayout.GetPhantomKeyBindingTokenPath(driveRoot);

        public PhantomKeyBindingTokenEnvelope CreateOrRotate(
            string driveRoot,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string? computerDeviceId = null,
            long? rotationCounter = null)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
                throw new ArgumentException("Drive root is required.", nameof(driveRoot));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            string tokenPath = GetDefaultTokenPath(driveRoot);
            string directory = PhantomDeviceLayout.EnsurePhantomRoot(driveRoot);
            Directory.CreateDirectory(directory);

            byte[] tokenKey = DeriveTokenKey(manifest, passphrase, keyfilePath);
            byte[]? fieldKey = null;
            try
            {
                fieldKey = DeriveFieldKey(manifest, passphrase, keyfilePath);
                var payload = BuildPayload(driveRoot, manifest, fieldKey, keyfilePath, computerDeviceId, rotationCounter);
                byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
                try
                {
                    byte[] nonce = RandomNumberGenerator.GetBytes(12);
                    byte[] ciphertext = new byte[plaintext.Length];
                    byte[] tag = new byte[16];
                    byte[] aad = BuildFileAad(manifest);

                    using (var aes = new AesGcm(tokenKey, 16))
                    {
                        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
                    }

                    var envelope = new PhantomKeyBindingTokenEnvelope
                    {
                        Magic = Magic,
                        Version = TokenVersion,
                        Algorithm = "AES-256-GCM",
                        Kdf = "HKDF-SHA256",
                        NonceBase64 = Convert.ToBase64String(nonce),
                        TagBase64 = Convert.ToBase64String(tag),
                        CiphertextBase64 = Convert.ToBase64String(ciphertext)
                    };

                    File.WriteAllText(tokenPath, JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8);

                    if (OperatingSystem.IsWindows())
                    {
                        File.SetAttributes(tokenPath, FileAttributes.Hidden | FileAttributes.System);
                    }

                    CryptographicOperations.ZeroMemory(ciphertext);
                    CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(nonce);

                    return envelope;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tokenKey);
                if (fieldKey != null)
                    CryptographicOperations.ZeroMemory(fieldKey);
            }
        }

        public PhantomKeyBindingValidationResult Validate(
            string driveRoot,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string? computerDeviceId = null)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.MissingInput, "Drive root is required.");
            if (manifest == null)
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.MissingInput, "Vault manifest is required.");

            string tokenPath = GetDefaultTokenPath(driveRoot);
            if (!File.Exists(tokenPath))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.TokenMissing, "PhantomKey binding token was not found.");

            PhantomKeyBindingTokenPayload payload;
            byte[] tokenKey;
            try
            {
                tokenKey = DeriveTokenKey(manifest, passphrase, keyfilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or SecurityException or IOException)
            {
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.SecretUnavailable, ex.Message);
            }

            try
            {
                payload = ReadPayload(tokenPath, manifest, tokenKey);
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or InvalidOperationException)
            {
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.TokenInvalid, ex.Message);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tokenKey);
            }

            using var expectedKey = new SensitiveBuffer(DeriveFieldKey(manifest, passphrase, keyfilePath));
            var expected = BuildPayload(driveRoot, manifest, expectedKey.Bytes, keyfilePath, computerDeviceId, payload.RotationCounter);

            if (payload.Version != TokenVersion)
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.TokenInvalid, "PhantomKey binding token version is unsupported.");
            if (!FixedEquals(payload.VaultBindingHashBase64, expected.VaultBindingHashBase64))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.VaultMismatch, "PhantomKey binding token belongs to a different vault.");
            if (!FixedEquals(payload.UsbBindingHashBase64, expected.UsbBindingHashBase64))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.UsbMismatch, "PhantomKey binding token belongs to a different USB device.");
            if (!FixedEquals(payload.DeviceBindingHashBase64, expected.DeviceBindingHashBase64))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.DeviceMismatch, "PhantomKey binding token belongs to a different vault device binding.");
            if (!FixedEquals(payload.ComputerBindingHashBase64, expected.ComputerBindingHashBase64))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.ComputerMismatch, "PhantomKey binding token belongs to a different computer/device.");
            if (!FixedEquals(payload.KeyfileNameHashBase64, expected.KeyfileNameHashBase64))
                return PhantomKeyBindingValidationResult.Fail(PhantomKeyBindingValidationFailure.KeyfileMismatch, "PhantomKey binding token belongs to a different keyfile.");

            return PhantomKeyBindingValidationResult.Success(payload);
        }

        public void ValidateOrThrow(
            string driveRoot,
            VaultManifest manifest,
            string? passphrase,
            string? keyfilePath,
            string? computerDeviceId = null)
        {
            var result = Validate(driveRoot, manifest, passphrase, keyfilePath, computerDeviceId);
            if (!result.IsValid)
                throw new SecurityException(result.Message);
        }

        private PhantomKeyBindingTokenPayload ReadPayload(string tokenPath, VaultManifest manifest, byte[] tokenKey)
        {
            var envelope = JsonSerializer.Deserialize<PhantomKeyBindingTokenEnvelope>(File.ReadAllText(tokenPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidOperationException("PhantomKey binding token is empty.");
            if (!string.Equals(envelope.Magic, Magic, StringComparison.Ordinal))
                throw new InvalidOperationException("PhantomKey binding token magic is invalid.");
            if (envelope.Version != TokenVersion)
                throw new InvalidOperationException("PhantomKey binding token version is unsupported.");
            if (!string.Equals(envelope.Algorithm, "AES-256-GCM", StringComparison.Ordinal))
                throw new InvalidOperationException("PhantomKey binding token algorithm is unsupported.");

            byte[] nonce = Convert.FromBase64String(envelope.NonceBase64);
            byte[] tag = Convert.FromBase64String(envelope.TagBase64);
            byte[] ciphertext = Convert.FromBase64String(envelope.CiphertextBase64);
            byte[] plaintext = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(tokenKey, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildFileAad(manifest));
                return JsonSerializer.Deserialize<PhantomKeyBindingTokenPayload>(plaintext, JsonOptions)
                    ?? throw new InvalidOperationException("PhantomKey binding token payload is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private PhantomKeyBindingTokenPayload BuildPayload(
            string driveRoot,
            VaultManifest manifest,
            byte[] fieldKey,
            string? keyfilePath,
            string? computerDeviceId,
            long? rotationCounter)
        {
            string usbId = _usbBindingProvider.ComputeDeviceId(driveRoot);
            string usbBinding = $"{NormalizeIdentifier(manifest.UsbBindingId)}|{NormalizeIdentifier(usbId)}";
            string vaultId = NormalizeIdentifier(manifest.Guuid ?? manifest.VaultName);
            string deviceId = NormalizeIdentifier(manifest.DeviceId);
            string computerId = NormalizeIdentifier(computerDeviceId ?? manifest.DeviceId ?? Environment.MachineName);
            string keyfileName = NormalizeIdentifier(Path.GetFileName(CompositeKeyfilePath.GetPrimaryPath(keyfilePath)) ?? string.Empty);

            return new PhantomKeyBindingTokenPayload
            {
                Version = TokenVersion,
                CreatedUtc = DateTimeOffset.UtcNow,
                RotationCounter = rotationCounter ?? manifest.KeyRotationCount,
                ManifestSequence = manifest.ManifestSequence,
                VaultBindingHashBase64 = HashField(fieldKey, "vault", vaultId),
                UsbBindingHashBase64 = HashField(fieldKey, "usb", usbBinding),
                DeviceBindingHashBase64 = HashField(fieldKey, "device", deviceId),
                ComputerBindingHashBase64 = HashField(fieldKey, "computer", computerId),
                KeyfileNameHashBase64 = HashField(fieldKey, "keyfile-name", keyfileName)
            };
        }

        private static byte[] DeriveTokenKey(VaultManifest manifest, string? passphrase, string? keyfilePath)
        {
            byte[] secretMaterial = BuildSecretMaterial(passphrase, keyfilePath);
            byte[] salt = ReadManifestSalt(manifest);
            try
            {
                byte[] digest = SHA256.HashData(secretMaterial);
                byte[] info = Encoding.UTF8.GetBytes($"PhantomVault.PhantomKey.BindingToken.Key.v1|{NormalizeIdentifier(manifest.Guuid ?? manifest.VaultName)}");
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, digest, 32, salt, info);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretMaterial);
                CryptographicOperations.ZeroMemory(salt);
            }
        }

        private static byte[] DeriveFieldKey(VaultManifest manifest, string? passphrase, string? keyfilePath)
        {
            byte[] secretMaterial = BuildSecretMaterial(passphrase, keyfilePath);
            byte[] salt = ReadManifestSalt(manifest);
            try
            {
                byte[] digest = SHA256.HashData(secretMaterial);
                byte[] info = Encoding.UTF8.GetBytes("PhantomVault.PhantomKey.BindingToken.Fields.v1");
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, digest, 32, salt, info);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretMaterial);
                CryptographicOperations.ZeroMemory(salt);
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
                throw new SecurityException("PhantomKey binding token requires passphrase and/or keyfile material.");

            return buffer.ToArray();
        }

        private static byte[] ReadManifestSalt(VaultManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.SaltBase64))
                throw new InvalidOperationException("Vault manifest salt is required for PhantomKey binding tokens.");

            return Convert.FromBase64String(manifest.SaltBase64);
        }

        private static string HashField(byte[] fieldKey, string label, string value)
        {
            using var hmac = new HMACSHA256(fieldKey);
            byte[] data = Encoding.UTF8.GetBytes($"{label}:{value}");
            try
            {
                return Convert.ToBase64String(hmac.ComputeHash(data));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(data);
            }
        }

        private static byte[] BuildFileAad(VaultManifest manifest)
        {
            byte[] manifestBytes = Encoding.UTF8.GetBytes(
                $"{NormalizeIdentifier(manifest.Guuid ?? manifest.VaultName)}|{manifest.ManifestSequence}|{manifest.KeyRotationCount}");
            byte[] aad = new byte[FileAadPrefix.Length + manifestBytes.Length];
            Buffer.BlockCopy(FileAadPrefix, 0, aad, 0, FileAadPrefix.Length);
            Buffer.BlockCopy(manifestBytes, 0, aad, FileAadPrefix.Length, manifestBytes.Length);
            CryptographicOperations.ZeroMemory(manifestBytes);
            return aad;
        }

        private static string NormalizeIdentifier(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static IPhantomKeyUsbBindingProvider CreateDefaultUsbBindingProvider()
        {
#if ANDROID
            return new PortablePhantomKeyUsbBindingProvider();
#else
            return new DesktopPhantomKeyUsbBindingProvider();
#endif
        }

        private static bool FixedEquals(string? leftBase64, string? rightBase64)
        {
            if (string.IsNullOrEmpty(leftBase64) || string.IsNullOrEmpty(rightBase64))
                return false;

            byte[] left = Convert.FromBase64String(leftBase64);
            byte[] right = Convert.FromBase64String(rightBase64);
            try
            {
                return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(left);
                CryptographicOperations.ZeroMemory(right);
            }
        }

        private sealed class SensitiveBuffer : IDisposable
        {
            public SensitiveBuffer(byte[] bytes)
            {
                Bytes = bytes;
            }

            public byte[] Bytes { get; }

            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(Bytes);
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public interface IPhantomKeyUsbBindingProvider
    {
        string ComputeDeviceId(string driveRoot);
    }

#if !ANDROID
    internal sealed class DesktopPhantomKeyUsbBindingProvider : IPhantomKeyUsbBindingProvider
    {
        private readonly UsbBindingService _usbBindingService = new();

        public string ComputeDeviceId(string driveRoot)
            => _usbBindingService.ComputeDeviceId(driveRoot);
    }
#endif

    internal sealed class PortablePhantomKeyUsbBindingProvider : IPhantomKeyUsbBindingProvider
    {
        public string ComputeDeviceId(string driveRoot)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
                throw new ArgumentException("Drive root is required.", nameof(driveRoot));

            var drive = new DriveInfo(driveRoot);
            string root = Path.GetPathRoot(driveRoot) ?? driveRoot;
            string composite = $"{root}|{drive.TotalSize}|{drive.DriveFormat}|{drive.VolumeLabel}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composite)));
        }
    }

    public sealed class PhantomKeyBindingTokenEnvelope
    {
        [JsonPropertyName("magic")]
        public string Magic { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = string.Empty;

        [JsonPropertyName("kdf")]
        public string Kdf { get; set; } = string.Empty;

        [JsonPropertyName("nonce")]
        public string NonceBase64 { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public string TagBase64 { get; set; } = string.Empty;

        [JsonPropertyName("ciphertext")]
        public string CiphertextBase64 { get; set; } = string.Empty;
    }

    public sealed class PhantomKeyBindingTokenPayload
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("createdUtc")]
        public DateTimeOffset CreatedUtc { get; set; }

        [JsonPropertyName("rotationCounter")]
        public long RotationCounter { get; set; }

        [JsonPropertyName("manifestSequence")]
        public long ManifestSequence { get; set; }

        [JsonPropertyName("vaultBindingHash")]
        public string VaultBindingHashBase64 { get; set; } = string.Empty;

        [JsonPropertyName("usbBindingHash")]
        public string UsbBindingHashBase64 { get; set; } = string.Empty;

        [JsonPropertyName("deviceBindingHash")]
        public string DeviceBindingHashBase64 { get; set; } = string.Empty;

        [JsonPropertyName("computerBindingHash")]
        public string ComputerBindingHashBase64 { get; set; } = string.Empty;

        [JsonPropertyName("keyfileNameHash")]
        public string KeyfileNameHashBase64 { get; set; } = string.Empty;
    }

    public sealed class PhantomKeyBindingValidationResult
    {
        private PhantomKeyBindingValidationResult(
            bool isValid,
            PhantomKeyBindingValidationFailure failure,
            string message,
            PhantomKeyBindingTokenPayload? payload)
        {
            IsValid = isValid;
            Failure = failure;
            Message = message;
            Payload = payload;
        }

        public bool IsValid { get; }
        public PhantomKeyBindingValidationFailure Failure { get; }
        public string Message { get; }
        public PhantomKeyBindingTokenPayload? Payload { get; }

        public static PhantomKeyBindingValidationResult Success(PhantomKeyBindingTokenPayload payload)
            => new(true, PhantomKeyBindingValidationFailure.None, "PhantomKey binding token is valid.", payload);

        public static PhantomKeyBindingValidationResult Fail(PhantomKeyBindingValidationFailure failure, string message)
            => new(false, failure, message, null);
    }

    public enum PhantomKeyBindingValidationFailure
    {
        None = 0,
        MissingInput,
        TokenMissing,
        TokenInvalid,
        SecretUnavailable,
        VaultMismatch,
        UsbMismatch,
        DeviceMismatch,
        ComputerMismatch,
        KeyfileMismatch
    }
}
