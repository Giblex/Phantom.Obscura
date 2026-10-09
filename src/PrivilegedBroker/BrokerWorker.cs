using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>Hosts the named-pipe server for the lifetime of the Windows service.</summary>
    internal sealed class BrokerWorker : BackgroundService
    {
        private readonly IntegrityWatchdogWorker _watchdog;
        private readonly IHostApplicationLifetime _lifetime;

        public BrokerWorker(IntegrityWatchdogWorker watchdog, IHostApplicationLifetime lifetime)
        {
            _watchdog = watchdog;
            _lifetime = lifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The lifetime is handed to the pipe server so the allow-listed client can ask the
            // service to stop itself. Stopping from outside would need elevation, which would mean
            // a UAC prompt every time the app closed.
            var server = new BrokerPipeServer(Program.TryLog, _watchdog, () => _lifetime.StopApplication());
            await server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
