using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.TrayBackground
{

    public interface ITrayBackgroundService : IDisposable
    {

        bool IsRunning { get; }

        /// <summary>
        /// The vault's USB was inserted while the vault was locked — the unlock prompt should be
        /// shown for this drive. Carries the drive path.
        /// </summary>
        event Action<string>? VaultUnlockRequested;

        /// <summary>
        /// A removable drive was pulled out. Carries the drive path, so the handler can decide
        /// whether it backed the open vault.
        /// </summary>
        event Action<string>? DriveRemoved;

        Task StartAsync(CancellationToken ct = default);

        Task StopAsync();
    }
}

