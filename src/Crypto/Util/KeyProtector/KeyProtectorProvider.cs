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

            var keyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "PhantomVault");
            Directory.CreateDirectory(keyDir);
            return new UnixFileKeyProtector(Path.Combine(keyDir, ".envelope.key"));
        }
    }
}

