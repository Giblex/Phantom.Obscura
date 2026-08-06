using System;
using System.IO;
using PhantomVault.Core.Utils;

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

            // keyfilePath may be a composite of several component paths (primary keyfile +
            // host companion keyfile) joined by CompositeKeyfilePath.Delimiter. Validate each
            // component on disk individually — never File.Exists the joined string, which is
            // not itself a real path.
            var parts = CompositeKeyfilePath.Split(keyfilePath);
            if (parts.Count == 0)
            {
                throw new ArgumentException(
                    "Vault unlock requires a USB keyfile. Password alone is insufficient.",
                    paramName ?? nameof(keyfilePath));
            }

            foreach (var part in parts)
            {
                if (!File.Exists(part))
                {
                    throw new FileNotFoundException(
                        "Vault keyfile is required but the supplied path does not resolve. Re-attach the USB device and try again.",
                        part);
                }
            }
        }
    }
}
