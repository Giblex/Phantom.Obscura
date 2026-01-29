using System;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{
    /// <summary>
    /// Abstraction for sealing/unsealing small secrets. Implementations
    /// may use platform-provided secure storage (DPAPI) or an application
    /// managed envelope. This is a prototype abstraction to enable
    /// cross-platform sealing strategies.
    /// </summary>
    public interface IKeyProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedBlob);
    }
}
