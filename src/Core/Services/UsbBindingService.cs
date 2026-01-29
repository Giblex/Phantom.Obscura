using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Responsible for computing a stable identifier for a removable drive and
    /// verifying that it matches a previously recorded identifier. This is
    /// used to bind a vault to a specific USB device so it cannot simply be
    /// copied onto another drive. The implementation is cross‑platform:
    /// on Windows it uses the volume serial number via a native API, and
    /// on Unix-like systems it derives an identifier from file system
    /// properties. 
    /// 
    /// For higher assurance, this service also supports a hidden per-device
    /// identifier file (.phantom_device_id) that contains a cryptographically
    /// random ID with an HMAC signature. This provides stronger protection
    /// against drive cloning since the hidden file cannot be easily replicated.
    /// </summary>
    public sealed class UsbBindingService
    {
        private const string HiddenIdFileName = ".phantom_device_id";

        /// <summary>
        /// Derives a per-vault HMAC key from the vault's salt.
        /// This ensures each vault has a unique HMAC key that cannot be reused across vaults.
        /// Uses HKDF (HMAC-based Key Derivation Function) as specified in RFC 5869.
        /// </summary>
        /// <param name="vaultSalt">The vault's unique salt (from VaultManifest.SaltBase64).</param>
        /// <returns>A 32-byte HMAC key derived from the vault salt.</returns>
        private static byte[] DeriveHmacKeyFromVaultSalt(byte[] vaultSalt)
        {
            if (vaultSalt == null || vaultSalt.Length == 0)
                throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            // Use HKDF to derive a key from the vault salt
            // Info parameter provides domain separation
            byte[] info = Encoding.UTF8.GetBytes("PhantomVault.USB.DeviceBinding.v1");

            // For HKDF, we use the vault salt as the IKM (Input Key Material)
            // We don't have a master key here, so we use HKDF-Expand only
            // This is acceptable because the vault salt is already high-entropy
            using var hmac = new HMACSHA256(vaultSalt);

            // HKDF-Expand: T(1) = HMAC(PRK, info | 0x01)
            byte[] t1Input = new byte[info.Length + 1];
            Array.Copy(info, 0, t1Input, 0, info.Length);
            t1Input[info.Length] = 0x01;

            return hmac.ComputeHash(t1Input);
        }

        /// <summary>
        /// Computes a unique identifier for the given drive root. On Windows
        /// this uses the volume serial number returned by GetVolumeInformation.
        /// On non‑Windows platforms the method combines the drive's total
        /// capacity, file system format and volume label and hashes the
        /// result with SHA‑256. The returned value is hex encoded.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive (e.g. "E:\\" or "/media/user/USB").</param>
        public string ComputeDeviceId(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            if (OperatingSystem.IsWindows())
            {
                // Windows: use native API to get the volume serial number. If this
                // fails, fall back to hashing drive info like on Linux.
                if (TryGetVolumeSerialNumber(driveRoot, out uint serial))
                {
                    return serial.ToString("X8");
                }
            }
            // Fallback for all platforms (including Windows if API call fails).
            var drive = new DriveInfo(driveRoot);
            string composite = $"{drive.TotalSize}|{drive.DriveFormat}|{drive.VolumeLabel}";
            byte[] data = Encoding.UTF8.GetBytes(composite);
            byte[] hash = SHA256.HashData(data);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Verifies that the removable drive at the given root has the expected
        /// device identifier. If the IDs do not match an exception is thrown.
        /// </summary>
        public void EnsureBoundToDevice(string driveRoot, string expectedDeviceId)
        {
            string actual = ComputeDeviceId(driveRoot);
            if (!string.Equals(actual, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The vault is bound to device ID {expectedDeviceId} but current device has ID {actual}. Please use the original USB drive.");
            }
        }

        #region Native Windows interop
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetVolumeInformation(
            string lpRootPathName,
            StringBuilder lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer,
            uint nFileSystemNameSize);

        private static bool TryGetVolumeSerialNumber(string rootPath, out uint serial)
        {
            serial = 0;
            try
            {
                const uint bufferLength = 260;
                var volName = new StringBuilder((int)bufferLength);
                var fsName = new StringBuilder((int)bufferLength);
                uint maxCompLength, fsFlags;
                if (GetVolumeInformation(rootPath, volName, bufferLength, out serial, out maxCompLength, out fsFlags, fsName, bufferLength))
                {
                    return true;
                }
            }
            catch
            {
                // ignore and fall back
            }
            return false;
        }
        #endregion

        #region Hidden Device ID File (High Assurance Binding)

        /// <summary>
        /// Creates a hidden device identifier file on the drive for high-assurance binding.
        /// This file contains a cryptographically random ID with an HMAC signature that
        /// cannot be easily replicated when cloning a drive.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive.</param>
        /// <param name="vaultSalt">The vault's unique salt for deriving the HMAC key.</param>
        /// <returns>The device ID stored in the hidden file.</returns>
        public string CreateHiddenDeviceId(string driveRoot, byte[] vaultSalt)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            if (vaultSalt == null || vaultSalt.Length == 0) throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            string hiddenFilePath = Path.Combine(driveRoot, HiddenIdFileName);

            // Generate a cryptographically random device ID
            byte[] randomId = new byte[32];
            RandomNumberGenerator.Fill(randomId);
            string deviceId = Convert.ToHexString(randomId);

            // Create timestamp for freshness
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Derive per-vault HMAC key
            byte[] hmacKey = DeriveHmacKeyFromVaultSalt(vaultSalt);

            // Compute HMAC signature over ID + timestamp
            byte[] dataToSign = Encoding.UTF8.GetBytes($"{deviceId}|{timestamp}");
            byte[] signature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                signature = hmac.ComputeHash(dataToSign);
            }

            // Wipe the derived key from memory
            CryptographicOperations.ZeroMemory(hmacKey);

            // Write to hidden file
            var idData = new DeviceIdData
            {
                DeviceId = deviceId,
                Timestamp = timestamp,
                Signature = Convert.ToHexString(signature)
            };

            string json = JsonSerializer.Serialize(idData, JsonOptions);
            File.WriteAllText(hiddenFilePath, json, Encoding.UTF8);

            // Set hidden attribute on Windows
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(hiddenFilePath, FileAttributes.Hidden | FileAttributes.System);
            }

            return deviceId;
        }

        /// <summary>
        /// Reads and validates the hidden device identifier from the drive.
        /// Throws if the file is missing, corrupted, or has an invalid signature.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive.</param>
        /// <param name="vaultSalt">The vault's unique salt for deriving the HMAC key.</param>
        /// <returns>The validated device ID.</returns>
        public string ReadHiddenDeviceId(string driveRoot, byte[] vaultSalt)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            if (vaultSalt == null || vaultSalt.Length == 0) throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            string hiddenFilePath = Path.Combine(driveRoot, HiddenIdFileName);

            if (!File.Exists(hiddenFilePath))
            {
                throw new FileNotFoundException("Hidden device identifier file not found. The drive may not be bound or was cloned without the hidden file.");
            }

            string json = File.ReadAllText(hiddenFilePath, Encoding.UTF8);
            var idData = JsonSerializer.Deserialize<DeviceIdData>(json, JsonOptions);

            if (idData == null || string.IsNullOrEmpty(idData.DeviceId) || string.IsNullOrEmpty(idData.Signature))
            {
                throw new InvalidOperationException("Hidden device identifier file is corrupted.");
            }

            // Validate timestamp freshness (max 2 years old)
            long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long maxAge = 2 * 365 * 24 * 60 * 60; // 2 years in seconds
            if (currentTimestamp - idData.Timestamp > maxAge)
            {
                throw new InvalidOperationException(
                    "Device identifier file is too old (>2 years). Please rebind the vault to this drive.");
            }

            // Derive per-vault HMAC key
            byte[] hmacKey = DeriveHmacKeyFromVaultSalt(vaultSalt);

            // Verify HMAC signature
            byte[] dataToVerify = Encoding.UTF8.GetBytes($"{idData.DeviceId}|{idData.Timestamp}");
            byte[] expectedSignature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                expectedSignature = hmac.ComputeHash(dataToVerify);
            }

            // Wipe the derived key from memory
            CryptographicOperations.ZeroMemory(hmacKey);

            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(idData.Signature),
                expectedSignature))
            {
                throw new InvalidOperationException("Hidden device identifier signature is invalid. The file may have been tampered with.");
            }

            return idData.DeviceId;
        }

        /// <summary>
        /// Checks if a hidden device identifier file exists on the drive.
        /// </summary>
        public bool HasHiddenDeviceId(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) return false;
            return File.Exists(Path.Combine(driveRoot, HiddenIdFileName));
        }

        /// <summary>
        /// Computes a combined device ID using both the volume serial and hidden file.
        /// This provides the strongest binding guarantee.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive.</param>
        /// <param name="vaultSalt">The vault's unique salt for deriving the HMAC key.</param>
        /// <returns>Combined device ID (volume + hidden).</returns>
        public string ComputeHighAssuranceDeviceId(string driveRoot, byte[] vaultSalt)
        {
            string volumeId = ComputeDeviceId(driveRoot);
            string hiddenId = ReadHiddenDeviceId(driveRoot, vaultSalt);

            // Combine both IDs with SHA256 for a unified identifier
            byte[] combined = Encoding.UTF8.GetBytes($"{volumeId}|{hiddenId}");
            byte[] hash = SHA256.HashData(combined);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Initializes high-assurance binding on a new drive.
        /// Creates the hidden ID file and returns the combined device ID.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive.</param>
        /// <param name="vaultSalt">The vault's unique salt for deriving the HMAC key.</param>
        /// <returns>Combined device ID (volume + hidden).</returns>
        public string InitializeHighAssuranceBinding(string driveRoot, byte[] vaultSalt)
        {
            CreateHiddenDeviceId(driveRoot, vaultSalt);
            return ComputeHighAssuranceDeviceId(driveRoot, vaultSalt);
        }

        private sealed class DeviceIdData
        {
            public string DeviceId { get; set; } = string.Empty;
            public long Timestamp { get; set; }
            public string Signature { get; set; } = string.Empty;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion
    }
}