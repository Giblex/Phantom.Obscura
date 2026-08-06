using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>Hosts the named-pipe server for the lifetime of the Windows service.</summary>
    internal sealed class BrokerWorker : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var server = new BrokerPipeServer(Program.TryLog);
            await server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
