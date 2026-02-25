using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.UI;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Central helper that launches the icon library window from any view model.
    /// </summary>
    public static class IconLibraryLauncher
    {
        private static readonly DialogService DialogService = new();

        public static async Task ShowAsync(Window? owner, string? contextTitle = null)
        {
            var title = string.IsNullOrWhiteSpace(contextTitle) ? "Icon Library" : contextTitle!;
            Window? loadingWindow = null;

            try
            {
                IconManager? iconManager = null;
                if (Application.Current is App app && app.Services != null)
                {
                    iconManager = app.Services.GetService(typeof(IconManager)) as IconManager;
                }

                // Default to the root Assets/Visuals so the manager can search subfolders (App Icons, Entry Logos, etc.)
                iconManager ??= new IconManager(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Visuals"));

                Window? ownerToUse = owner;
                if (ownerToUse == null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                {
                    ownerToUse = desktopLifetime.MainWindow;
                }

                loadingWindow = await ShowLoadingWindowAsync(ownerToUse, title);

                var managerViewModel = new IconManagerViewModel(iconManager);
                var window = new IconManagerWindow
                {
                    DataContext = managerViewModel
                };

                // Set owner window for file pickers and dialogs and the caller owner (category manager window if provided)
                managerViewModel.SetOwnerWindow(window, ownerToUse);

                // If an owner is available, set the window owner and show non-modally
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        Console.WriteLine("[ICON-LAUNCHER] Showing IconManagerWindow (non-modal)");
                    }
                    catch { }
                    // Preserve the original owner for the view model via managerViewModel.SetOwnerWindow(...)
                    window.Show();
                    return true;
                });
            }
            catch (Exception ex)
            {
                Window? ownerToUse = owner;
                if (ownerToUse == null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    ownerToUse = desktop.MainWindow;
                }

                await CloseLoadingWindowAsync(loadingWindow);
                loadingWindow = null;

                await DialogService.ShowErrorAsync(title, $"Failed to open icon library: {ex.Message}", ownerToUse);
            }
            finally
            {
                await CloseLoadingWindowAsync(loadingWindow);
            }
        }

        private static Task<Window> ShowLoadingWindowAsync(Window? owner, string title)
        {
            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                var loadingWindow = CreateLoadingWindow(title);
                loadingWindow.WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

                if (owner != null)
                {
                    loadingWindow.Show(owner);
                }
                else
                {
                    loadingWindow.Show();
                }

                return loadingWindow;
            }).GetTask();
        }

        private static Task CloseLoadingWindowAsync(Window? loadingWindow)
        {
            if (loadingWindow == null)
            {
                return Task.CompletedTask;
            }

            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    loadingWindow.Close();
                }
                catch
                {
                    // ignore
                }
            }).GetTask();
        }

        private static Window CreateLoadingWindow(string contextTitle)
        {
            var text = new TextBlock
            {
                Text = $"Opening {contextTitle}...",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 6,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var stack = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(text);
            stack.Children.Add(progress);

            var border = new Border
            {
                Padding = new Thickness(24, 18),
                Background = new SolidColorBrush(Color.Parse("#1B2838")),
                CornerRadius = new CornerRadius(10),
                Child = stack
            };

            var window = new Window
            {
                Width = 360,
                Height = 160,
                CanResize = false,
                ShowInTaskbar = false,
                SystemDecorations = SystemDecorations.BorderOnly,
                Title = contextTitle,
                Content = border
            };

            return window;
        }
    }
}
