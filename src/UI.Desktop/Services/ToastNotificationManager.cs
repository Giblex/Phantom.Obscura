using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PhantomVault.UI.Desktop.Controls;
using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Desktop.Services;

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

    public void Initialize(Panel container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public void ShowSuccess(string title, string message = "", int durationMs = 4000)
    {
        Show(ToastType.Success, title, message, durationMs);
    }

    public void ShowError(string title, string message = "", int durationMs = 5000)
    {
        RecentIssuesLog.Instance.Record(IssueSeverity.Error, title, message);
        Show(ToastType.Error, title, message, durationMs);
    }

    public void ShowWarning(string title, string message = "", int durationMs = 4500)
    {
        RecentIssuesLog.Instance.Record(IssueSeverity.Warning, title, message);
        Show(ToastType.Warning, title, message, durationMs);
    }

    public void ShowInfo(string title, string message = "", int durationMs = 4000)
    {
        Show(ToastType.Info, title, message, durationMs);
    }

    private void Show(ToastType type, string title, string message, int durationMs)
    {
        if (_container == null)
        {
            System.Diagnostics.Debug.WriteLine("[ToastManager] Not initialized! Call Initialize() first.");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {

            if (_activeToasts.Count >= MaxToasts)
            {
                var oldestToast = _activeToasts[0];
                RemoveToast(oldestToast);
            }

            var toast = new ToastNotification();
            toast.HorizontalAlignment = HorizontalAlignment.Right;
            toast.VerticalAlignment = VerticalAlignment.Top;
            toast.Margin = new Avalonia.Thickness(0, GetTopMargin(), 20, 0);
            toast.Closed += (s, e) => RemoveToast(toast);

            _container.Children.Add(toast);
            _activeToasts.Add(toast);

            toast.Show(type, title, message, durationMs);

            RepositionToasts();
        });
    }

    private void RemoveToast(ToastNotification toast)
    {
        if (_container == null)
            return;

        _activeToasts.Remove(toast);
        _container.Children.Remove(toast);
        RepositionToasts();
    }

    private void RepositionToasts()
    {
        for (int i = 0; i < _activeToasts.Count; i++)
        {
            var toast = _activeToasts[i];
            var topMargin = GetTopMargin(i);
            toast.Margin = new Avalonia.Thickness(0, topMargin, 20, 0);
        }
    }

    private double GetTopMargin(int index = -1)
    {
        if (index < 0)
            index = _activeToasts.Count;

        const double toastHeight = 80;
        return 20 + (index * (toastHeight + ToastSpacing));
    }

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

