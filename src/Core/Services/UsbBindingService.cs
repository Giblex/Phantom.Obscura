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

    public sealed class UsbBindingService
    {
        private static byte[] DeriveHmacKeyFromVaultSalt(byte[] vaultSalt)
        {
            if (vaultSalt == null || vaultSalt.Length == 0)
                throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            byte[] info = Encoding.UTF8.GetBytes("PhantomVault.USB.DeviceBinding.v1");

            using var hmac = new HMACSHA256(vaultSalt);

            byte[] t1Input = new byte[info.Length + 1];
            Array.Copy(info, 0, t1Input, 0, info.Length);
            t1Input[info.Length] = 0x01;

            return hmac.ComputeHash(t1Input);
        }

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

                if (TryGetGptGuids(driveRoot, out string? diskGuid, out string? partitionGuid))
                {

                    string combined = $"GPT:{diskGuid}|{partitionGuid}";
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
                    return Convert.ToHexString(hash);
                }

                if (TryGetDiskSerialNumber(driveRoot, out string? diskSerial) && !string.IsNullOrEmpty(diskSerial))
                {
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"DISK:{diskSerial}"));
                    return Convert.ToHexString(hash);
                }

                if (TryGetVolumeSerialNumber(driveRoot, out uint serial))
                {
                    return serial.ToString("X8");
                }
            }

            var drive = new DriveInfo(driveRoot);
            string composite = $"{drive.TotalSize}|{drive.DriveFormat}|{drive.VolumeLabel}";
            byte[] data = Encoding.UTF8.GetBytes(composite);
            byte[] fallbackHash = SHA256.HashData(data);
            return Convert.ToHexString(fallbackHash);
        }

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

            }
            return false;
        }

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

                using var logicalDiskQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in logicalDiskQuery.Get())
                {

                    var partDeviceId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(partDeviceId))
                        continue;

                    var diskIndex = partition["DiskIndex"];

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

                    break;
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

        public string CreateHiddenDeviceId(string driveRoot, byte[] vaultSalt)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            if (vaultSalt == null || vaultSalt.Length == 0) throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            PhantomDeviceLayout.EnsurePhantomRoot(driveRoot);
            string hiddenFilePath = PhantomDeviceLayout.GetDeviceIdPath(driveRoot);

            byte[] randomId = new byte[32];
            RandomNumberGenerator.Fill(randomId);
            string deviceId = Convert.ToHexString(randomId);

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            byte[] hmacKey = DeriveHmacKeyFromVaultSalt(vaultSalt);

            byte[] dataToSign = Encoding.UTF8.GetBytes($"{deviceId}|{timestamp}");
            byte[] signature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                signature = hmac.ComputeHash(dataToSign);
            }

            CryptographicOperations.ZeroMemory(hmacKey);

            var idData = new DeviceIdData
            {
                DeviceId = deviceId,
                Timestamp = timestamp,
                Signature = Convert.ToHexString(signature)
            };

            string innerJson = JsonSerializer.Serialize(idData, JsonOptions);

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

                var envelope = new EncryptedDeviceIdEnvelope
                {
                    Version = 2,
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag),
                    Ciphertext = Convert.ToBase64String(ciphertext)
                };

                string envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
                File.WriteAllText(hiddenFilePath, envelopeJson, Encoding.UTF8);

                CryptographicOperations.ZeroMemory(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
            }

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(hiddenFilePath, FileAttributes.Hidden | FileAttributes.System);
            }

            return deviceId;
        }

        public string ReadHiddenDeviceId(string driveRoot, byte[] vaultSalt)
        {
            if (string.IsNullOrEmpty(driveRoot)) throw new ArgumentException("Drive root must not be null or empty", nameof(driveRoot));
            if (vaultSalt == null || vaultSalt.Length == 0) throw new ArgumentException("Vault salt must not be null or empty", nameof(vaultSalt));

            string hiddenFilePath = PhantomDeviceLayout.GetDeviceIdPath(driveRoot);

            if (!File.Exists(hiddenFilePath))
            {
                throw new FileNotFoundException("Hidden device identifier file not found. The drive may not be bound or was cloned without the hidden file.");
            }

            string rawJson = File.ReadAllText(hiddenFilePath, Encoding.UTF8);

            DeviceIdData? idData;
            using (var doc = JsonDocument.Parse(rawJson))
            {
                if (doc.RootElement.TryGetProperty("version", out var vProp) && vProp.GetInt32() >= 2)
                {

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

                    idData = JsonSerializer.Deserialize<DeviceIdData>(rawJson, JsonOptions);
                }
            }

            if (idData == null || string.IsNullOrEmpty(idData.DeviceId) || string.IsNullOrEmpty(idData.Signature))
            {
                throw new InvalidOperationException("Hidden device identifier file is corrupted.");
            }

            long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long maxAge = 2 * 365 * 24 * 60 * 60;
            if (currentTimestamp - idData.Timestamp > maxAge)
            {
                throw new InvalidOperationException(
                    "Device identifier file is too old (>2 years). Please rebind the vault to this drive.");
            }

            byte[] hmacKey = DeriveHmacKeyFromVaultSalt(vaultSalt);

            byte[] dataToVerify = Encoding.UTF8.GetBytes($"{idData.DeviceId}|{idData.Timestamp}");
            byte[] expectedSignature;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                expectedSignature = hmac.ComputeHash(dataToVerify);
            }

            CryptographicOperations.ZeroMemory(hmacKey);

            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(idData.Signature),
                expectedSignature))
            {
                throw new InvalidOperationException("Hidden device identifier signature is invalid. The file may have been tampered with.");
            }

            return idData.DeviceId;
        }

        public bool HasHiddenDeviceId(string driveRoot)
        {
            if (string.IsNullOrEmpty(driveRoot)) return false;
            return File.Exists(PhantomDeviceLayout.GetDeviceIdPath(driveRoot));
        }

        public string ComputeHighAssuranceDeviceId(string driveRoot, byte[] vaultSalt)
        {
            string volumeId = ComputeDeviceId(driveRoot);
            string hiddenId = ReadHiddenDeviceId(driveRoot, vaultSalt);

            byte[] combined = Encoding.UTF8.GetBytes($"{volumeId}|{hiddenId}");
            byte[] hash = SHA256.HashData(combined);
            return Convert.ToHexString(hash);
        }

        public string InitializeHighAssuranceBinding(string driveRoot, byte[] vaultSalt)
        {
            CreateHiddenDeviceId(driveRoot, vaultSalt);
            return ComputeHighAssuranceDeviceId(driveRoot, vaultSalt);
        }

        public string? RotateHiddenDeviceId(string driveRoot, byte[] newVaultSalt, string knownDeviceId)
        {
            if (string.IsNullOrEmpty(driveRoot)) return null;
            if (newVaultSalt == null || newVaultSalt.Length == 0) return null;
            if (string.IsNullOrEmpty(knownDeviceId)) return null;

            try
            {
                PhantomDeviceLayout.EnsurePhantomRoot(driveRoot);
                string hiddenFilePath = PhantomDeviceLayout.GetDeviceIdPath(driveRoot);
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                byte[] hmacKey = DeriveHmacKeyFromVaultSalt(newVaultSalt);
                byte[] dataToSign = Encoding.UTF8.GetBytes($"{knownDeviceId}|{timestamp}");
                byte[] signature;
                using (var hmac = new HMACSHA256(hmacKey))
                    signature = hmac.ComputeHash(dataToSign);
                CryptographicOperations.ZeroMemory(hmacKey);

                var idData = new DeviceIdData
                {
                    DeviceId = knownDeviceId,
                    Timestamp = timestamp,
                    Signature = Convert.ToHexString(signature)
                };

                string innerJson = JsonSerializer.Serialize(idData, JsonOptions);
                byte[] encKey = DeriveEncryptionKeyFromVaultSalt(newVaultSalt);
                try
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(innerJson);
                    byte[] nonce = new byte[12];
                    RandomNumberGenerator.Fill(nonce);
                    byte[] ciphertext = new byte[plainBytes.Length];
                    byte[] tag = new byte[16];

                    using (var aes = new AesGcm(encKey, 16))
                        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

                    var envelope = new EncryptedDeviceIdEnvelope
                    {
                        Version = 2,
                        Nonce = Convert.ToBase64String(nonce),
                        Tag = Convert.ToBase64String(tag),
                        Ciphertext = Convert.ToBase64String(ciphertext)
                    };

                    if (File.Exists(hiddenFilePath) && OperatingSystem.IsWindows())
                    {
                        var attrs = File.GetAttributes(hiddenFilePath);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(hiddenFilePath, attrs & ~FileAttributes.ReadOnly);
                    }

                    File.WriteAllText(hiddenFilePath, JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8);
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(encKey);
                }

                if (OperatingSystem.IsWindows())
                    File.SetAttributes(hiddenFilePath, FileAttributes.Hidden | FileAttributes.System);

                string volumeId = ComputeDeviceId(driveRoot);
                byte[] combined = Encoding.UTF8.GetBytes($"{volumeId}|{knownDeviceId}");
                byte[] hash = SHA256.HashData(combined);
                return Convert.ToHexString(hash);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[UsbBinding] RotateHiddenDeviceId threw {ExType} on path {Path}", ex.GetType().Name, PhantomDeviceLayout.GetDeviceIdPath(driveRoot));
                return null;
            }
        }

        private sealed class DeviceIdData
        {
            public string DeviceId { get; set; } = string.Empty;
            public long Timestamp { get; set; }
            public string Signature { get; set; } = string.Empty;
        }

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

    public enum DeviceBindingStrength
    {

        None = 0,

        Heuristic = 1,

        VolumeSerial = 2,

        DiskSerial = 3,

        GptGuid = 4
    }
}

