using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Autofill
{

    public interface INativeMessagingHost
    {

        Task StartAsync(CancellationToken cancellationToken = default);

        bool IsRunning { get; }

        bool ValidateOrigin(string origin);
    }
}

