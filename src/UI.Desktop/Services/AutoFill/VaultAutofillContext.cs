using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Bridges the main application vault state to <see cref="IAutofillVaultContext"/>
    /// so that <see cref="NativeMessagingHostService"/> can enforce the vault-unlock
    /// requirement without a direct dependency on <c>VaultViewModel</c>.
    /// </summary>
    public sealed class VaultAutofillContext : IAutofillVaultContext
    {
        private bool _isUnlocked;
        private VaultManifest? _manifest;

        /// <inheritdoc/>
        public bool IsUnlocked => _isUnlocked;

        /// <inheritdoc/>
        public VaultManifest? CurrentManifest => _manifest;

        /// <summary>
        /// Called by the vault unlock flow once a vault has been successfully opened.
        /// </summary>
        public void SetUnlocked(VaultManifest manifest)
        {
            _manifest = manifest;
            _isUnlocked = true;
        }

        /// <summary>
        /// Called when the vault is locked or dismounted.
        /// </summary>
        public void SetLocked()
        {
            _isUnlocked = false;
            _manifest = null;
        }
    }
}
