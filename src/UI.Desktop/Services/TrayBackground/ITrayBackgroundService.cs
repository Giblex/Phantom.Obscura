using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.TrayBackground
{

    public interface ITrayBackgroundService : IDisposable
    {

        bool IsRunning { get; }

        Task StartAsync(CancellationToken ct = default);

        Task StopAsync();
    }
}

