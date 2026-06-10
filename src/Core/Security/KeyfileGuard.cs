using System;
using System.IO;

namespace PhantomVault.Core.Security
{
    /// <summary>
    /// Single source of truth for the keyfile-mandatory contract.
    /// Hard rule (user memory): "USB keyfile is MANDATORY — required to unlock the vault on every platform."
    /// Every public API that touches the master key MUST call <see cref="Require"/> at entry.
    /// </summary>
    public static class KeyfileGuard
    {
        public static void Require(string? keyfilePath, string? paramName = null)
        {
            if (string.IsNullOrWhiteSpace(keyfilePath))
            {
                throw new ArgumentException(
                    "Vault unlock requires a USB keyfile. Password alone is insufficient.",
                    paramName ?? nameof(keyfilePath));
            }

            if (!File.Exists(keyfilePath))
            {
                throw new FileNotFoundException(
                    "Vault keyfile is required but the supplied path does not resolve. Re-attach the USB device and try again.",
                    keyfilePath);
            }
        }
    }
}
