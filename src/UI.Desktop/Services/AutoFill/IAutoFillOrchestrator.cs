using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;

namespace PhantomVault.UI.Services.AutoFill
{

    public interface IAutoFillOrchestrator
    {

        void SetVaultContext(ICredentialProvider provider, VaultManifest manifest);

        Task RunAutoFillFlowAsync(string usbDrivePath, CancellationToken ct = default);
    }
}

