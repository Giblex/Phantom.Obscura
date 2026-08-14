using System;
using System.Timers;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// App-scope idle watchdog. Fires <see cref="IdleElapsed"/> once the configured idle
    /// window passes without a <see cref="Reset"/>.
    ///
    /// The timeout is resolved through a provider delegate on every <see cref="Reset"/>
    /// rather than captured at construction, so the user's auto-lock setting takes effect
    /// immediately instead of being frozen at whatever the value was when the container
    /// was built. A non-positive timeout means "auto-lock disabled" and stops the timer.
    /// </summary>
    public sealed class IdleLockService : IDisposable
    {
        private readonly Timer _timer;
        private readonly Func<TimeSpan> _timeoutProvider;
        private readonly object _gate = new();
        private bool _disposed;

        /// <summary>Fixed-timeout constructor, retained for tests and callers with no settings access.</summary>
        public IdleLockService(TimeSpan timeout) : this(() => timeout)
        {
        }

        /// <summary>
        /// Preferred constructor. <paramref name="timeoutProvider"/> is invoked on each
        /// <see cref="Reset"/> so configuration changes apply without a restart.
        /// </summary>
        public IdleLockService(Func<TimeSpan> timeoutProvider)
        {
            _timeoutProvider = timeoutProvider ?? throw new ArgumentNullException(nameof(timeoutProvider));
            _timer = new Timer { AutoReset = false };
            _timer.Elapsed += (_, __) => IdleElapsed?.Invoke();
        }

        public event Action? IdleElapsed;

        /// <summary>The idle window currently in force, or <see cref="TimeSpan.Zero"/> when disabled.</summary>
        public TimeSpan CurrentTimeout => ResolveTimeout();

        /// <summary>
        /// Records user activity and restarts the countdown. Safe to call at high frequency —
        /// it only touches the timer interval.
        /// </summary>
        public void Reset()
        {
            lock (_gate)
            {
                if (_disposed) return;

                var timeout = ResolveTimeout();

                _timer.Stop();

                // Non-positive timeout means the user disabled auto-lock. Leave the timer
                // stopped rather than falling back to a default, which would silently lock
                // a vault the user asked to stay open.
                if (timeout <= TimeSpan.Zero) return;

                _timer.Interval = timeout.TotalMilliseconds;
                _timer.Start();
            }
        }

        /// <summary>Stops the countdown without firing. Used when the vault locks by another route.</summary>
        public void Suspend()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _timer.Stop();
            }
        }

        private TimeSpan ResolveTimeout()
        {
            try
            {
                var t = _timeoutProvider();

                // Timer.Interval throws on values outside (0, int.MaxValue] ms; clamp rather
                // than let a bad setting take down the watchdog entirely.
                if (t <= TimeSpan.Zero) return TimeSpan.Zero;
                return t > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : t;
            }
            catch
            {
                // A failed settings read must not disable the watchdog. Fall back to a
                // conservative window rather than leaving the vault unlocked indefinitely.
                return TimeSpan.FromMinutes(15);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Dispose();
            }
        }
    }
}
