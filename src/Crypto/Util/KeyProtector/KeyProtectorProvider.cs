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
            {
                return new DpapiKeyProtector();
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData ?? ".", "PhantomVault", "KeyProtector");
            Directory.CreateDirectory(dir);
            var keyPath = Path.Combine(dir, "envelope.key");
            return new FileKeyProtector(keyPath);
        }
    }
}
