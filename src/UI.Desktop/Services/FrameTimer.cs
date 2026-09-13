using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Drop-in replacement for a 16 ms <see cref="DispatcherTimer"/> used to drive animation,
    /// paced by the display's rendered frames instead of a timer.
    ///
    /// A 16 ms DispatcherTimer on Windows' 15.6 ms timer resolution alternated roughly 15 ms and
    /// 31 ms ticks, out of phase with the screen, which read as micro-stutter on every hover and
    /// spring. This ticks in step with rendering and does no work while stopped.
    ///
    /// Ticks are capped at one per ~16.6 ms of real time: the existing tick handlers step their
    /// physics by a fixed 16 ms, so on a 120/144 Hz display they would otherwise run 2x faster.
    /// </summary>
    public sealed class FrameTimer
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(16.6);

        private readonly Visual _visual;
        private readonly Stopwatch _clock = new();
        private TimeSpan _lastTick;
        private bool _running;
        private bool _frameRequested;
        private DispatcherTimer? _fallback;
        private bool _reportedLongRun;
        private static readonly TimeSpan LongRunThreshold = TimeSpan.FromSeconds(3);

        public FrameTimer(Visual visual)
        {
            _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        }

        public event EventHandler? Tick;

        public bool IsEnabled => _running;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _clock.Restart();
            _lastTick = TimeSpan.Zero;
            RequestFrame();
        }

        public void Stop()
        {
            _running = false;
            _fallback?.Stop();
        }

        private void RequestFrame()
        {
            if (!_running || _frameRequested) return;

            var topLevel = TopLevel.GetTopLevel(_visual);
            if (topLevel == null)
            {
                // Not attached to a window (yet): tick on a plain timer so animations still finish.
                _fallback ??= CreateFallback();
                if (!_fallback.IsEnabled) _fallback.Start();
                return;
            }

            _fallback?.Stop();
            _frameRequested = true;
            topLevel.RequestAnimationFrame(OnFrame);
        }

        private void OnFrame(TimeSpan _)
        {
            _frameRequested = false;
            if (!_running) return;

            var now = _clock.Elapsed;
            if (now - _lastTick >= MinInterval)
            {
                _lastTick = now;
                Tick?.Invoke(this, EventArgs.Empty);
            }

            // Every requested frame is a full window render, so a timer that never stops costs
            // real CPU even when nothing moves. Name the owner once so it can be fixed.
            if (!_reportedLongRun && now > LongRunThreshold)
            {
                _reportedLongRun = true;
                Serilog.Log.Warning("[FrameTimer] Animation timer on {Owner} has run continuously for over {Seconds}s",
                    _visual.GetType().Name, LongRunThreshold.TotalSeconds);
            }

            RequestFrame();
        }

        private DispatcherTimer CreateFallback()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                if (!_running) { timer.Stop(); return; }
                if (TopLevel.GetTopLevel(_visual) != null)
                {
                    // Now on screen: switch to frame pacing.
                    timer.Stop();
                    RequestFrame();
                    return;
                }
                Tick?.Invoke(this, EventArgs.Empty);
            };
            return timer;
        }
    }
}
