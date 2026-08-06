using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomVault.UI.Services
{

    // Attestor runs as its own OS process (deliberate — the Suite session design carries no
    // key material across the process boundary). This gives Obscura an app-switcher feel
    // without true UI embedding: track the launched process, and on a repeat "open" just
    // bring its existing window to the front (optionally aligned to Obscura's own window
    // bounds) instead of spawning a second instance.
    public sealed class IntegratedAttestorService
    {
        private readonly SuiteWorkspaceService _workspaceService;
        private Process? _trackedProcess;

        public event EventHandler? RunningStateChanged;

        public IntegratedAttestorService()
            : this(new SuiteWorkspaceService())
        {
        }

        public IntegratedAttestorService(SuiteWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        }

        public string? AttestorExecutablePath => _workspaceService.ResolveAttestorExecutablePath();

        public bool IsAvailable => !string.IsNullOrWhiteSpace(AttestorExecutablePath);

        public string AvailabilityMessage => IsAvailable
            ? $"PhantomAttestor is available at {AttestorExecutablePath}."
            : "PhantomAttestor could not be located in this suite build output.";

        // True only while we're tracking a live, still-running instance we launched. A
        // user-launched or previously-orphaned Attestor process isn't tracked — this is for
        // the switcher chip, not general process discovery.
        public bool IsRunning
        {
            get
            {
                try
                {
                    return _trackedProcess is { HasExited: false };
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public bool TryLaunch(out string? errorMessage) => TryLaunch(ownerBounds: null, out errorMessage);

        [SupportedOSPlatform("windows")]
        public bool TryLaunch(PixelBounds? ownerBounds, out string? errorMessage)
        {
            errorMessage = null;

            if (IsRunning)
            {
                BringToFront(ownerBounds);
                return true;
            }

            if (!IsAvailable)
            {
                errorMessage = AvailabilityMessage;
                return false;
            }

            try
            {
                _trackedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = AttestorExecutablePath!,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(AttestorExecutablePath!)!
                });

                if (_trackedProcess != null)
                {
                    _trackedProcess.EnableRaisingEvents = true;
                    _trackedProcess.Exited += (_, _) => RunningStateChanged?.Invoke(this, EventArgs.Empty);
                }

                RunningStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to launch PhantomAttestor: {ex.Message}";
                return false;
            }
        }

        // Brings the tracked Attestor window to the foreground and, when Obscura's own bounds
        // are supplied, resizes/repositions it to match — so switching feels like the same
        // window changing content rather than juggling two separate ones.
        [SupportedOSPlatform("windows")]
        public bool BringToFront(PixelBounds? ownerBounds = null)
        {
            if (_trackedProcess is not { HasExited: false }) return false;

            try
            {
                var hWnd = _trackedProcess.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    // Main window may not be captured yet if we're racing the just-launched
                    // process's startup; refresh and retry once.
                    _trackedProcess.Refresh();
                    hWnd = _trackedProcess.MainWindowHandle;
                }
                if (hWnd == IntPtr.Zero) return false;

                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }

                if (ownerBounds is { } bounds)
                {
                    MoveWindow(hWnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, true);
                }

                SetForegroundWindow(hWnd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    }

    public readonly record struct PixelBounds(int X, int Y, int Width, int Height);
}

