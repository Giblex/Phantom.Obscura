using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PhantomVault.UI.Desktop.Controls;
using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Desktop.Services;

/// <summary>
/// Global service for managing toast notifications across the application.
/// Displays notifications in a stack at the top-right of the window.
/// </summary>
public sealed class ToastNotificationManager
{
    private static ToastNotificationManager? _instance;
    private Panel? _container;
    private readonly List<ToastNotification> _activeToasts = new();
    private const int MaxToasts = 3;
    private const int ToastSpacing = 12;

    public static ToastNotificationManager Instance => _instance ??= new ToastNotificationManager();

    private ToastNotificationManager()
    {
    }

    /// <summary>
    /// Initialize the toast manager with a container panel.
    /// Call this from your main window after it's loaded.
    /// </summary>
    public void Initialize(Panel container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    /// <summary>
    /// Show a success toast notification.
    /// </summary>
    public void ShowSuccess(string title, string message = "", int durationMs = 4000)
    {
        Show(ToastType.Success, title, message, durationMs);
    }

    /// <summary>
    /// Show an error toast notification.
    /// </summary>
    public void ShowError(string title, string message = "", int durationMs = 5000)
    {
        Show(ToastType.Error, title, message, durationMs);
    }

    /// <summary>
    /// Show a warning toast notification.
    /// </summary>
    public void ShowWarning(string title, string message = "", int durationMs = 4500)
    {
        Show(ToastType.Warning, title, message, durationMs);
    }

    /// <summary>
    /// Show an info toast notification.
    /// </summary>
    public void ShowInfo(string title, string message = "", int durationMs = 4000)
    {
        Show(ToastType.Info, title, message, durationMs);
    }

    /// <summary>
    /// Show a toast notification with custom type and duration.
    /// </summary>
    private void Show(ToastType type, string title, string message, int durationMs)
    {
        if (_container == null)
        {
            System.Diagnostics.Debug.WriteLine("[ToastManager] Not initialized! Call Initialize() first.");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // Remove oldest toast if at max capacity
            if (_activeToasts.Count >= MaxToasts)
            {
                var oldestToast = _activeToasts[0];
                RemoveToast(oldestToast);
            }

            // Create new toast
            var toast = new ToastNotification();
            toast.HorizontalAlignment = HorizontalAlignment.Right;
            toast.VerticalAlignment = VerticalAlignment.Top;
            toast.Margin = new Avalonia.Thickness(0, GetTopMargin(), 20, 0);
            toast.Closed += (s, e) => RemoveToast(toast);

            // Add to container and active list
            _container.Children.Add(toast);
            _activeToasts.Add(toast);

            // Show the toast
            toast.Show(type, title, message, durationMs);

            // Reposition existing toasts
            RepositionToasts();
        });
    }

    /// <summary>
    /// Remove a toast from the display and active list.
    /// </summary>
    private void RemoveToast(ToastNotification toast)
    {
        if (_container == null)
            return;

        _activeToasts.Remove(toast);
        _container.Children.Remove(toast);
        RepositionToasts();
    }

    /// <summary>
    /// Reposition all active toasts with staggered vertical spacing.
    /// </summary>
    private void RepositionToasts()
    {
        for (int i = 0; i < _activeToasts.Count; i++)
        {
            var toast = _activeToasts[i];
            var topMargin = GetTopMargin(i);
            toast.Margin = new Avalonia.Thickness(0, topMargin, 20, 0);
        }
    }

    /// <summary>
    /// Calculate the top margin for a toast at the given index.
    /// </summary>
    private double GetTopMargin(int index = -1)
    {
        if (index < 0)
            index = _activeToasts.Count;

        // Stack toasts from top, with spacing between them
        // Approximate toast height: 80px
        const double toastHeight = 80;
        return 20 + (index * (toastHeight + ToastSpacing));
    }

    /// <summary>
    /// Clear all active toasts immediately.
    /// </summary>
    public void ClearAll()
    {
        if (_container == null)
            return;

        var toastsToRemove = _activeToasts.ToArray();
        foreach (var toast in toastsToRemove)
        {
            toast.Close();
        }
    }
}
