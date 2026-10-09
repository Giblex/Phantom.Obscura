using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services.AutoFill;
using Serilog;

namespace PhantomVault.UI.Services.TrayBackground
{

    public sealed class TrayBackgroundService : ITrayBackgroundService
    {
        private readonly IUsbDetector _usbDetector;
        private readonly IAutoFillOrchestrator _orchestrator;

        private TrayIcon? _trayIcon;
        private bool _isRunning;
        private bool _disposed;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// The vault's USB was inserted while the vault was locked. Raised instead of running the
        /// autofill flow, which would abort at its first guard with nothing to fill from. The
        /// handler is expected to put the unlock prompt in front of the user; autofill resumes on
        /// the next insertion event once the vault is open.
        /// </summary>
        public event Action<string>? VaultUnlockRequested;

        /// <summary>
        /// A removable drive was pulled out. The handler decides whether it was the drive backing
        /// the open vault and, if so, locks it: an unlocked vault whose key material has physically
        /// left the machine must not stay open.
        /// </summary>
        public event Action<string>? DriveRemoved;

        public TrayBackgroundService(IUsbDetector usbDetector, IAutoFillOrchestrator orchestrator)
        {
            _usbDetector = usbDetector;
            _orchestrator = orchestrator;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            if (_isRunning) return Task.CompletedTask;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _trayIcon = new TrayIcon
                {
                    ToolTipText = "Phantom Obscura — AutoFill Mode Active",
                    Icon = GetAppIcon(),
                    Menu = BuildContextMenu()
                };
                _trayIcon.Clicked += OnTrayIconClicked;
            });

            _usbDetector.RemovableDriveInserted += OnUsbInserted;
            _usbDetector.RemovableDriveRemoved += OnUsbRemoved;
            _isRunning = true;

            Log.Information("[TrayBackground] AutoFill Mode started — listening for USB insertion");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (!_isRunning) return Task.CompletedTask;

            _usbDetector.RemovableDriveInserted -= OnUsbInserted;
            _usbDetector.RemovableDriveRemoved -= OnUsbRemoved;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
            });

            _isRunning = false;
            Log.Information("[TrayBackground] AutoFill Mode stopped");
            return Task.CompletedTask;
        }

        private async void OnUsbInserted(string drivePath)
        {
            try
            {
                // Give the OS a moment to finish mounting before anything reads the drive.
                await Task.Delay(500);

                if (!_orchestrator.IsVaultReady)
                {
                    // Locked: running the flow now would abort at its first guard and look like
                    // nothing happened. Ask for the vault to be opened instead.
                    Log.Information("[TrayBackground] USB inserted with a locked vault — requesting unlock for {Drive}", drivePath);
                    Dispatcher.UIThread.Post(() => VaultUnlockRequested?.Invoke(drivePath));
                    return;
                }

                await _orchestrator.RunAutoFillFlowAsync(drivePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TrayBackground] Error during USB-triggered auto-fill");
            }
        }

        private void OnUsbRemoved(string drivePath)
        {
            try
            {
                Log.Information("[TrayBackground] Removable drive removed: {Drive}", drivePath);
                Dispatcher.UIThread.Post(() => DriveRemoved?.Invoke(drivePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TrayBackground] Error handling USB removal");
            }
        }

        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    // Prefer the currently-live vault window (the unlocked session) over
                    // MainWindow if they've diverged — e.g. MainWindow still points at the
                    // welcome window after unlock. Falls back to MainWindow otherwise.
                    var win = desktop.Windows
                        .FirstOrDefault(w => w.GetType().Name == "VaultWindow")
                        ?? desktop.MainWindow;
                    if (win is null) return;
                    win.Show();
                    if (win.WindowState == WindowState.Minimized)
                        win.WindowState = WindowState.Normal;
                    win.Activate();
                    win.BringIntoView();
                }
            });
        }

        private NativeMenu BuildContextMenu()
        {
            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open Phantom Obscura");
            openItem.Click += (_, _) => OnTrayIconClicked(null, EventArgs.Empty);
            menu.Add(openItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) =>
            {
                _ = StopAsync();
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    lifetime.Shutdown();
            };
            menu.Add(exitItem);

            return menu;
        }

        private static WindowIcon? GetAppIcon()
        {
            try
            {
                var uri = new Uri("avares://PhantomVault.UI/Assets/phantom_obscura_tray.ico");
                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = StopAsync();
        }
    }
}

