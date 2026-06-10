using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace PhantomVault.UI.Services
{

    public static class WindowStateManager
    {

        public static void RestoreMainWindowState(Window window, UserSettings settings)
        {
            if (window == null || settings == null)
            {
                return;
            }

            try
            {

                if (settings.MainWindowWidth.HasValue && settings.MainWindowWidth.Value > 0)
                {
                    window.Width = Math.Max(400, settings.MainWindowWidth.Value);
                }

                if (settings.MainWindowHeight.HasValue && settings.MainWindowHeight.Value > 0)
                {
                    window.Height = Math.Max(300, settings.MainWindowHeight.Value);
                }

                if (settings.MainWindowX.HasValue && settings.MainWindowY.HasValue)
                {

                    var screens = window.Screens;
                    var position = new PixelPoint((int)settings.MainWindowX.Value, (int)settings.MainWindowY.Value);

                    if (IsPositionOnScreen(position, screens))
                    {
                        window.Position = position;
                    }
                    else
                    {

                        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        Log.Information("Window position was off-screen, centering window");
                    }
                }
                else
                {

                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                if (!string.IsNullOrWhiteSpace(settings.MainWindowState))
                {
                    if (Enum.TryParse<WindowState>(settings.MainWindowState, out var state))
                    {

                        if (state != WindowState.Minimized)
                        {
                            window.WindowState = state;
                        }
                    }
                }

                Log.Information("MainWindow state restored from settings");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restore MainWindow state, using defaults");
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        public static void SaveMainWindowState(Window window, UserSettings settings)
        {
            if (window == null || settings == null)
            {
                return;
            }

            try
            {

                settings.MainWindowX = window.Position.X;
                settings.MainWindowY = window.Position.Y;

                settings.MainWindowWidth = window.Width;
                settings.MainWindowHeight = window.Height;

                settings.MainWindowState = window.WindowState.ToString();

                Log.Debug("MainWindow state saved: Position=({X}, {Y}), Size=({Width}x{Height}), State={State}",
                    settings.MainWindowX, settings.MainWindowY,
                    settings.MainWindowWidth, settings.MainWindowHeight,
                    settings.MainWindowState);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save MainWindow state");
            }
        }

        private static bool IsPositionOnScreen(PixelPoint position, Screens screens)
        {
            if (screens == null)
            {
                return false;
            }

            foreach (var screen in screens.All)
            {
                var bounds = screen.Bounds;

                if (position.X >= bounds.X && position.X < bounds.X + bounds.Width &&
                    position.Y >= bounds.Y && position.Y < bounds.Y + bounds.Height)
                {
                    return true;
                }
            }

            return false;
        }

        public static void AttachStateChangeHandlers(Window window, Action onStateChanged)
        {
            if (window == null || onStateChanged == null)
            {
                return;
            }

            window.PositionChanged += (s, e) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    onStateChanged();
                }
            };

            var sizeChangedDisposable = window.GetObservable(Window.BoundsProperty).Subscribe(_ =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    onStateChanged();
                }
            });

            window.GetObservable(Window.WindowStateProperty).Subscribe(_ =>
            {
                onStateChanged();
            });

            window.Closed += (s, e) =>
            {
                sizeChangedDisposable?.Dispose();
                onStateChanged();
            };
        }
    }
}

