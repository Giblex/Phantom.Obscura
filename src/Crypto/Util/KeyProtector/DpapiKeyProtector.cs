using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{
    [SupportedOSPlatform("windows")]
    internal class DpapiKeyProtector : IKeyProtector
    {
        private readonly DataProtectionScope _scope;

        public DpapiKeyProtector(DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            _scope = scope;
        }

        public byte[] Protect(byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            return ProtectedData.Protect(plaintext, null, _scope);
        }

        public byte[] Unprotect(byte[] protectedBlob)
        {
            if (protectedBlob == null) throw new ArgumentNullException(nameof(protectedBlob));
            return ProtectedData.Unprotect(protectedBlob, null, _scope);
        }
    }
}
