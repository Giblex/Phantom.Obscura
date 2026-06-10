using System;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Util;

namespace PhantomVault.Core.Services.Platform.Android
{

    public sealed class AndroidKeystoreService : IKeyProtector
    {
        private Func<byte[], byte[]>? _encryptHandler;
        private Func<byte[], byte[]>? _decryptHandler;

        public void RegisterKeystoreHandlers(
            Func<byte[], byte[]> encryptHandler,
            Func<byte[], byte[]> decryptHandler)
        {
            _encryptHandler = encryptHandler;
            _decryptHandler = decryptHandler;
        }

        public byte[] Protect(byte[] plain)
        {
            if (_encryptHandler is null)
                throw new InvalidOperationException(
                    "Android Keystore handlers have not been registered. " +
                    "Call RegisterKeystoreHandlers() from the MAUI Android platform entry point.");
            return _encryptHandler(plain);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (_decryptHandler is null)
                throw new InvalidOperationException(
                    "Android Keystore handlers have not been registered. " +
                    "Call RegisterKeystoreHandlers() from the MAUI Android platform entry point.");
            return _decryptHandler(protectedData);
        }
    }
}

