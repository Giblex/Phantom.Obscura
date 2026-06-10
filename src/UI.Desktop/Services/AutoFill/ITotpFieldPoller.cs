using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.UI.Services.AutoFill
{

    public interface ITotpFieldPoller
    {

        Task<TotpFieldDescriptor?> WaitForTotpFieldAsync(
            AutoInjectContext context,
            bool isBrowserContext,
            int pollIntervalMs = 500,
            int timeoutMs = 8000,
            CancellationToken ct = default);
    }
}

