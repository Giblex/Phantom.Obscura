using System;
using System.Timers;

namespace PhantomVault.Core.Services
{

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

        public event Action? IdleElapsed;

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

