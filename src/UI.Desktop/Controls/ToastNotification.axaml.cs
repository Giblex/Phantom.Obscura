using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using System;
using System.Threading.Tasks;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Toast notification control for non-intrusive user feedback.
/// Automatically dismisses after a timeout or can be closed manually.
/// </summary>
public partial class ToastNotification : UserControl
{
    private Border? _toastBorder;
    private SvgIcon? _toastIcon;
    private TextBlock? _titleText;
    private TextBlock? _messageText;
    private Button? _closeButton;
    private DispatcherTimer? _autoCloseTimer;

    public event EventHandler? Closed;

    public ToastNotification()
    {
        InitializeComponent();

        _toastBorder = this.FindControl<Border>("ToastBorder");
        _toastIcon = this.FindControl<SvgIcon>("ToastIcon");
        _titleText = this.FindControl<TextBlock>("TitleText");
        _messageText = this.FindControl<TextBlock>("MessageText");
        _closeButton = this.FindControl<Button>("CloseButton");

        if (_closeButton != null)
        {
            _closeButton.Click += (s, e) => Close();
        }
    }

    /// <summary>
    /// Show the toast notification with specified type, title, and message.
    /// </summary>
    public void Show(ToastType type, string title, string message, int durationMs = 4000)
    {
        if (_toastBorder == null || _toastIcon == null || _titleText == null || _messageText == null)
            return;

        // Set content
        _titleText.Text = title;
        _messageText.Text = message;
        _messageText.IsVisible = !string.IsNullOrWhiteSpace(message);

        // Set icon and styling based on type
        _toastBorder.Classes.Clear();
        _toastBorder.Classes.Add("toast");
        _toastBorder.Classes.Add("show");

        switch (type)
        {
            case ToastType.Success:
                _toastBorder.Classes.Add("success");
                _toastIcon.Preset = IconPreset.Check;
                break;
            case ToastType.Error:
                _toastBorder.Classes.Add("error");
                _toastIcon.Preset = IconPreset.AlertCircle;
                break;
            case ToastType.Warning:
                _toastBorder.Classes.Add("warning");
                _toastIcon.Preset = IconPreset.AlertTriangle;
                break;
            case ToastType.Info:
            default:
                _toastBorder.Classes.Add("info");
                _toastIcon.Preset = IconPreset.Info;
                break;
        }

        // Auto-close after duration
        _autoCloseTimer?.Stop();
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            Close();
            _autoCloseTimer.Stop();
        };
        _autoCloseTimer.Start();
    }

    /// <summary>
    /// Close the toast notification with animation.
    /// </summary>
    public void Close()
    {
        if (_toastBorder == null)
            return;

        _toastBorder.Classes.Remove("show");

        // Wait for animation to complete before raising Closed event
        Task.Delay(300).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Closed?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Type of toast notification determining icon and color.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}
