using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{

    public sealed class DeviceFingerprintProvider : IDeviceFingerprintProvider
    {
        private const string AppIdFileName = ".phantom_device_id";

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

        private static string GetOrCreateMachineId()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "PhantomVault");
            string idFilePath = Path.Combine(appFolder, AppIdFileName);

            try
            {

                if (File.Exists(idFilePath))
                {
                    string existingId = File.ReadAllText(idFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(existingId))
                    {
                        return existingId;
                    }
                }

                Directory.CreateDirectory(appFolder);
                string newId = GenerateStableMachineId();
                File.WriteAllText(idFilePath, newId);
                return newId;
            }
            catch
            {

                return GenerateStableMachineId();
            }
        }

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

            return Convert.ToHexString(hashBytes)[..32];
        }

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

