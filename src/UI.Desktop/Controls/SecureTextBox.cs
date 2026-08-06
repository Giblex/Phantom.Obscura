using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.Core.Services.Security;

namespace PhantomVault.UI.Controls
{

    public sealed class SecureTextBox : TextBox
    {
        private readonly AntiKeyloggingService? _antiKeylogging;
        private readonly MemoryProtectionService? _memoryProtection;
        private readonly Stopwatch _keystrokeTimer = new();
        private DispatcherTimer? _obfuscationTimer;
        private bool _isObfuscated;
        private string? _realText;
        private bool _suppressTextChange;

        private const string ObfuscationChars = "●○◐◑◒◓⬤○●◦•·⚫⚪";

        public static readonly StyledProperty<bool> EnableAntiKeyloggingProperty =
            AvaloniaProperty.Register<SecureTextBox, bool>(nameof(EnableAntiKeylogging), defaultValue: true);

        public static readonly StyledProperty<bool> EnableVisualObfuscationProperty =
            AvaloniaProperty.Register<SecureTextBox, bool>(nameof(EnableVisualObfuscation), defaultValue: false);

        public static readonly StyledProperty<bool> DisableClipboardProperty =
            AvaloniaProperty.Register<SecureTextBox, bool>(nameof(DisableClipboard), defaultValue: true);

        public static readonly StyledProperty<bool> ShowVirtualKeyboardButtonProperty =
            AvaloniaProperty.Register<SecureTextBox, bool>(nameof(ShowVirtualKeyboardButton), defaultValue: false);

        public SecureTextBox()
        {

            try
            {
                _antiKeylogging = new AntiKeyloggingService();
                _memoryProtection = new MemoryProtectionService();
            }
            catch
            {

            }

            this.AddHandler(KeyDownEvent, OnSecureKeyDown, RoutingStrategies.Tunnel);
            this.AddHandler(TextInputEvent, OnSecureTextInput, RoutingStrategies.Tunnel);
            this.GotFocus += OnSecureGotFocus;
            this.LostFocus += OnSecureLostFocus;

            _keystrokeTimer.Start();
        }

        public bool EnableAntiKeylogging
        {
            get => GetValue(EnableAntiKeyloggingProperty);
            set => SetValue(EnableAntiKeyloggingProperty, value);
        }

        public bool EnableVisualObfuscation
        {
            get => GetValue(EnableVisualObfuscationProperty);
            set => SetValue(EnableVisualObfuscationProperty, value);
        }

        public bool DisableClipboard
        {
            get => GetValue(DisableClipboardProperty);
            set => SetValue(DisableClipboardProperty, value);
        }

        public bool ShowVirtualKeyboardButton
        {
            get => GetValue(ShowVirtualKeyboardButtonProperty);
            set => SetValue(ShowVirtualKeyboardButtonProperty, value);
        }

#pragma warning disable CS0067
        public event EventHandler? VirtualKeyboardRequested;
#pragma warning restore CS0067

        private void OnSecureKeyDown(object? sender, KeyEventArgs e)
        {

            if (DisableClipboard)
            {
                var modifiers = e.KeyModifiers;
                if (modifiers.HasFlag(KeyModifiers.Control))
                {
                    if (e.Key == Key.C || e.Key == Key.X || e.Key == Key.V)
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (EnableAntiKeylogging && _antiKeylogging != null)
            {
                var timeSinceLastKey = _keystrokeTimer.Elapsed;
                _keystrokeTimer.Restart();

                _antiKeylogging.RegisterKeystroke('\0', timeSinceLastKey);
            }
        }

        private void OnSecureTextInput(object? sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || !EnableAntiKeylogging)
                return;

            int spinIterations = RandomNumberGenerator.GetInt32(1, 101);
            System.Threading.Thread.SpinWait(spinIterations);
        }

        private void OnSecureGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (EnableVisualObfuscation)
            {
                StartObfuscation();
            }

            Background = new SolidColorBrush(Color.Parse("#1A4CAF50"));
        }

        private void OnSecureLostFocus(object? sender, RoutedEventArgs e)
        {
            if (EnableVisualObfuscation)
            {
                StopObfuscation();
            }

            Background = Brushes.Transparent;
        }

        private void StartObfuscation()
        {
            if (_obfuscationTimer != null)
                return;

            _realText = Text;

            _obfuscationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(80, 200))
            };

            _obfuscationTimer.Tick += (s, e) =>
            {
                if (string.IsNullOrEmpty(_realText))
                    return;

                if (RandomNumberGenerator.GetInt32(0, 3) == 0)
                {
                    if (!_isObfuscated)
                    {
                        _isObfuscated = true;
                        _suppressTextChange = true;

                        try
                        {

                            var obfuscated = new StringBuilder(_realText.Length);
                            for (int i = 0; i < _realText.Length; i++)
                            {
                                int charIndex = RandomNumberGenerator.GetInt32(0, ObfuscationChars.Length);
                                obfuscated.Append(ObfuscationChars[charIndex]);
                            }

                            Text = obfuscated.ToString();
                        }
                        finally
                        {
                            _suppressTextChange = false;
                        }
                    }
                }
                else if (_isObfuscated)
                {

                    _isObfuscated = false;
                    _suppressTextChange = true;
                    try
                    {
                        Text = _realText;
                    }
                    finally
                    {
                        _suppressTextChange = false;
                    }
                }

                _obfuscationTimer!.Interval = TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(80, 200));
            };

            _obfuscationTimer.Start();
        }

        private void StopObfuscation()
        {
            _obfuscationTimer?.Stop();
            _obfuscationTimer = null;

            if (_isObfuscated && _realText != null)
            {
                _suppressTextChange = true;
                try
                {
                    Text = _realText;
                }
                finally
                {
                    _suppressTextChange = false;
                }
            }

            _isObfuscated = false;
            _realText = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TextProperty && !_suppressTextChange && EnableVisualObfuscation && _obfuscationTimer != null)
            {
                _realText = Text;
            }
        }

        public byte[]? GetProtectedText()
        {
            if (string.IsNullOrEmpty(Text) || _memoryProtection == null)
                return null;

            return _memoryProtection.ProtectString(Text);
        }

        public void SetProtectedText(byte[] protectedData)
        {
            if (protectedData == null || _memoryProtection == null)
            {
                Text = string.Empty;
                return;
            }

            Text = _memoryProtection.UnprotectString(protectedData);
        }

        public void SecureClear()
        {
            if (_memoryProtection != null && !string.IsNullOrEmpty(Text))
            {
                _memoryProtection.ClearString(Text);
            }

            Text = string.Empty;
            Clear();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            // Honest copy: this field does not "detect keylogging" (no software running in
            // user space can reliably defeat a kernel/global-hook keylogger). What it actually
            // does is disable clipboard exposure and monitor for environmental hostility
            // signals (overlays, screen-capture tooling). Describe only that.
            if (DisableClipboard && EnableAntiKeylogging)
            {
                ToolTip.SetTip(this, "🔒 Secure field — clipboard disabled, input environment monitored");
            }
            else if (DisableClipboard)
            {
                ToolTip.SetTip(this, "🔒 Secure field — clipboard disabled");
            }
            else if (EnableAntiKeylogging)
            {
                ToolTip.SetTip(this, "🔒 Secure field — input environment monitored");
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {

            StopObfuscation();

            this.RemoveHandler(KeyDownEvent, OnSecureKeyDown);
            this.RemoveHandler(TextInputEvent, OnSecureTextInput);

            this.GotFocus -= OnSecureGotFocus;
            this.LostFocus -= OnSecureLostFocus;

            if (_memoryProtection != null && !string.IsNullOrEmpty(Text))
            {
                _memoryProtection.ClearString(Text);
            }
            if (_memoryProtection != null && !string.IsNullOrEmpty(_realText))
            {
                _memoryProtection.ClearString(_realText);
            }
            _realText = null;

            base.OnDetachedFromVisualTree(e);
        }
    }
}

