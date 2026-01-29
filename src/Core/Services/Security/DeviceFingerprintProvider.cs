using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Generates stable device fingerprints by combining hardware, OS, and user context.
    /// Uses a persistent application-specific ID to ensure consistency across sessions.
    /// All operations remain offline (no network calls or telemetry).
    /// </summary>
    public sealed class DeviceFingerprintProvider : IDeviceFingerprintProvider
    {
        private const string AppIdFileName = ".phantom_device_id";
        
        /// <summary>
        /// Generates a fingerprint for the current device and user context.
        /// Creates or loads a persistent device ID from user's AppData for consistency.
        /// </summary>
        public DeviceFingerprint GetCurrentFingerprint()
        {
            string machineId = GetOrCreateMachineId();
            string osFamily = GetOsFamily();
            string osVersion = Environment.OSVersion.VersionString;
            string hostname = Environment.MachineName;
            string username = Environment.UserName;

            return new DeviceFingerprint
            {
                MachineId = machineId,
                OsFamily = osFamily,
                OsVersion = osVersion,
                Hostname = hostname,
                UserName = username,
                TrustedAt = DateTimeOffset.UtcNow,
                LastAccessAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Gets or creates a stable machine identifier stored in user's AppData.
        /// This ensures the same device always produces the same MachineId even
        /// if hardware changes slightly (e.g., RAM upgrade). For maximum security,
        /// consider hashing additional hardware identifiers (CPU serial, disk ID).
        /// </summary>
        private static string GetOrCreateMachineId()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "PhantomVault");
            string idFilePath = Path.Combine(appFolder, AppIdFileName);

            try
            {
                // Try to load existing ID
                if (File.Exists(idFilePath))
                {
                    string existingId = File.ReadAllText(idFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(existingId))
                    {
                        return existingId;
                    }
                }

                // Generate new ID if file doesn't exist or is invalid
                Directory.CreateDirectory(appFolder);
                string newId = GenerateStableMachineId();
                File.WriteAllText(idFilePath, newId);
                return newId;
            }
            catch
            {
                // Fallback: generate transient ID (not persisted, will change on restart)
                return GenerateStableMachineId();
            }
        }

        /// <summary>
        /// Generates a stable machine identifier by hashing environment properties.
        /// Combines machine name, OS version, user name, and processor count.
        /// For production, consider adding hardware serial numbers if accessible.
        /// </summary>
        private static string GenerateStableMachineId()
        {
            var components = new StringBuilder();
            components.Append(Environment.MachineName);
            components.Append(Environment.OSVersion.Platform);
            components.Append(Environment.OSVersion.Version);
            components.Append(Environment.UserName);
            components.Append(Environment.ProcessorCount);
            components.Append(Environment.Is64BitOperatingSystem);
            
            byte[] inputBytes = Encoding.UTF8.GetBytes(components.ToString());
            byte[] hashBytes = SHA256.HashData(inputBytes);
            
            // Return first 16 bytes as hex for compact representation
            return Convert.ToHexString(hashBytes)[..32];
        }

        /// <summary>
        /// Determines OS family from platform ID for cross-platform identification.
        /// </summary>
        private static string GetOsFamily()
        {
            return Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => "Windows",
                PlatformID.Unix => "Linux",
                PlatformID.MacOSX => "macOS",
                _ => "Unknown"
            };
        }
    }
}
