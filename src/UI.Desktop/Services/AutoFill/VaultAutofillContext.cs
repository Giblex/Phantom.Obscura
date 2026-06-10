using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{

    public sealed class VaultAutofillContext : IAutofillVaultContext
    {
        private bool _isUnlocked;
        private VaultManifest? _manifest;

        public bool IsUnlocked => _isUnlocked;

        public VaultManifest? CurrentManifest => _manifest;

        public void SetUnlocked(VaultManifest manifest)
        {
            _manifest = manifest;
            _isUnlocked = true;
        }

        public void SetLocked()
        {
            _isUnlocked = false;
            _manifest = null;
        }
    }
}

