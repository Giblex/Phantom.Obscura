using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Linq;

namespace PhantomVault.UI.Controls
{

    public partial class VirtualKeyboard : UserControl
    {
        private Button? _closeButton;
        private Button? _shiftButton;
        private Button? _backspaceButton;
        private TextBlock? _statusText;
        private bool _isShiftPressed;
        private string _inputBuffer = string.Empty;

        public VirtualKeyboard()
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
            _closeButton = this.FindControl<Button>("CloseButton");
            _shiftButton = this.FindControl<Button>("ShiftButton");
            _backspaceButton = this.FindControl<Button>("BackspaceButton");
            _statusText = this.FindControl<TextBlock>("StatusText");

            if (_closeButton != null)
                _closeButton.Click += OnCloseClicked;

            if (_shiftButton != null)
                _shiftButton.Click += OnShiftClicked;

            if (_backspaceButton != null)
                _backspaceButton.Click += OnBackspaceClicked;

            AttachKeyHandlers(this);
        }

        public event EventHandler<KeyClickedEventArgs>? KeyClicked;

        public event EventHandler? Closed;

        public string InputBuffer => _inputBuffer;

        public void ClearBuffer()
        {
            _inputBuffer = string.Empty;
            UpdateStatus();
        }

        public bool IsShiftPressed => _isShiftPressed;

        private void AttachKeyHandlers(Control control)
        {
            if (control is Button button && button.Classes.Contains("key-btn"))
            {

                if (button == _shiftButton || button == _backspaceButton || button == _closeButton)
                    return;

                button.Click += OnKeyButtonClicked;
            }

            if (control is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Control childControl)
                        AttachKeyHandlers(childControl);
                }
            }
            else if (control is ContentControl contentControl && contentControl.Content is Control content)
            {
                AttachKeyHandlers(content);
            }
        }

        private void OnKeyButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var key = button.Tag?.ToString();
            if (string.IsNullOrEmpty(key))
                return;

            if (_isShiftPressed && key.Length == 1 && char.IsLetter(key[0]))
            {
                key = key.ToUpperInvariant();
            }
            else if (!_isShiftPressed && key.Length == 1 && char.IsLetter(key[0]))
            {
                key = key.ToLowerInvariant();
            }

            _inputBuffer += key;

            if (_isShiftPressed)
            {
                _isShiftPressed = false;
                UpdateShiftState();
            }

            UpdateStatus();

            KeyClicked?.Invoke(this, new KeyClickedEventArgs { Key = key });
        }

        private void OnShiftClicked(object? sender, RoutedEventArgs e)
        {
            _isShiftPressed = !_isShiftPressed;
            UpdateShiftState();
        }

        private void OnBackspaceClicked(object? sender, RoutedEventArgs e)
        {
            if (_inputBuffer.Length > 0)
            {
                _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                UpdateStatus();

                KeyClicked?.Invoke(this, new KeyClickedEventArgs { Key = "\b" });
            }
        }

        private void OnCloseClicked(object? sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateShiftState()
        {
            if (_shiftButton != null)
            {
                _shiftButton.Background = _isShiftPressed
                    ? Avalonia.Media.Brushes.DodgerBlue
                    : Avalonia.Media.Brush.Parse("#2B2B2B");
            }
        }

        private void UpdateStatus()
        {
            if (_statusText != null)
            {
                var maskedLength = _inputBuffer.Length;
                _statusText.Text = maskedLength > 0
                    ? $"{maskedLength} character{(maskedLength != 1 ? "s" : "")} entered"
                    : "Click keys to input securely";
            }
        }

        public void RandomizeLayout()
        {

        }
    }

    public sealed class KeyClickedEventArgs : EventArgs
    {
        public string Key { get; set; } = string.Empty;
    }
}

