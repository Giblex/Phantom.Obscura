using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{

    public interface IAutofillVaultContext
    {

        bool IsUnlocked { get; }

        VaultManifest? CurrentManifest { get; }
    }
}

