using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Coordinates the full USB-triggered auto-fill flow:
    /// detect portal → match credential → fill → wait for TOTP → fill TOTP.
    /// When no credential matches, surfaces the no-match dialog so the user can
    /// save a new entry and, when linked, hand off passkey creation to Attestor.
    /// </summary>
    public interface IAutoFillOrchestrator
    {
        /// <summary>
        /// Provides the vault credential provider and manifest after the vault is unlocked.
        /// Must be called before <see cref="RunAutoFillFlowAsync"/> will succeed.
        /// </summary>
        void SetVaultContext(ICredentialProvider provider, VaultManifest manifest);

        /// <summary>
        /// Runs the full auto-fill pipeline for a USB insertion event.
        /// </summary>
        /// <param name="usbDrivePath">Root path of the inserted USB drive.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RunAutoFillFlowAsync(string usbDrivePath, CancellationToken ct = default);
    }
}
