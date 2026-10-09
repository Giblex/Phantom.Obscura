using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;

namespace PhantomVault.UI.Services.AutoFill
{

    public interface IAutoFillOrchestrator
    {

        void SetVaultContext(ICredentialProvider provider, VaultManifest manifest);

        /// <summary>
        /// True when an unlocked vault is wired up and autofill can actually do something. The USB
        /// handler checks this so a locked vault triggers an unlock prompt instead of a flow that
        /// would silently abort at its first guard.
        /// </summary>
        bool IsVaultReady { get; }

        Task RunAutoFillFlowAsync(string usbDrivePath, CancellationToken ct = default);
    }
}

