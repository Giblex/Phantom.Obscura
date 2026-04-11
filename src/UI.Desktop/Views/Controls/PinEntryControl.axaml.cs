using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Key = Avalonia.Input.Key;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;

namespace PhantomVault.UI.Views.Controls
{
    public partial class PinEntryControl : UserControl
    {
        /// <summary>Current PIN string entered by the user.</summary>
        public static readonly StyledProperty<string> PinProperty =
            AvaloniaProperty.Register<PinEntryControl, string>(nameof(Pin), defaultValue: string.Empty,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        /// <summary>Maximum number of PIN digits.</summary>
        public static readonly StyledProperty<int> MaxLengthProperty =
            AvaloniaProperty.Register<PinEntryControl, int>(nameof(MaxLength), defaultValue: 6);

        /// <summary>Minimum digits required before Unlock is enabled.</summary>
        public static readonly StyledProperty<int> MinLengthProperty =
            AvaloniaProperty.Register<PinEntryControl, int>(nameof(MinLength), defaultValue: 4);

        /// <summary>Command executed when the user presses Unlock or Enter after reaching MinLength.</summary>
        public static readonly StyledProperty<ICommand?> UnlockCommandProperty =
            AvaloniaProperty.Register<PinEntryControl, ICommand?>(nameof(UnlockCommand));

        public string Pin
        {
            get => GetValue(PinProperty);
            set => SetValue(PinProperty, value);
        }

        public int MaxLength
        {
            get => GetValue(MaxLengthProperty);
            set => SetValue(MaxLengthProperty, value);
        }

        public int MinLength
        {
            get => GetValue(MinLengthProperty);
            set => SetValue(MinLengthProperty, value);
        }

        public ICommand? UnlockCommand
        {
            get => GetValue(UnlockCommandProperty);
            set => SetValue(UnlockCommandProperty, value);
        }

        public PinEntryControl()
        {
            InitializeComponent();
            RefreshDots();
            UpdateUnlockEnabled();

            // Forward keyboard input to the hidden TextBox
            this.KeyDown += OnControlKeyDown;
        }

        // --- Dependency property change callbacks ---

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == PinProperty)
            {
                RefreshDots();
                UpdateUnlockEnabled();
            }
            else if (change.Property == MaxLengthProperty || change.Property == MinLengthProperty)
            {
                RefreshDots();
                UpdateUnlockEnabled();
            }
            else if (change.Property == UnlockCommandProperty)
            {
                UnlockButton.Command = UnlockCommand;
            }
        }

        // --- Dot rendering ---

        private void RefreshDots()
        {
            var items = DotDisplay.Items as Avalonia.Controls.ItemCollection;
            if (items == null) return;

            items.Clear();
            int max = MaxLength;
            int filled = Math.Min(Pin?.Length ?? 0, max);

            for (int i = 0; i < max; i++)
            {
                var dot = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    StrokeThickness = 2,
                };

                if (i < filled)
                {
                    // Filled dot
                    dot.Fill = TryFindBrush("AccentBrush", Brushes.DodgerBlue);
                    dot.Stroke = TryFindBrush("AccentBrush", Brushes.DodgerBlue);
                }
                else
                {
                    // Empty dot
                    dot.Fill = Brushes.Transparent;
                    dot.Stroke = TryFindBrush("ControlBorderBrush", Brushes.Gray);
                }

                items.Add(dot);
            }
        }

        private IBrush TryFindBrush(string key, IBrush fallback)
        {
            if (this.TryFindResource(key, this.ActualThemeVariant, out var resource) && resource is IBrush brush)
                return brush;
            return fallback;
        }

        private void UpdateUnlockEnabled()
        {
            int len = Pin?.Length ?? 0;
            UnlockButton.IsEnabled = len >= MinLength;
        }

        // --- Button handlers ---

        private void OnDigitClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string digit && digit.Length == 1)
            {
                AppendChar(digit[0]);
            }
        }

        private void OnBackspaceClick(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Pin))
                Pin = Pin[..^1];
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            Pin = string.Empty;
        }

        // --- Keyboard support ---

        private void OnControlKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Pin?.Length ?? 0) >= MinLength)
            {
                if (UnlockCommand?.CanExecute(null) == true)
                    UnlockCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                if (!string.IsNullOrEmpty(Pin))
                    Pin = Pin[..^1];
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete || e.Key == Key.Escape)
            {
                Pin = string.Empty;
                e.Handled = true;
                return;
            }

            // Accept digit keys (top row and numpad)
            char? c = KeyToDigit(e.Key);
            if (c.HasValue)
            {
                AppendChar(c.Value);
                e.Handled = true;
            }
        }

        private void AppendChar(char c)
        {
            if ((Pin?.Length ?? 0) >= MaxLength) return;
            Pin = (Pin ?? string.Empty) + c;
        }

        private static char? KeyToDigit(Key key) => key switch
        {
            Key.D0 or Key.NumPad0 => '0',
            Key.D1 or Key.NumPad1 => '1',
            Key.D2 or Key.NumPad2 => '2',
            Key.D3 or Key.NumPad3 => '3',
            Key.D4 or Key.NumPad4 => '4',
            Key.D5 or Key.NumPad5 => '5',
            Key.D6 or Key.NumPad6 => '6',
            Key.D7 or Key.NumPad7 => '7',
            Key.D8 or Key.NumPad8 => '8',
            Key.D9 or Key.NumPad9 => '9',
            _ => null,
        };

        /// <summary>Focus the control so keyboard entry works immediately.</summary>
        public void FocusPinInput()
        {
            this.Focus();
        }

        /// <summary>Reset the entered PIN.</summary>
        public void Clear()
        {
            Pin = string.Empty;
        }
    }
}
