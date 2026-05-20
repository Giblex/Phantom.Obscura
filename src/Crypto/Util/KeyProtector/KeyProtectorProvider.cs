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

            throw new PlatformNotSupportedException(
                "No production-safe key protector is available on this platform. " +
                "Plain file-backed envelope keys are disabled.");
        }
    }
}
