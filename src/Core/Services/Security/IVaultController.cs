using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Controls vault access modes and security state.
    /// </summary>
    public interface IVaultController
    {
        /// <summary>
        /// Switches to a decoy vault containing fake credentials.
        /// Used to mislead attackers during suspected compromise.
        /// </summary>
        /// <returns>Task that completes when decoy vault is activated</returns>
        Task SwitchToDecoyVaultAsync();

        /// <summary>
        /// Enters read-only mode, preventing any modifications to vault data.
        /// Credentials can still be viewed but not edited, added, or deleted.
        /// </summary>
        void EnterReadOnlyMode();

        /// <summary>
        /// Exits read-only mode, restoring full vault functionality.
        /// Should only be called after verifying legitimate user.
        /// </summary>
        void ExitReadOnlyMode();

        /// <summary>
        /// Gets whether the vault is currently in read-only mode.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Gets whether the vault is currently showing decoy data.
        /// When true, all data shown to the user is fake.
        /// </summary>
        bool IsDecoyActive { get; }

        /// <summary>
        /// Gets the active decoy database (null if no decoy is active).
        /// </summary>
        VaultDatabase? ActiveDecoyDatabase { get; }
    }
}
