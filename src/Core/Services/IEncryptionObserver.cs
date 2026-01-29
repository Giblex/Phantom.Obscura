using System;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Observer interface used in tests to observe sensitive buffers after
    /// they have been zeroized by the <see cref="EncryptionService"/>.
    /// Production code should not implement or set this; it is intended
    /// for tests that need to assert zeroization semantics without using
    /// static state.
    /// </summary>
    public interface IEncryptionObserver
    {
        /// <summary>Called after a password buffer has been zeroized.</summary>
        void OnPasswordBufferZeroized(byte[] buffer);

        /// <summary>Called after a transient buffer (plaintext/ciphertext/tag) has been zeroized.</summary>
        void OnTransientBufferZeroized(byte[] buffer);
    }
}
