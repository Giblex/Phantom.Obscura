using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Views.Controls
{

    public partial class PinDigitBoxes : UserControl
    {
        private const char MaskChar = '•';

        public static readonly StyledProperty<string> PinProperty =
            AvaloniaProperty.Register<PinDigitBoxes, string>(
                nameof(Pin),
                defaultValue: string.Empty,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> PinLengthProperty =
            AvaloniaProperty.Register<PinDigitBoxes, int>(
                nameof(PinLength),
                defaultValue: 4,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<bool> IsRevealedProperty =
            AvaloniaProperty.Register<PinDigitBoxes, bool>(nameof(IsRevealed), defaultValue: false);

        public static readonly StyledProperty<bool> ShowLengthSelectorProperty =
            AvaloniaProperty.Register<PinDigitBoxes, bool>(nameof(ShowLengthSelector), defaultValue: true);

        public static readonly StyledProperty<bool> AllowLettersProperty =
            AvaloniaProperty.Register<PinDigitBoxes, bool>(nameof(AllowLetters), defaultValue: false);

        private bool _suppressSync;
        private bool _initialized;

        public string Pin
        {
            get => GetValue(PinProperty);
            set => SetValue(PinProperty, value);
        }

        public int PinLength
        {
            get => GetValue(PinLengthProperty);
            set => SetValue(PinLengthProperty, value);
        }

        public bool IsRevealed
        {
            get => GetValue(IsRevealedProperty);
            set => SetValue(IsRevealedProperty, value);
        }

        public bool ShowLengthSelector
        {
            get => GetValue(ShowLengthSelectorProperty);
            set => SetValue(ShowLengthSelectorProperty, value);
        }

        public bool AllowLetters
        {
            get => GetValue(AllowLettersProperty);
            set => SetValue(AllowLettersProperty, value);
        }

        public PinDigitBoxes()
        {
            InitializeComponent();

            foreach (var length in PinLengthRange.All)
            {
                LengthSelector.Items.Add(length);
            }

            _suppressSync = true;
            LengthSelector.SelectedItem = PinLengthRange.Clamp(PinLength);
            _suppressSync = false;

            _initialized = true;
            RebuildBoxes();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (!_initialized)
                return;

            if (change.Property == PinLengthProperty)
            {
                var clamped = PinLengthRange.Clamp(PinLength);
                if (clamped != PinLength)
                {
                    PinLength = clamped;
                    return;
                }

                _suppressSync = true;
                LengthSelector.SelectedItem = clamped;
                _suppressSync = false;

                if (Pin.Length > clamped)
                    Pin = Pin[..clamped];

                RebuildBoxes();
            }
            else if (change.Property == PinProperty)
            {
                if (_suppressSync)
                    return;

                SyncBoxesFromPin();
            }
            else if (change.Property == IsRevealedProperty)
            {
                RevealGlyph.Text = IsRevealed ? "\U0001F648" : "\U0001F441";
                ApplyMasking();
            }
            else if (change.Property == ShowLengthSelectorProperty)
            {
                LengthSelector.IsVisible = ShowLengthSelector;
            }
            else if (change.Property == AllowLettersProperty)
            {
                RebuildBoxes();
            }
        }

        private void RebuildBoxes()
        {
            DigitHost.Children.Clear();

            var count = PinLengthRange.Clamp(PinLength);

            // Long PINs are unreadable as one undifferentiated run, so add a wider gap
            // after every fourth box once the PIN is long enough to need it.
            var groupDigits = count > 6;

            for (var i = 0; i < count; i++)
            {
                var isGroupEnd = groupDigits && (i + 1) % 4 == 0 && i + 1 < count;

                var box = new TextBox
                {
                    Width = 40,
                    Height = 44,
                    MaxLength = 1,
                    Margin = new Thickness(0, 0, isGroupEnd ? 16 : 6, 6),
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontSize = 18,
                    Tag = i,
                    PasswordChar = IsRevealed ? '\0' : MaskChar
                };

                AutomationProperties.SetName(box, $"PIN digit {i + 1} of {count}");

                box.TextChanged += OnDigitTextChanged;
                box.KeyDown += OnDigitKeyDown;
                box.GotFocus += (_, _) => box.SelectAll();
                box.AddHandler(TextBox.PastingFromClipboardEvent, OnDigitPaste, RoutingStrategies.Tunnel);

                DigitHost.Children.Add(box);
            }

            SyncBoxesFromPin();
            UpdateCountLabel();
        }

        private void SyncBoxesFromPin()
        {
            var pin = Pin ?? string.Empty;
            var boxes = DigitHost.Children.OfType<TextBox>().ToList();

            _suppressSync = true;
            for (var i = 0; i < boxes.Count; i++)
            {
                boxes[i].Text = i < pin.Length ? pin[i].ToString() : string.Empty;
            }
            _suppressSync = false;

            UpdateCountLabel();
        }

        private void ApplyMasking()
        {
            foreach (var box in DigitHost.Children.OfType<TextBox>())
            {
                box.PasswordChar = IsRevealed ? '\0' : MaskChar;
            }
        }

        private void OnDigitTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressSync || sender is not TextBox box || box.Tag is not int index)
                return;

            var text = box.Text ?? string.Empty;

            // A paste lands as a multi-character change in a single box. Spread it across
            // this box and the ones after it rather than throwing all but the first char away.
            if (text.Length > 1)
            {
                SpreadFrom(index, text);
                return;
            }

            if (text.Length > 0 && !IsAccepted(text[0]))
            {
                _suppressSync = true;
                box.Text = string.Empty;
                _suppressSync = false;
                return;
            }

            RebuildPinFromBoxes();

            if (text.Length == 1)
                FocusBox(index + 1);
        }

        private void OnDigitKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox box || box.Tag is not int index)
                return;

            switch (e.Key)
            {
                case Key.Back when string.IsNullOrEmpty(box.Text):
                    FocusBox(index - 1);
                    e.Handled = true;
                    break;

                case Key.Left:
                    FocusBox(index - 1);
                    e.Handled = true;
                    break;

                case Key.Right:
                    FocusBox(index + 1);
                    e.Handled = true;
                    break;

                case Key.Delete:
                    _suppressSync = true;
                    box.Text = string.Empty;
                    _suppressSync = false;
                    RebuildPinFromBoxes();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Each box holds a single character, so the default paste would keep only the
        /// first one. Intercept it and lay the whole pasted PIN out across the boxes.
        /// </summary>
        private async void OnDigitPaste(object? sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box || box.Tag is not int index)
                return;

            e.Handled = true;

            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                    return;

                var text = await clipboard.TryGetTextAsync();
                if (string.IsNullOrEmpty(text))
                    return;

                SpreadFrom(index, text);
            }
            catch
            {

            }
        }

        /// <summary>
        /// Distributes pasted or typed-over text across the boxes starting at
        /// <paramref name="startIndex"/>, keeping only accepted characters.
        /// </summary>
        private void SpreadFrom(int startIndex, string text)
        {
            var boxes = DigitHost.Children.OfType<TextBox>().ToList();
            var accepted = text.Where(IsAccepted).ToList();

            _suppressSync = true;

            for (var i = startIndex; i < boxes.Count; i++)
            {
                var offset = i - startIndex;
                boxes[i].Text = offset < accepted.Count ? accepted[offset].ToString() : string.Empty;
            }

            _suppressSync = false;

            RebuildPinFromBoxes();

            var landing = Math.Min(startIndex + accepted.Count, boxes.Count - 1);
            FocusBox(landing);
        }

        private void RebuildPinFromBoxes()
        {
            var builder = new StringBuilder();
            foreach (var box in DigitHost.Children.OfType<TextBox>())
            {
                var text = box.Text;
                if (!string.IsNullOrEmpty(text))
                    builder.Append(text[0]);
            }

            _suppressSync = true;
            Pin = builder.ToString();
            _suppressSync = false;

            UpdateCountLabel();
        }

        private void FocusBox(int index)
        {
            var boxes = DigitHost.Children.OfType<TextBox>().ToList();
            if (index < 0 || index >= boxes.Count)
                return;

            boxes[index].Focus();
            boxes[index].SelectAll();
        }

        private void UpdateCountLabel()
        {
            var entered = (Pin ?? string.Empty).Length;
            var total = PinLengthRange.Clamp(PinLength);
            CountLabel.Text = $"{entered}/{total}";
        }

        private bool IsAccepted(char c)
            => AllowLetters ? !char.IsControl(c) && !char.IsWhiteSpace(c) : char.IsDigit(c);

        private void OnLengthSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSync || !_initialized)
                return;

            if (LengthSelector.SelectedItem is int selected)
                PinLength = PinLengthRange.Clamp(selected);
        }

        private void OnRevealClick(object? sender, RoutedEventArgs e)
        {
            IsRevealed = !IsRevealed;
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            _suppressSync = true;
            Pin = string.Empty;
            _suppressSync = false;

            SyncBoxesFromPin();
            FocusBox(0);
        }

        public void FocusFirstBox() => FocusBox(0);
    }
}
