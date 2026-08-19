using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>Hosts the named-pipe server for the lifetime of the Windows service.</summary>
    internal sealed class BrokerWorker : BackgroundService
    {
        private readonly IntegrityWatchdogWorker _watchdog;

        public BrokerWorker(IntegrityWatchdogWorker watchdog) => _watchdog = watchdog;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var server = new BrokerPipeServer(Program.TryLog, _watchdog);
            await server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
