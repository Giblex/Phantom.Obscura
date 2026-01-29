using System;
using System.Timers;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Tracks user inactivity and invokes a callback when the configured
    /// timeout elapses. This implementation does not rely on OS‑specific
    /// APIs; instead it requires the host UI to call <see cref="Reset"/>
    /// whenever user interaction occurs (e.g. mouse/keyboard events).
    /// </summary>
    public sealed class IdleLockService : IDisposable
    {
        private readonly Timer _timer;
        private readonly TimeSpan _timeout;

        public IdleLockService(TimeSpan timeout)
        {
            _timeout = timeout;
            _timer = new Timer(_timeout.TotalMilliseconds)
            {
                AutoReset = false
            };
            _timer.Elapsed += (_, __) => IdleElapsed?.Invoke();
        }

        /// <summary>
        /// Invoked when the user has been idle for the configured timeout.
        /// Consumers typically handle this event by locking or dismounting the
        /// vault.
        /// </summary>
        public event Action? IdleElapsed;

        /// <summary>
        /// Resets the idle timer. Call this whenever the user performs an
        /// action in the application. If the user stays inactive, the
        /// <see cref="IdleElapsed"/> event will fire after the timeout.
        /// </summary>
        public void Reset()
        {
            _timer.Stop();
            _timer.Interval = _timeout.TotalMilliseconds;
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}