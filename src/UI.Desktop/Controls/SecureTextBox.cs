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
    /// <summary>
    /// Secure text input control with anti-keylogging features and visual obfuscation.
    /// Visual obfuscation periodically replaces displayed characters with random symbols
    /// to defeat screen-capture and shoulder-surfing attacks.
    /// </summary>
    public sealed class SecureTextBox : TextBox
    {
        private readonly AntiKeyloggingService? _antiKeylogging;
        private readonly MemoryProtectionService? _memoryProtection;
        private readonly Stopwatch _keystrokeTimer = new();
        private DispatcherTimer? _obfuscationTimer;
        private bool _isObfuscated;
        private string? _realText; // Stores the actual text during obfuscation
        private bool _suppressTextChange; // Prevents recursion during obfuscation
        
        // Characters used for visual obfuscation (mix of symbols that look similar)
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
            // Initialize security services if available
            try
            {
                _antiKeylogging = new AntiKeyloggingService();
                _memoryProtection = new MemoryProtectionService();
            }
            catch
            {
                // Services not available
            }

            // Override default behaviors
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

        /// <summary>
        /// Event raised when virtual keyboard button is clicked.
        /// </summary>
#pragma warning disable CS0067 // Event is never used (reserved for future virtual keyboard integration)
        public event EventHandler? VirtualKeyboardRequested;
#pragma warning restore CS0067

        private void OnSecureKeyDown(object? sender, KeyEventArgs e)
        {
            // Block clipboard shortcuts if disabled
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

            // Register keystroke for pattern analysis
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

            // Add random microsecond delay to obfuscate timing using cryptographically secure RNG
            int spinIterations = RandomNumberGenerator.GetInt32(1, 101);
            System.Threading.Thread.SpinWait(spinIterations);
        }

        private void OnSecureGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (EnableVisualObfuscation)
            {
                StartObfuscation();
            }

            // Change background to indicate secure mode
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

            // Store the real text before starting obfuscation
            _realText = Text;

            // Create timer that periodically replaces displayed characters with random symbols
            _obfuscationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(80, 200)) // Random interval for unpredictability
            };

            _obfuscationTimer.Tick += (s, e) =>
            {
                if (string.IsNullOrEmpty(_realText))
                    return;

                // Randomly decide whether to show obfuscated or real text
                if (RandomNumberGenerator.GetInt32(0, 3) == 0) // 33% chance to obfuscate
                {
                    if (!_isObfuscated)
                    {
                        _isObfuscated = true;
                        _suppressTextChange = true;
                        
                        try
                        {
                            // Generate random obfuscation characters matching text length
                            var obfuscated = new StringBuilder(_realText.Length);
                            for (int i = 0; i < _realText.Length; i++)
                            {
                                int charIndex = RandomNumberGenerator.GetInt32(0, ObfuscationChars.Length);
                                obfuscated.Append(ObfuscationChars[charIndex]);
                            }
                            
                            // Briefly display obfuscated text
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
                    // Restore real text
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
                
                // Randomize next interval for unpredictability
                _obfuscationTimer!.Interval = TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(80, 200));
            };

            _obfuscationTimer.Start();
        }

        private void StopObfuscation()
        {
            _obfuscationTimer?.Stop();
            _obfuscationTimer = null;
            
            // Ensure real text is restored when stopping
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
        
        /// <summary>
        /// Handles text property changes to track real text during obfuscation.
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            
            // Track text changes when in obfuscation mode
            if (change.Property == TextProperty && !_suppressTextChange && EnableVisualObfuscation && _obfuscationTimer != null)
            {
                _realText = Text;
            }
        }

        /// <summary>
        /// Gets the text as a protected byte array.
        /// </summary>
        public byte[]? GetProtectedText()
        {
            if (string.IsNullOrEmpty(Text) || _memoryProtection == null)
                return null;

            return _memoryProtection.ProtectString(Text);
        }

        /// <summary>
        /// Sets text from a protected byte array.
        /// </summary>
        public void SetProtectedText(byte[] protectedData)
        {
            if (protectedData == null || _memoryProtection == null)
            {
                Text = string.Empty;
                return;
            }

            Text = _memoryProtection.UnprotectString(protectedData);
        }

        /// <summary>
        /// Securely clears the text content.
        /// </summary>
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

            // Add security indicator
            if (EnableAntiKeylogging || DisableClipboard)
            {
                ToolTip.SetTip(this, "🔒 Secure input - Protected against keylogging");
            }
        }

        /// <summary>
        /// Cleans up event handlers and timers when the control is removed from the visual tree
        /// to prevent memory leaks (DispatcherTimer is rooted by the Dispatcher).
        /// </summary>
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Stop obfuscation timer first — it holds a closure over this instance
            StopObfuscation();

            // Unsubscribe tunnelled event handlers
            this.RemoveHandler(KeyDownEvent, OnSecureKeyDown);
            this.RemoveHandler(TextInputEvent, OnSecureTextInput);

            // Unsubscribe routed event handlers
            this.GotFocus -= OnSecureGotFocus;
            this.LostFocus -= OnSecureLostFocus;

            // Securely clear any residual text in memory
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
