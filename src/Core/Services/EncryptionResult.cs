using System;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Represents the output of an encryption operation. The caller is
    /// responsible for persisting the values in a secure format. None of
    /// the properties should be altered before decryption.
    /// </summary>
    public readonly struct EncryptionResult
    {
        public EncryptionResult(byte[] ciphertext, byte[] nonce, byte[] tag)
        {
            Ciphertext = ciphertext;
            Nonce = nonce;
            Tag = tag;
        }

        /// <summary>
        /// The encrypted data. This includes any padding added by the
        /// encryption algorithm. Do not truncate or otherwise modify.
        /// </summary>
        public byte[] Ciphertext { get; }

        /// <summary>
        /// The unique nonce used for the encryption. This must be passed
        /// unchanged into the decryption routine. A fresh nonce must be
        /// generated for every encryption operation.
        /// </summary>
        public byte[] Nonce { get; }

        /// <summary>
        /// The authentication tag produced by authenticated encryption
        /// algorithms such as AES‑GCM. The tag ensures integrity and
        /// authenticity of the ciphertext and associated data.
        /// </summary>
        public byte[] Tag { get; }
    }
}