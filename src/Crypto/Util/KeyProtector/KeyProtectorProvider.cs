using System;
using System.IO;
using System.Runtime.Versioning;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{
    internal static class KeyProtectorProvider
    {
        public static IKeyProtector CreateDefault()
        {
            if (OperatingSystem.IsWindows())
                return new DpapiKeyProtector();

            // macOS / Linux: AES-256-GCM envelope key stored in ~/.config/PhantomVault/
            // with Unix 0600 permissions (owner read/write only).
            //
            // Upgrade path:
            //   macOS  → Keychain via Security.framework (SecItemAdd / SecItemCopyMatching)
            //   Linux  → SecretService / libsecret D-Bus API
            var keyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "PhantomVault");
            Directory.CreateDirectory(keyDir);
            return new UnixFileKeyProtector(Path.Combine(keyDir, ".envelope.key"));
        }
    }
}
