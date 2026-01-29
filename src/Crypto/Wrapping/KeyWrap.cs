using System;
using System.Security.Cryptography;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;

namespace GiblexVault.Security.ZK.Wrapping
{
    public static class KeyWrap
    {
        public static byte[] WrapAead(CipherSuite s, byte[] kek, byte[] keyToWrap, byte[] aad)
        {
            var nonce = RandomNumberGenerator.GetBytes(Aead.GetSuite(s).NonceSize);
            var ct = Aead.Encrypt(s, kek, nonce, aad, keyToWrap);
            return System.Linq.Enumerable.Concat(nonce, ct).ToArray();
        }

        public static byte[] UnwrapAead(CipherSuite s, byte[] kek, byte[] wrapped, byte[] aad)
        {
            var ns = Aead.GetSuite(s).NonceSize;
            var nonce = new byte[ns];
            Array.Copy(wrapped, 0, nonce, 0, ns);
            var ct = new byte[wrapped.Length - ns];
            Array.Copy(wrapped, ns, ct, 0, ct.Length);
            return Aead.Decrypt(s, kek, nonce, aad, ct);
        }
    }
}