using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Paces hand-written animation loops by the display's real refresh rather than a timer.
    ///
    /// Loops paced with <c>await Task.Delay(16)</c> actually waited about 31 ms on Windows'
    /// default 15.6 ms timer resolution, so "60 fps" animations ran at a jittery ~30 fps, out
    /// of step with the screen. Awaiting the next rendered frame gives one update per displayed
    /// frame (60/120/144 Hz), and none when nothing is being drawn.
    /// </summary>
    public static class FrameClock
    {
        /// <summary>Completes when the next frame is rendered for <paramref name="visual"/>'s window.</summary>
        public static Task NextFrameAsync(Visual? visual)
        {
            var topLevel = visual != null ? TopLevel.GetTopLevel(visual) : null;
            if (topLevel == null)
            {
                // Not on screen (yet): fall back to a short wait so callers still make progress.
                return Task.Delay(16);
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            topLevel.RequestAnimationFrame(_ => tcs.TrySetResult());
            return tcs.Task;
        }
    }
}
