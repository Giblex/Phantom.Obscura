using System;
using System.IO;
using System.Security.Principal;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Persistent broker configuration stored under ProgramData (writable only by
    /// administrators/SYSTEM). Holds the allow-listed UI executable path that the
    /// pipe server checks each incoming connection against.
    /// </summary>
    internal static class BrokerConfig
    {
        private static string ConfigDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PhantomObscura", "Broker");

        private static string AllowedClientFile => Path.Combine(ConfigDirectory, "allowed-client.txt");
        private static string AllowedClientUserSidFile => Path.Combine(ConfigDirectory, "allowed-client-user-sid.txt");

        public static void SaveAllowedClientPath(string clientExePath)
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(AllowedClientFile, clientExePath.Trim());

            try
            {
                var sid = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrWhiteSpace(sid))
                    File.WriteAllText(AllowedClientUserSidFile, sid.Trim());
            }
            catch
            {
                // Non-Windows or identity unavailable — pipe ACL falls back to path check only.
            }
        }

        public static string? LoadAllowedClientPath()
        {
            try
            {
                if (!File.Exists(AllowedClientFile))
                    return null;
                var value = File.ReadAllText(AllowedClientFile).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        public static string? LoadAllowedClientUserSid()
        {
            try
            {
                if (!File.Exists(AllowedClientUserSidFile))
                    return null;
                var value = File.ReadAllText(AllowedClientUserSidFile).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        public static string LogDirectory
        {
            get
            {
                var dir = Path.Combine(ConfigDirectory, "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
