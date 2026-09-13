using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Read-only field for the entry detail views.
///
/// Clicking the value copies it, through the vault's clipboard path so the clipboard guard and
/// auto-clear apply; it is never an editable box, as editing happens only through the Edit
/// button. Secret fields show <see cref="MaskedValue"/> (or bullets) with an eye button that
/// reveals the real value in place. The reveal resets whenever the value changes, so selecting
/// another entry never inherits a revealed field. The copy is always the real value.
/// </summary>
public partial class CopyableField : UserControl
{
    private const string ShowIcon = "Assets/SVG/Current/Visible eye.svg";
    private const string HideIcon = "Assets/SVG/Current/Hidden eye.svg";

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<CopyableField, string?>(nameof(Value));

    public static readonly StyledProperty<string?> MaskedValueProperty =
        AvaloniaProperty.Register<CopyableField, string?>(nameof(MaskedValue));

    public static readonly StyledProperty<bool> IsSecretProperty =
        AvaloniaProperty.Register<CopyableField, bool>(nameof(IsSecret));

    public static readonly StyledProperty<bool> IsMultilineProperty =
        AvaloniaProperty.Register<CopyableField, bool>(nameof(IsMultiline));

    public static readonly StyledProperty<string> CopyLabelProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(CopyLabel), "Value");

    public static readonly StyledProperty<bool> DigitBoxesProperty =
        AvaloniaProperty.Register<CopyableField, bool>(nameof(DigitBoxes));

    /// <summary>Show the value as one small box per character (PINs) instead of a text box.</summary>
    public bool DigitBoxes
    {
        get => GetValue(DigitBoxesProperty);
        set => SetValue(DigitBoxesProperty, value);
    }

    /// <summary>A revealed secret hides itself again after this long.</summary>
    private static readonly System.TimeSpan RevealTimeout = System.TimeSpan.FromSeconds(60);

    /// <summary>The real value: what is copied, and what is shown once revealed.</summary>
    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// What a secret field shows while hidden, e.g. a card number with only the last four
    /// digits. When empty, the real value is shown as bullets instead.
    /// </summary>
    public string? MaskedValue
    {
        get => GetValue(MaskedValueProperty);
        set => SetValue(MaskedValueProperty, value);
    }

    public bool IsSecret
    {
        get => GetValue(IsSecretProperty);
        set => SetValue(IsSecretProperty, value);
    }

    public bool IsMultiline
    {
        get => GetValue(IsMultilineProperty);
        set => SetValue(IsMultilineProperty, value);
    }

    /// <summary>Field name used in the tooltip and the "copied" status, e.g. "Card number".</summary>
    public string CopyLabel
    {
        get => GetValue(CopyLabelProperty);
        set => SetValue(CopyLabelProperty, value);
    }

    private readonly TextBox? _box;
    private readonly WrapPanel? _digits;
    private readonly Button? _reveal;
    private readonly SvgIcon? _revealIcon;
    private bool _isRevealed;
    private int _revealGeneration;

    public CopyableField()
    {
        AvaloniaXamlLoader.Load(this);
        _box = this.FindControl<TextBox>("PART_Box");
        _digits = this.FindControl<WrapPanel>("PART_Digits");
        _reveal = this.FindControl<Button>("PART_Reveal");
        _revealIcon = this.FindControl<SvgIcon>("PART_RevealIcon");
        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            SetRevealed(false);
        }
        else if (change.Property == MaskedValueProperty
                 || change.Property == IsSecretProperty
                 || change.Property == IsMultilineProperty
                 || change.Property == CopyLabelProperty
                 || change.Property == DigitBoxesProperty)
        {
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Reveals or hides the value. A reveal hides itself again after <see cref="RevealTimeout"/>;
    /// the generation counter makes a stale timer (from an earlier reveal, a manual hide or a
    /// different entry) do nothing.
    /// </summary>
    private void SetRevealed(bool revealed)
    {
        _isRevealed = revealed;
        var generation = ++_revealGeneration;

        if (revealed)
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (generation == _revealGeneration && _isRevealed)
                    SetRevealed(false);
            }, RevealTimeout);
        }

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_box == null) return;

        bool hidden = IsSecret && !_isRevealed;

        // Digit boxes: one box per character of the real value, bullets while hidden.
        bool useDigits = DigitBoxes && !IsMultiline && !string.IsNullOrEmpty(Value);
        _box.IsVisible = !useDigits;
        if (_digits != null)
        {
            _digits.IsVisible = useDigits;
            if (useDigits)
            {
                BuildDigitCells(hidden);
                ToolTip.SetTip(_digits, $"Click to copy {CopyLabel.ToLowerInvariant()}");
                AutomationProperties.SetName(_digits, CopyLabel);
            }
        }
        if (hidden && !string.IsNullOrEmpty(MaskedValue))
        {
            _box.PasswordChar = char.MinValue;
            _box.Text = MaskedValue;
        }
        else
        {
            // NUL disables masking, which is how Avalonia's TextBox shows clear text.
            _box.PasswordChar = hidden ? '•' : char.MinValue;
            _box.Text = Value ?? string.Empty;
        }

        _box.TextWrapping = IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _box.AcceptsReturn = IsMultiline;
        _box.Height = IsMultiline ? double.NaN : 36;
        ToolTip.SetTip(_box, $"Click to copy {CopyLabel.ToLowerInvariant()}");
        AutomationProperties.SetName(_box, CopyLabel);

        if (_reveal != null)
        {
            _reveal.IsVisible = IsSecret;
            ToolTip.SetTip(_reveal, _isRevealed ? $"Hide {CopyLabel.ToLowerInvariant()}" : $"Show {CopyLabel.ToLowerInvariant()}");
        }

        if (_revealIcon != null)
            _revealIcon.IconName = _isRevealed ? HideIcon : ShowIcon;
    }

    private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c is '/' or '-' or '.';

    /// <summary>
    /// One cell per character of the real value. Separators (spaces in a card number, the slash
    /// in an expiry) become a wider gap rather than a cell; a long unseparated value is grouped
    /// in fours. While hidden, cells show bullets except where the masked form deliberately
    /// shows a character (a card's last four digits), aligned from the end.
    /// </summary>
    private void BuildDigitCells(bool hidden)
    {
        if (_digits == null) return;

        var raw = Value ?? string.Empty;
        var chars = new System.Collections.Generic.List<char>();
        var groupEnds = new System.Collections.Generic.HashSet<int>();
        foreach (var c in raw)
        {
            if (IsSeparator(c))
            {
                if (chars.Count > 0) groupEnds.Add(chars.Count - 1);
                continue;
            }
            chars.Add(c);
        }
        if (groupEnds.Count == 0 && chars.Count > 6)
        {
            for (var i = 3; i < chars.Count - 1; i += 4)
                groupEnds.Add(i);
        }

        var masked = hidden
            ? new string(System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Where(MaskedValue ?? string.Empty, c => !IsSeparator(c))))
            : string.Empty;
        var offset = chars.Count - masked.Length;

        _digits.Children.Clear();
        for (var i = 0; i < chars.Count; i++)
        {
            var shown = chars[i];
            if (hidden)
            {
                var m = i - offset;
                shown = m >= 0 && m < masked.Length && masked[m] != '•' && masked[m] != '*' ? masked[m] : '•';
            }

            var cell = new Border { Child = new TextBlock { Text = shown.ToString() } };
            cell.Classes.Add("digit-cell");
            if (groupEnds.Contains(i) && i < chars.Count - 1)
                cell.Classes.Add("group-end");
            _digits.Children.Add(cell);
        }
    }

    private async void OnBoxTapped(object? sender, TappedEventArgs e)
    {
        // The vault (single card) or an account tile (shared card): each copies its own entry.
        if (DataContext is not IDetailHost host) return;

        // The clicked surface: the text box, or the row of digit boxes.
        Control? target = sender as Control ?? _box;
        if (target == null) return;

        // Bounce straight away so the click feels answered; confirm only once it copied.
        CopyFeedback.Bounce(target);
        if (await host.CopyFieldValueAsync(Value, CopyLabel))
            CopyFeedback.ShowCopied(target);
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        SetRevealed(!_isRevealed);
    }
}
