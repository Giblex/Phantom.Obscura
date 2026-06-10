using System;
using System.Security.Cryptography;

namespace GiblexVault.Security.ZK.Primitives;

public static class Hkdf
{
    public static byte[] Sha256(byte[] ikm, byte[] salt, byte[] info, int len = 32)
    {
        ArgumentNullException.ThrowIfNull(ikm);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(info);
        if (len <= 0) throw new ArgumentOutOfRangeException(nameof(len));

        var output = new byte[len];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, output, salt, info);
        return output;
    }
}
