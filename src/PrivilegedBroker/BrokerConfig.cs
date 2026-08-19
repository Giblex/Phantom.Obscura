using System;
using System.IO;
using System.Security.Principal;
using System.Security.AccessControl;

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
        private static string AllowedClientSignerFile => Path.Combine(ConfigDirectory, "allowed-client-signer-sha256.txt");
        private static string ManifestKeyPinFile => Path.Combine(ConfigDirectory, "manifest-key-sha256.txt");

        public static void SaveAllowedClient(string clientExePath, string signerSha256)
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(AllowedClientFile, clientExePath.Trim());
            File.WriteAllText(AllowedClientSignerFile, signerSha256.Trim());

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

        public static string? LoadAllowedClientSignerSha256()
        {
            try
            {
                if (!File.Exists(AllowedClientSignerFile))
                    return null;
                var value = File.ReadAllText(AllowedClientSignerFile).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveManifestKeyPin(string keyId)
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ManifestKeyPinFile, keyId.Trim().ToLowerInvariant());
        }

        public static string? LoadManifestKeyPin()
        {
            try
            {
                string value = File.ReadAllText(ManifestKeyPinFile).Trim();
                return value.Length == 64 ? value : null;
            }
            catch { return null; }
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

        public static string IntegrityStateDirectory
        {
            get
            {
                var dir = Path.Combine(ConfigDirectory, "integrity");
                Directory.CreateDirectory(dir);
                HardenIntegrityDirectory(dir);
                return dir;
            }
        }

        private static void HardenIntegrityDirectory(string directory)
        {
            if (!OperatingSystem.IsWindows()) return;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            string? userSid = LoadAllowedClientUserSid();
            if (!string.IsNullOrWhiteSpace(userSid))
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(userSid),
                    FileSystemRights.ReadAndExecute | FileSystemRights.Read, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
    }
}
