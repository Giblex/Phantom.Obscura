using System;

namespace PhantomVault.Core.Services
{

    public readonly struct EncryptionResult
    {
        public EncryptionResult(byte[] ciphertext, byte[] nonce, byte[] tag)
        {
            Ciphertext = ciphertext;
            Nonce = nonce;
            Tag = tag;
        }

        public byte[] Ciphertext { get; }

        public byte[] Nonce { get; }

        public byte[] Tag { get; }
    }
}

