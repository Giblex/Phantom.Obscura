using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.TrayBackground
{
    /// <summary>
    /// Manages the system-tray lifecycle for AutoFill Mode.
    /// When running, the app stays resident in the notification area and
    /// listens for USB key insertion to trigger credential auto-fill.
    /// </summary>
    public interface ITrayBackgroundService : IDisposable
    {
        /// <summary>True while the tray icon is active and USB events are wired.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Starts the tray icon and begins listening for USB insertion events.
        /// Safe to call multiple times — subsequent calls are no-ops.
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Hides the tray icon and stops listening for USB events.
        /// </summary>
        Task StopAsync();
    }
}
