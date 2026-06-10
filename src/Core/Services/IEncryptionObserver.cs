using System;

namespace PhantomVault.Core.Services
{

    public interface IEncryptionObserver
    {

        void OnPasswordBufferZeroized(byte[] buffer);

        void OnTransientBufferZeroized(byte[] buffer);
    }
}

