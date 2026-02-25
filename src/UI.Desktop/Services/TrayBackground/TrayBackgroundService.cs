using System;
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
    /// <summary>
    /// Owns the system-tray icon and wires USB insertion events to the
    /// <see cref="IAutoFillOrchestrator"/>. When AutoFill Mode is enabled the
    /// main window hides to tray on close rather than exiting the process.
    /// </summary>
    public sealed class TrayBackgroundService : ITrayBackgroundService
    {
        private readonly IUsbDetector _usbDetector;
        private readonly IAutoFillOrchestrator _orchestrator;

        private TrayIcon? _trayIcon;
        private bool _isRunning;
        private bool _disposed;

        public bool IsRunning => _isRunning;

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
            _isRunning = true;

            Log.Information("[TrayBackground] AutoFill Mode started — listening for USB insertion");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (!_isRunning) return Task.CompletedTask;

            _usbDetector.RemovableDriveInserted -= OnUsbInserted;

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
                // Brief delay so the drive mounts fully before we try to read from it
                await Task.Delay(500);
                await _orchestrator.RunAutoFillFlowAsync(drivePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TrayBackground] Error during USB-triggered auto-fill");
            }
        }

        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var win = desktop.MainWindow;
                    if (win is null) return;
                    win.Show();
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
                var uri = new Uri("avares://PhantomVault.UI/Assets/phantom_icon.ico");
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
