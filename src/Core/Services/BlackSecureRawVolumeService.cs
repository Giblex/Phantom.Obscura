using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Windows-only raw-device transport for Black Secure vaults.
    /// This writes the canonical encrypted inner vault layout directly to the
    /// removable device so the host OS cannot browse a normal filesystem.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class BlackSecureRawVolumeService
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("OBRAW001");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public const string RawSelectionPrefix = "RAWUSB:";

        public IReadOnlyList<string> GetSelectableRawDevices()
        {
            if (!OperatingSystem.IsWindows())
                return Array.Empty<string>();

            var results = new List<string>();
            using var diskQuery = new ManagementObjectSearcher(
                "SELECT Index, DeviceID, Model, Size, InterfaceType FROM Win32_DiskDrive WHERE InterfaceType = 'USB'");

            foreach (ManagementObject disk in diskQuery.Get())
            {
                int index = Convert.ToInt32(disk["Index"]);
                string deviceId = disk["DeviceID"]?.ToString() ?? $@"\\.\PHYSICALDRIVE{index}";
                string model = disk["Model"]?.ToString()?.Trim() ?? $"USB Disk {index}";
                long sizeBytes = TryParseLong(disk["Size"]);
                if (HasMountedLogicalDrive(deviceId))
                    continue;

                results.Add(CreateSelectionToken(index, model, sizeBytes));
            }

            return results;
        }

        public bool IsRawSelection(string? selection)
            => !string.IsNullOrWhiteSpace(selection) &&
               selection.StartsWith(RawSelectionPrefix, StringComparison.OrdinalIgnoreCase);

        public string? TryResolvePhysicalDevicePathFromSelection(string? selection)
        {
            if (!TryParseSelectionIndex(selection, out var index))
                return null;

            return $@"\\.\PHYSICALDRIVE{index}";
        }

        public bool TryResolvePhysicalDevicePathFromDriveRoot(string driveRoot, out string? physicalDevicePath)
        {
            physicalDevicePath = null;
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(driveRoot))
                return false;

            string driveLetter = driveRoot.TrimEnd('\\').TrimEnd(':');
            using var logicalDiskQuery = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

            foreach (ManagementObject partition in logicalDiskQuery.Get())
            {
                string? partitionDeviceId = partition["DeviceID"]?.ToString();
                if (string.IsNullOrWhiteSpace(partitionDeviceId))
                    continue;

                using var diskQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject disk in diskQuery.Get())
                {
                    physicalDevicePath = disk["DeviceID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(physicalDevicePath))
                        return true;
                }
            }

            return false;
        }

        public async Task<bool> IsBlackSecureVolumeAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(physicalDevicePath))
                return false;

            await using var stream = OpenRawDevice(physicalDevicePath, FileAccess.Read);
            byte[] magic = new byte[Magic.Length];
            int read = await stream.ReadAsync(magic.AsMemory(0, magic.Length), cancellationToken).ConfigureAwait(false);
            return read == Magic.Length && magic.SequenceEqual(Magic);
        }

        public async Task CreateVolumeFromDirectoryAsync(string physicalDevicePath, string sourceRoot, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Black Secure raw-device transport is currently implemented for Windows only.");
            if (string.IsNullOrWhiteSpace(physicalDevicePath))
                throw new ArgumentException("Physical device path is required.", nameof(physicalDevicePath));
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException($"Source root not found: {sourceRoot}");

            var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var entries = new List<BlackSecureRawEntry>(files.Length);
            long currentOffset = 0;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                var fileInfo = new FileInfo(file);
                byte[] entryHash;
                await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    entryHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                entries.Add(new BlackSecureRawEntry
                {
                    Path = relativePath,
                    Offset = currentOffset,
                    Length = fileInfo.Length,
                    Sha256 = Convert.ToBase64String(entryHash)
                });

                currentOffset += fileInfo.Length;
                payloadHasher.AppendData(entryHash);
            }

            var manifest = new BlackSecureRawManifest
            {
                Version = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                Entries = entries,
                PayloadHash = Convert.ToBase64String(payloadHasher.GetHashAndReset())
            };

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            long requiredBytes = Magic.Length + sizeof(int) + headerBytes.Length + currentOffset;
            long deviceBytes = GetPhysicalDriveSizeBytes(physicalDevicePath);
            if (requiredBytes > deviceBytes)
            {
                throw new InvalidOperationException(
                    $"Black Secure volume requires {requiredBytes} bytes but the selected USB device only exposes {deviceBytes} bytes.");
            }

            await using var output = OpenRawDevice(physicalDevicePath, FileAccess.ReadWrite);
            output.Position = 0;
            await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);

            byte[] headerLengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(headerLengthBytes, headerBytes.Length);
            await output.WriteAsync(headerLengthBytes, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

            foreach (var file in files)
            {
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> ExtractVolumeAsync(string physicalDevicePath, string destinationRoot, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Black Secure raw-device transport is currently implemented for Windows only.");
            if (string.IsNullOrWhiteSpace(physicalDevicePath))
                throw new ArgumentException("Physical device path is required.", nameof(physicalDevicePath));
            if (string.IsNullOrWhiteSpace(destinationRoot))
                throw new ArgumentException("Destination root is required.", nameof(destinationRoot));

            var manifest = await ReadManifestAsync(physicalDevicePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(destinationRoot);

            await using var input = OpenRawDevice(physicalDevicePath, FileAccess.Read);
            int headerLength = await ReadAndValidateHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            long payloadStart = Magic.Length + sizeof(int) + headerLength;
            using var payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string outputPath = Path.Combine(destinationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                input.Position = payloadStart + entry.Offset;
                await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

                long remaining = entry.Length;
                byte[] buffer = new byte[81920];
                using var entryHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (remaining > 0)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, remaining);
                    int bytesRead = await input.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        throw new EndOfStreamException($"Unexpected end of raw device while reading {entry.Path}");

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    entryHasher.AppendData(buffer.AsSpan(0, bytesRead));
                    remaining -= bytesRead;
                }

                byte[] computedEntryHash = entryHasher.GetHashAndReset();
                payloadHasher.AppendData(computedEntryHash);
                if (!string.Equals(Convert.ToBase64String(computedEntryHash), entry.Sha256, StringComparison.Ordinal))
                    throw new CryptographicException($"Black Secure entry integrity check failed for {entry.Path}");
            }

            if (!string.Equals(Convert.ToBase64String(payloadHasher.GetHashAndReset()), manifest.PayloadHash, StringComparison.Ordinal))
                throw new CryptographicException("Black Secure payload integrity check failed.");

            return destinationRoot;
        }

        private static string CreateSelectionToken(int index, string model, long sizeBytes)
        {
            long sizeGb = sizeBytes <= 0 ? 0 : sizeBytes / 1024 / 1024 / 1024;
            return $"{RawSelectionPrefix}{index} - {model} ({sizeGb} GB, Black Secure)";
        }

        private static bool TryParseSelectionIndex(string? selection, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(selection) || !selection.StartsWith(RawSelectionPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var tail = selection.Substring(RawSelectionPrefix.Length);
            var separator = tail.IndexOf(' ');
            string indexText = separator >= 0 ? tail.Substring(0, separator) : tail;
            return int.TryParse(indexText.TrimEnd('-', ':'), out index) || int.TryParse(indexText, out index);
        }

        private static bool HasMountedLogicalDrive(string deviceId)
        {
            try
            {
                using var partitionQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{EscapeWmiString(deviceId)}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject partition in partitionQuery.Get())
                {
                    string? partitionDeviceId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(partitionDeviceId))
                        continue;

                    using var logicalDiskQuery = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{EscapeWmiString(partitionDeviceId)}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                    foreach (ManagementObject logicalDisk in logicalDiskQuery.Get())
                    {
                        if (!string.IsNullOrWhiteSpace(logicalDisk["DeviceID"]?.ToString()))
                            return true;
                    }
                }
            }
            catch
            {
                // Best effort only.
            }

            return false;
        }

        private static long GetPhysicalDriveSizeBytes(string physicalDevicePath)
        {
            int index = ParsePhysicalDriveIndex(physicalDevicePath);
            using var diskQuery = new ManagementObjectSearcher(
                $"SELECT Size FROM Win32_DiskDrive WHERE Index = {index}");

            foreach (ManagementObject disk in diskQuery.Get())
            {
                return TryParseLong(disk["Size"]);
            }

            throw new InvalidOperationException($"Unable to resolve size for {physicalDevicePath}.");
        }

        private static int ParsePhysicalDriveIndex(string physicalDevicePath)
        {
            const string token = "PHYSICALDRIVE";
            int offset = physicalDevicePath.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (offset < 0)
                throw new ArgumentException($"Not a Windows physical drive path: {physicalDevicePath}", nameof(physicalDevicePath));

            string number = physicalDevicePath[(offset + token.Length)..];
            if (!int.TryParse(number, out int index))
                throw new ArgumentException($"Unable to parse physical drive index from {physicalDevicePath}", nameof(physicalDevicePath));

            return index;
        }

        private static async Task<int> ReadAndValidateHeaderAsync(Stream input, CancellationToken cancellationToken)
        {
            byte[] magicBuffer = new byte[Magic.Length];
            int magicRead = await input.ReadAsync(magicBuffer.AsMemory(0, magicBuffer.Length), cancellationToken).ConfigureAwait(false);
            if (magicRead != Magic.Length || !magicBuffer.SequenceEqual(Magic))
                throw new InvalidOperationException("Invalid Black Secure raw-device format.");

            byte[] headerLengthBytes = new byte[4];
            int headerLengthRead = await input.ReadAsync(headerLengthBytes.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (headerLengthRead != 4)
                throw new EndOfStreamException("Failed to read Black Secure header length.");

            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(headerLengthBytes);
            if (headerLength <= 0 || headerLength > 1024 * 1024)
                throw new InvalidOperationException("Invalid Black Secure header length.");

            return headerLength;
        }

        private static FileStream OpenRawDevice(string physicalDevicePath, FileAccess fileAccess)
            => new(
                physicalDevicePath,
                FileMode.Open,
                fileAccess,
                FileShare.ReadWrite,
                4096,
                fileAccess == FileAccess.Read ? FileOptions.None : FileOptions.WriteThrough);

        private static string EscapeWmiString(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

        private static long TryParseLong(object? value)
            => value == null ? 0 : Convert.ToInt64(value);

        public async Task<BlackSecureRawManifest> ReadManifestAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
        {
            await using var input = OpenRawDevice(physicalDevicePath, FileAccess.Read);
            int headerLength = await ReadAndValidateHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            byte[] headerBytes = new byte[headerLength];
            int read = await input.ReadAsync(headerBytes.AsMemory(0, headerLength), cancellationToken).ConfigureAwait(false);
            if (read != headerLength)
                throw new EndOfStreamException("Failed to read Black Secure header.");

            return JsonSerializer.Deserialize<BlackSecureRawManifest>(headerBytes, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse Black Secure header manifest.");
        }
    }

    public sealed class BlackSecureRawManifest
    {
        public int Version { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string PayloadHash { get; set; } = string.Empty;
        public List<BlackSecureRawEntry> Entries { get; set; } = new();
    }

    public sealed class BlackSecureRawEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
