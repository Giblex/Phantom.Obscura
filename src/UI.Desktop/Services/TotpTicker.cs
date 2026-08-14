using System;
using System.Threading;

namespace PhantomVault.UI.Services
{

    /// <summary>
    /// A single process-wide one-second tick that every live TOTP display subscribes to.
    ///
    /// Each section previously owned a <see cref="Timer"/>; an entry with several linked
    /// authenticator sections therefore spun up several timers that all did the same thing
    /// one second apart. One shared tick keeps the codes in step with each other and stops
    /// the timer count growing with the vault.
    /// </summary>
    public static class TotpTicker
    {
        private static readonly object Gate = new();
        private static Timer? _timer;
        private static int _subscribers;

        private static event Action? Tick;

        public static IDisposable Subscribe(Action onTick)
        {
            ArgumentNullException.ThrowIfNull(onTick);

            lock (Gate)
            {
                Tick += onTick;
                _subscribers++;

                _timer ??= new Timer(_ => Raise(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            onTick();

            return new Subscription(onTick);
        }

        private static void Raise()
        {
            Action? handlers;
            lock (Gate)
            {
                handlers = Tick;
            }

            handlers?.Invoke();
        }

        private static void Unsubscribe(Action onTick)
        {
            lock (Gate)
            {
                Tick -= onTick;
                _subscribers--;

                if (_subscribers > 0)
                    return;

                _subscribers = 0;
                _timer?.Dispose();
                _timer = null;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _onTick;

            public Subscription(Action onTick) => _onTick = onTick;

            public void Dispose()
            {
                var handler = Interlocked.Exchange(ref _onTick, null);
                if (handler != null)
                    Unsubscribe(handler);
            }
        }
    }
}
