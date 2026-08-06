using PhantomVault.Core.Services.Sync;

namespace PhantomVault.UI.Services.Sync
{
    /// <summary>
    /// Vault-state gate for <see cref="TotpSyncPipeServer"/>, mirroring
    /// <c>IAutofillVaultContext</c>/<c>VaultAutofillContext</c> for the autofill pipe.
    /// </summary>
    public interface ITotpSyncVaultContext
    {
        bool IsUnlocked { get; }

        bool SyncTotpEnabled { get; }

        ITotpSyncBridge? Bridge { get; }
    }
}
