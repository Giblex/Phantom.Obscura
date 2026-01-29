using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Provides the current vault state for autofill operations so the native
    /// messaging host can fail closed when the vault is locked or autofill is
    /// disabled in the manifest.
    /// </summary>
    public interface IAutofillVaultContext
    {
        /// <summary>
        /// True when the vault master key is loaded and the vault is unlocked.
        /// </summary>
        bool IsUnlocked { get; }

        /// <summary>
        /// The active vault manifest, or null if no vault is loaded.
        /// </summary>
        VaultManifest? CurrentManifest { get; }
    }
}
