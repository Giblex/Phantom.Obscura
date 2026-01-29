using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Manages window state persistence, including position, size, and maximized/minimized state.
    /// Ensures windows remember their last position and size across application restarts.
    /// </summary>
    public static class WindowStateManager
    {
        /// <summary>
        /// Restores the MainWindow state from persisted settings.
        /// </summary>
        /// <param name="window">The window to restore state for.</param>
        /// <param name="settings">The persisted settings containing window state.</param>
        public static void RestoreMainWindowState(Window window, UserSettings settings)
        {
            if (window == null || settings == null)
            {
                return;
            }

            try
            {
                // Restore window size
                if (settings.MainWindowWidth.HasValue && settings.MainWindowWidth.Value > 0)
                {
                    window.Width = Math.Max(400, settings.MainWindowWidth.Value); // Minimum 400px width
                }

                if (settings.MainWindowHeight.HasValue && settings.MainWindowHeight.Value > 0)
                {
                    window.Height = Math.Max(300, settings.MainWindowHeight.Value); // Minimum 300px height
                }

                // Restore window position
                if (settings.MainWindowX.HasValue && settings.MainWindowY.HasValue)
                {
                    // Validate position is on screen
                    var screens = window.Screens;
                    var position = new PixelPoint((int)settings.MainWindowX.Value, (int)settings.MainWindowY.Value);
                    
                    if (IsPositionOnScreen(position, screens))
                    {
                        window.Position = position;
                    }
                    else
                    {
                        // Position is off-screen, center on primary screen
                        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        Log.Information("Window position was off-screen, centering window");
                    }
                }
                else
                {
                    // No saved position, center on screen
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                // Restore window state (Normal, Minimized, Maximized)
                if (!string.IsNullOrWhiteSpace(settings.MainWindowState))
                {
                    if (Enum.TryParse<WindowState>(settings.MainWindowState, out var state))
                    {
                        // Don't restore minimized state on startup (would be confusing)
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

        /// <summary>
        /// Saves the current MainWindow state to settings.
        /// </summary>
        /// <param name="window">The window to save state from.</param>
        /// <param name="settings">The settings object to save state to.</param>
        public static void SaveMainWindowState(Window window, UserSettings settings)
        {
            if (window == null || settings == null)
            {
                return;
            }

            try
            {
                // Save position
                settings.MainWindowX = window.Position.X;
                settings.MainWindowY = window.Position.Y;

                // Save size
                settings.MainWindowWidth = window.Width;
                settings.MainWindowHeight = window.Height;

                // Save window state
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

        /// <summary>
        /// Checks if a position is visible on any screen.
        /// </summary>
        private static bool IsPositionOnScreen(PixelPoint position, Screens screens)
        {
            if (screens == null)
            {
                return false;
            }

            foreach (var screen in screens.All)
            {
                var bounds = screen.Bounds;
                
                // Check if position is within screen bounds (with some tolerance)
                if (position.X >= bounds.X && position.X < bounds.X + bounds.Width &&
                    position.Y >= bounds.Y && position.Y < bounds.Y + bounds.Height)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attaches event handlers to automatically save window state when the window is moved or resized.
        /// </summary>
        /// <param name="window">The window to monitor.</param>
        /// <param name="onStateChanged">Action to call when state changes (typically to save settings).</param>
        public static void AttachStateChangeHandlers(Window window, Action onStateChanged)
        {
            if (window == null || onStateChanged == null)
            {
                return;
            }

            // Save state when window is moved
            window.PositionChanged += (s, e) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    onStateChanged();
                }
            };

            // Save state when window is resized
            var sizeChangedDisposable = window.GetObservable(Window.BoundsProperty).Subscribe(_ =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    onStateChanged();
                }
            });

            // Save state when window state changes (maximized, restored, etc.)
            window.GetObservable(Window.WindowStateProperty).Subscribe(_ =>
            {
                onStateChanged();
            });

            // Clean up on window close
            window.Closed += (s, e) =>
            {
                sizeChangedDisposable?.Dispose();
                onStateChanged(); // Final save on close
            };
        }
    }
}
