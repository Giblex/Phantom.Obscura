using System;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Util;

namespace PhantomVault.Core.Services.Platform.Android
{
    /// <summary>
    /// Android hardware-backed IKeyProtector using the Android Keystore system.
    /// On Android 6.0+ (API 23+), AES keys created with KeyGenParameterSpec.Builder are
    /// stored in hardware-backed secure enclave and never leave the device's secure element.
    ///
    /// This wraps sync Protect/Unprotect around async delegate handlers registered
    /// from the MAUI Android platform layer where Android APIs are accessible.
    /// </summary>
    public sealed class AndroidKeystoreService : IKeyProtector
    {
        private Func<byte[], byte[]>? _encryptHandler;
        private Func<byte[], byte[]>? _decryptHandler;

        /// <summary>
        /// Registers hardware-backed encrypt/decrypt delegates backed by Android Keystore AES/GCM.
        /// Must be called from the MAUI Android platform entry point before first use.
        /// </summary>
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
