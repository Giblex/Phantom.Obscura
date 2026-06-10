using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Controls
{

    public partial class PasswordCaptureToast : UserControl
    {
        private TextBlock? _titleText;
        private TextBlock? _messageText;
        private Button? _closeButton;
        private Button? _ignoreButton;
        private Button? _actionButton;
        private DispatcherTimer? _autoHideTimer;

        public PasswordCaptureToast()
        {
            InitializeComponent();
            SetupControls();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SetupControls()
        {
            _titleText = this.FindControl<TextBlock>("TitleText");
            _messageText = this.FindControl<TextBlock>("MessageText");
            _closeButton = this.FindControl<Button>("CloseButton");
            _ignoreButton = this.FindControl<Button>("IgnoreButton");
            _actionButton = this.FindControl<Button>("ActionButton");

            if (_closeButton != null)
                _closeButton.Click += OnCloseClicked;

            if (_ignoreButton != null)
                _ignoreButton.Click += OnIgnoreClicked;

            if (_actionButton != null)
                _actionButton.Click += OnActionClicked;
        }

        public event EventHandler? ActionClicked;

        public event EventHandler? Dismissed;

        public void ShowNewPasswordCapture(PasswordCaptureEventArgs args)
        {
            if (_titleText != null)
                _titleText.Text = "New Password Detected";

            if (_messageText != null)
                _messageText.Text = args.CaptureType == CaptureType.Registration
                    ? $"Save credentials for {args.Username} at {args.Domain}?"
                    : $"Save login for {args.Domain}?";

            if (_actionButton != null)
                _actionButton.Content = "Save";

            IsVisible = true;
            StartAutoHideTimer();
        }

        public void ShowPasswordChange(PasswordChangeEventArgs args)
        {
            if (_titleText != null)
                _titleText.Text = "Password Changed";

            if (_messageText != null)
                _messageText.Text = $"Update password for {args.ExistingCredential.Username} at {args.Domain}?";

            if (_actionButton != null)
                _actionButton.Content = "Update";

            IsVisible = true;
            StartAutoHideTimer();
        }

        public void Hide()
        {
            IsVisible = false;
            StopAutoHideTimer();
        }

        private void OnCloseClicked(object? sender, RoutedEventArgs e)
        {
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private void OnIgnoreClicked(object? sender, RoutedEventArgs e)
        {
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private void OnActionClicked(object? sender, RoutedEventArgs e)
        {
            ActionClicked?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        private void StartAutoHideTimer()
        {
            StopAutoHideTimer();

            _autoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };

            _autoHideTimer.Tick += (s, e) =>
            {
                Hide();
                Dismissed?.Invoke(this, EventArgs.Empty);
            };

            _autoHideTimer.Start();
        }

        private void StopAutoHideTimer()
        {
            if (_autoHideTimer != null)
            {
                _autoHideTimer.Stop();
                _autoHideTimer = null;
            }
        }
    }
}

