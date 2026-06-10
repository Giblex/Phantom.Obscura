using System;

namespace GiblexVault.Security.ZK.Util.KeyProtector
{

    public interface IKeyProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedBlob);
    }
}

