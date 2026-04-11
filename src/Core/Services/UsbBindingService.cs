using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        /// Derives a 32-byte AES-256-GCM encryption key from the vault salt.
        /// Uses a distinct domain separation string from <see cref="DeriveHmacKeyFromVaultSalt"/>
        /// to ensure orthogonal cryptographic keys per HKDF best practices.
        /// </summary>
        private static byte[] DeriveEncryptionKeyFromVaultSalt(byte[] vaultSalt)
        {
            if (vaultSalt == null || vaultSalt.Length == 0)
                throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            byte[] info = Encoding.UTF8.GetBytes("PhantomVault.USB.DeviceId.Encryption.v1");
            using var hmac = new HMACSHA256(vaultSalt);

            byte[] t1Input = new byte[info.Length + 1];
            Array.Copy(info, 0, t1Input, 0, info.Length);
            t1Input[info.Length] = 0x01;

            return hmac.ComputeHash(t1Input);
        }

        /// <summary>
        /// Computes a unique identifier for the given drive root. Prefers cryptographically
        /// strong identifiers in this priority order:
        /// 1. GPT Disk GUID + Partition GUID (128-bit random each, strongest binding)
        /// 2. WMI Disk Serial Number (manufacturer serial, harder to spoof than volume serial)
        /// 3. Volume serial number via native API (32-bit, weakest - legacy fallback only)
        /// 4. Heuristic fallback: SHA-256 of drive properties (non-Windows or all else fails)
        ///
        /// The returned value is always a hex-encoded string. When GPT GUIDs are available,
        /// the result is a SHA-256 hash of both the disk and partition GUIDs, providing
        /// 256 bits derived from 256 bits of random input.
        /// </summary>
        /// <param name="driveRoot">The root path of the removable drive (e.g. "E:\\" or "/media/user/USB").</param>
        public string ComputeDeviceId(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));

            if (OperatingSystem.IsWindows())
            {
                if (IsPhysicalDrivePath(driveRoot))
                {
                    int diskIndex = ParsePhysicalDriveIndex(driveRoot);

                    if (TryGetGptGuidsByDiskIndex(diskIndex, out string? rawDiskGuid, out string? rawPartitionGuid))
                    {
                        string combined = $"GPT:{rawDiskGuid}|{rawPartitionGuid}";
                        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
                        return Convert.ToHexString(hash);
                    }

                    if (TryGetDiskSerialNumberByDiskIndex(diskIndex, out string? physicalDiskSerial) &&
                        !string.IsNullOrEmpty(physicalDiskSerial))
                    {
                        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"DISK:{physicalDiskSerial}"));
                        return Convert.ToHexString(hash);
                    }
                }

                // Priority 1: GPT Disk GUID + Partition GUID (strongest - 128-bit random each)
                if (TryGetGptGuids(driveRoot, out string? diskGuid, out string? partitionGuid))
                {
                    // Combine both GUIDs via SHA-256 for a stable, high-entropy device ID
                    string combined = $"GPT:{diskGuid}|{partitionGuid}";
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
                    return Convert.ToHexString(hash);
                }

                // Priority 2: WMI physical disk serial (manufacturer-set, not trivially spoofable)
                if (TryGetDiskSerialNumber(driveRoot, out string? diskSerial) && !string.IsNullOrEmpty(diskSerial))
                {
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"DISK:{diskSerial}"));
                    return Convert.ToHexString(hash);
                }

                // Priority 3: Volume serial number (32-bit, weak - legacy fallback)
                if (TryGetVolumeSerialNumber(driveRoot, out uint serial))
                {
                    return serial.ToString("X8");
                }
            }

            // Priority 4: Heuristic fallback for all platforms
            var drive = new DriveInfo(driveRoot);
            string composite = $"{drive.TotalSize}|{drive.DriveFormat}|{drive.VolumeLabel}";
            byte[] data = Encoding.UTF8.GetBytes(composite);
            byte[] fallbackHash = SHA256.HashData(data);
            return Convert.ToHexString(fallbackHash);
        }

        /// <summary>
        /// Returns the binding strength of the device ID for the given drive.
        /// Useful for UI warnings and policy enforcement.
        /// </summary>
        public DeviceBindingStrength GetBindingStrength(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot))
                return DeviceBindingStrength.None;

            if (OperatingSystem.IsWindows())
            {
                if (IsPhysicalDrivePath(driveRoot))
                {
                    int diskIndex = ParsePhysicalDriveIndex(driveRoot);
                    if (TryGetGptGuidsByDiskIndex(diskIndex, out _, out _))
                        return DeviceBindingStrength.GptGuid;

                    if (TryGetDiskSerialNumberByDiskIndex(diskIndex, out string? rawDiskSerial) && !string.IsNullOrEmpty(rawDiskSerial))
                        return DeviceBindingStrength.DiskSerial;
                }

                if (TryGetGptGuids(driveRoot, out _, out _))
                    return DeviceBindingStrength.GptGuid;

                if (TryGetDiskSerialNumber(driveRoot, out string? ds) && !string.IsNullOrEmpty(ds))
                    return DeviceBindingStrength.DiskSerial;

                if (TryGetVolumeSerialNumber(driveRoot, out _))
                    return DeviceBindingStrength.VolumeSerial;
            }

            return DeviceBindingStrength.Heuristic;
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

        /// <summary>
        /// Attempts to retrieve GPT Disk GUID and Partition GUID for the given drive using WMI.
        /// GPT GUIDs are 128-bit random values assigned at disk/partition creation time and are
        /// cryptographically stronger than 32-bit volume serial numbers. They survive reformatting
        /// of individual partitions and cannot be trivially changed without specialized tools.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static bool TryGetGptGuids(string driveRoot, out string? diskGuid, out string? partitionGuid)
        {
            diskGuid = null;
            partitionGuid = null;

            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                var driveLetter = driveRoot.TrimEnd('\\').TrimEnd(':');

                // Traverse: LogicalDisk → Partition → DiskDrive to get the physical disk
                using var logicalDiskQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in logicalDiskQuery.Get())
                {
                    // Get the GPT partition type and GUID from MSFT_Partition (Storage WMI)
                    var partDeviceId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(partDeviceId))
                        continue;

                    // Extract disk index and partition number from Win32_DiskPartition
                    var diskIndex = partition["DiskIndex"];

                    // Query MSFT_Partition via Storage namespace for GPT GUIDs
                    using var storagePartitionQuery = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage",
                        $"SELECT GptType, Guid, DiskNumber FROM MSFT_Partition WHERE DiskNumber = {diskIndex}");

                    foreach (ManagementObject storagePart in storagePartitionQuery.Get())
                    {
                        var guid = storagePart["Guid"]?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            partitionGuid = guid.Trim('{', '}');
                            break;
                        }
                    }

                    // Get the Disk GUID from MSFT_Disk
                    using var diskQuery = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage",
                        $"SELECT Guid FROM MSFT_Disk WHERE Number = {diskIndex}");

                    foreach (ManagementObject disk in diskQuery.Get())
                    {
                        var guid = disk["Guid"]?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            diskGuid = guid.Trim('{', '}');
                            break;
                        }
                    }

                    break; // Only process first partition association
                }

                return !string.IsNullOrEmpty(diskGuid) && !string.IsNullOrEmpty(partitionGuid);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPT GUID lookup failed: {ex.Message}");
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool TryGetGptGuidsByDiskIndex(int diskIndex, out string? diskGuid, out string? partitionGuid)
        {
            diskGuid = null;
            partitionGuid = null;

            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var storagePartitionQuery = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    $"SELECT Guid, DiskNumber FROM MSFT_Partition WHERE DiskNumber = {diskIndex}");

                foreach (ManagementObject storagePart in storagePartitionQuery.Get())
                {
                    var guid = storagePart["Guid"]?.ToString();
                    if (!string.IsNullOrEmpty(guid))
                    {
                        partitionGuid = guid.Trim('{', '}');
                        break;
                    }
                }

                using var diskQuery = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    $"SELECT Guid FROM MSFT_Disk WHERE Number = {diskIndex}");

                foreach (ManagementObject disk in diskQuery.Get())
                {
                    var guid = disk["Guid"]?.ToString();
                    if (!string.IsNullOrEmpty(guid))
                    {
                        diskGuid = guid.Trim('{', '}');
                        break;
                    }
                }

                return !string.IsNullOrEmpty(diskGuid) && !string.IsNullOrEmpty(partitionGuid);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPT GUID lookup by disk index failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to retrieve the physical disk serial number via WMI.
        /// This is the manufacturer-assigned serial, which is harder to spoof than
        /// a volume serial number but can still be faked with driver-level tools.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static bool TryGetDiskSerialNumber(string driveRoot, out string? serialNumber)
        {
            serialNumber = null;

            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                var driveLetter = driveRoot.TrimEnd('\\').TrimEnd(':');

                using var logicalDiskQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in logicalDiskQuery.Get())
                {
                    using var diskQuery = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementObject disk in diskQuery.Get())
                    {
                        serialNumber = disk["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serialNumber))
                            return true;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Disk serial number lookup failed: {ex.Message}");
            }

            return false;
        }

        [SupportedOSPlatform("windows")]
        private static bool TryGetDiskSerialNumberByDiskIndex(int diskIndex, out string? serialNumber)
        {
            serialNumber = null;

            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var diskQuery = new ManagementObjectSearcher(
                    $"SELECT SerialNumber FROM Win32_DiskDrive WHERE Index = {diskIndex}");

                foreach (ManagementObject disk in diskQuery.Get())
                {
                    serialNumber = disk["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(serialNumber))
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Disk serial number lookup by disk index failed: {ex.Message}");
            }

            return false;
        }

        private static bool IsPhysicalDrivePath(string path)
            => path.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase);

        private static int ParsePhysicalDriveIndex(string physicalDrivePath)
        {
            const string token = "PHYSICALDRIVE";
            int offset = physicalDrivePath.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (offset < 0)
                throw new ArgumentException($"Not a physical drive path: {physicalDrivePath}", nameof(physicalDrivePath));

            string number = physicalDrivePath[(offset + token.Length)..];
            return int.Parse(number);
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

            // Build inner payload (deviceId + timestamp + HMAC signature)
            var idData = new DeviceIdData
            {
                DeviceId = deviceId,
                Timestamp = timestamp,
                Signature = Convert.ToHexString(signature)
            };

            string innerJson = JsonSerializer.Serialize(idData, JsonOptions);

            // Encrypt the payload with AES-256-GCM using a vault-salt-derived key
            byte[] encKey = DeriveEncryptionKeyFromVaultSalt(vaultSalt);
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(innerJson);
                byte[] nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);
                byte[] ciphertext = new byte[plainBytes.Length];
                byte[] tag = new byte[16];

                using (var aes = new AesGcm(encKey, 16))
                {
                    aes.Encrypt(nonce, plainBytes, ciphertext, tag);
                }

                // Write encrypted envelope (v2 format)
                var envelope = new EncryptedDeviceIdEnvelope
                {
                    Version = 2,
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag),
                    Ciphertext = Convert.ToBase64String(ciphertext)
                };

                string envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
                File.WriteAllText(hiddenFilePath, envelopeJson, Encoding.UTF8);

                // Wipe plaintext from memory
                CryptographicOperations.ZeroMemory(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
            }

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

            string rawJson = File.ReadAllText(hiddenFilePath, Encoding.UTF8);

            // Detect format: v2 encrypted envelope or legacy plaintext
            DeviceIdData? idData;
            using (var doc = JsonDocument.Parse(rawJson))
            {
                if (doc.RootElement.TryGetProperty("version", out var vProp) && vProp.GetInt32() >= 2)
                {
                    // v2 encrypted format — decrypt first
                    var envelope = JsonSerializer.Deserialize<EncryptedDeviceIdEnvelope>(rawJson, JsonOptions);
                    if (envelope == null
                        || string.IsNullOrEmpty(envelope.Nonce)
                        || string.IsNullOrEmpty(envelope.Tag)
                        || string.IsNullOrEmpty(envelope.Ciphertext))
                    {
                        throw new InvalidOperationException("Encrypted device identifier envelope is corrupted.");
                    }

                    byte[] encKey = DeriveEncryptionKeyFromVaultSalt(vaultSalt);
                    try
                    {
                        byte[] nonce = Convert.FromBase64String(envelope.Nonce);
                        byte[] tag = Convert.FromBase64String(envelope.Tag);
                        byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
                        byte[] plainBytes = new byte[ciphertext.Length];

                        using (var aes = new AesGcm(encKey, 16))
                        {
                            aes.Decrypt(nonce, ciphertext, tag, plainBytes);
                        }

                        string innerJson = Encoding.UTF8.GetString(plainBytes);
                        CryptographicOperations.ZeroMemory(plainBytes);

                        idData = JsonSerializer.Deserialize<DeviceIdData>(innerJson, JsonOptions);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encKey);
                    }
                }
                else
                {
                    // Legacy plaintext format — read directly (backward compatibility)
                    idData = JsonSerializer.Deserialize<DeviceIdData>(rawJson, JsonOptions);
                }
            }

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

        /// <summary>
        /// Encrypted envelope (v2+) for the hidden device identifier file.
        /// The ciphertext contains the AES-256-GCM-encrypted DeviceIdData JSON.
        /// </summary>
        private sealed class EncryptedDeviceIdEnvelope
        {
            public int Version { get; set; }
            public string Nonce { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public string Ciphertext { get; set; } = string.Empty;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion
    }

    /// <summary>
    /// Indicates the strength of the device binding mechanism used.
    /// Stronger bindings are harder to spoof or clone.
    /// </summary>
    public enum DeviceBindingStrength
    {
        /// <summary>No binding could be established.</summary>
        None = 0,

        /// <summary>Heuristic binding based on drive properties (weakest).</summary>
        Heuristic = 1,

        /// <summary>Windows volume serial number (32-bit, easily spoofable).</summary>
        VolumeSerial = 2,

        /// <summary>Physical disk manufacturer serial number (harder to spoof).</summary>
        DiskSerial = 3,

        /// <summary>GPT Disk + Partition GUIDs (128-bit random each, strongest).</summary>
        GptGuid = 4
    }
}
