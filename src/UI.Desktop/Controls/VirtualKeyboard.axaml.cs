using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Linq;

namespace PhantomVault.UI.Controls
{
    /// <summary>
    /// Virtual on-screen keyboard for secure password entry without physical keyboard.
    /// </summary>
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

            // Attach click handlers to all key buttons
            AttachKeyHandlers(this);
        }

        /// <summary>
        /// Event raised when a key is clicked.
        /// </summary>
        public event EventHandler<KeyClickedEventArgs>? KeyClicked;

        /// <summary>
        /// Event raised when the keyboard is closed.
        /// </summary>
        public event EventHandler? Closed;

        /// <summary>
        /// Gets the current input buffer.
        /// </summary>
        public string InputBuffer => _inputBuffer;

        /// <summary>
        /// Clears the input buffer.
        /// </summary>
        public void ClearBuffer()
        {
            _inputBuffer = string.Empty;
            UpdateStatus();
        }

        /// <summary>
        /// Gets whether shift is currently pressed.
        /// </summary>
        public bool IsShiftPressed => _isShiftPressed;

        private void AttachKeyHandlers(Control control)
        {
            if (control is Button button && button.Classes.Contains("key-btn"))
            {
                // Skip special buttons that have their own handlers
                if (button == _shiftButton || button == _backspaceButton || button == _closeButton)
                    return;

                button.Click += OnKeyButtonClicked;
            }

            // Recursively attach to children
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

            // Apply shift modifier
            if (_isShiftPressed && key.Length == 1 && char.IsLetter(key[0]))
            {
                key = key.ToUpperInvariant();
            }
            else if (!_isShiftPressed && key.Length == 1 && char.IsLetter(key[0]))
            {
                key = key.ToLowerInvariant();
            }

            _inputBuffer += key;

            // Reset shift after key press
            if (_isShiftPressed)
            {
                _isShiftPressed = false;
                UpdateShiftState();
            }

            UpdateStatus();

            // Raise event
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

                // Raise backspace event
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

        /// <summary>
        /// Randomizes key positions for additional security.
        /// </summary>
        public void RandomizeLayout()
        {
            // This would shuffle the key positions
            // Implementation left as exercise - would require rebuilding the layout
        }
    }

    /// <summary>
    /// Event args for key click events.
    /// </summary>
    public sealed class KeyClickedEventArgs : EventArgs
    {
        public string Key { get; set; } = string.Empty;
    }
}
