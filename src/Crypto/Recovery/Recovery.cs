using System;
using System.Security.Cryptography;
using System.Text;
using GiblexVault.Security.ZK.Primitives;

namespace GiblexVault.Security.ZK.Recovery
{
    public static class Recovery
    {
        public static byte[] GenerateRecoveryKey() => RandomNumberGenerator.GetBytes(32);

        public static byte[] WrapMasterKey(byte[] masterKey, byte[] recoveryKey)
        {
            var salt = new byte[32];
            var kek = Hkdf.Sha256(recoveryKey, salt, Encoding.UTF8.GetBytes("rk->kek"));

            var nonce = RandomNumberGenerator.GetBytes(12);
            // Use the ReadOnlySpan<byte> constructor with explicit tag size.
            using var aes = new AesGcm(kek.AsSpan(), 16);
            var tag = new byte[16];
            var ct = new byte[masterKey.Length];
            aes.Encrypt(nonce, masterKey, ct, tag);
            return System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(nonce, ct), tag).ToArray();
        }

        public static byte[] UnwrapMasterKey(byte[] wrapped, byte[] recoveryKey)
        {
            var salt = new byte[32];
            var kek = Hkdf.Sha256(recoveryKey, salt, Encoding.UTF8.GetBytes("rk->kek"));

            var nonce = new byte[12];
            Array.Copy(wrapped, 0, nonce, 0, 12);

            var ctLen = wrapped.Length - 12 - 16;
            var ct = new byte[ctLen];
            Array.Copy(wrapped, 12, ct, 0, ctLen);

            var tag = new byte[16];
            Array.Copy(wrapped, wrapped.Length - 16, tag, 0, 16);

            var plain = new byte[ct.Length];
            // Use the ReadOnlySpan<byte> constructor with explicit tag size.
            using var aes = new AesGcm(kek.AsSpan(), 16);
            aes.Decrypt(nonce, ct, tag, plain);
            return plain;
        }
    }
}