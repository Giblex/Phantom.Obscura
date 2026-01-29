using System;

namespace GiblexVault.Security.ZK.Util
{
    public interface IKeyProtector
    {
        byte[] Protect(byte[] plain);
        byte[] Unprotect(byte[] protectedData);
    }
}
