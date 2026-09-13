using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Shows a TOTP code as a row of digit cells that roll like an odometer when the code
/// changes: each changed digit slides up and out while its replacement slides in from below,
/// staggered left to right. Unchanged digits stay put. Font, size, weight and colour are
/// inherited from this control. Honours Reduce Motion by swapping instantly.
/// </summary>
public sealed class RollingCodeText : UserControl
{
    private static readonly TimeSpan RollDuration = TimeSpan.FromMilliseconds(340);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(45);

    // A rebind within this window (e.g. a recycled list tile picking up another entry) is a
    // new code to show, not a rollover, so it snaps instead of rolling.
    private static readonly TimeSpan RebindWindow = TimeSpan.FromMilliseconds(500);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RollingCodeText, string?>(nameof(Text));

    public static readonly StyledProperty<double> CharacterSpacingProperty =
        AvaloniaProperty.Register<RollingCodeText, double>(nameof(CharacterSpacing), 1.0);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gap between digits, in DIPs (stands in for TextBlock.LetterSpacing).</summary>
    public double CharacterSpacing
    {
        get => GetValue(CharacterSpacingProperty);
        set => SetValue(CharacterSpacingProperty, value);
    }

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<DigitCell> _cells = new();
    private bool _hasShown;
    private DateTime _dataContextChangedUtc = DateTime.MinValue;

    public RollingCodeText()
    {
        _row.Spacing = CharacterSpacing;
        Content = _row;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
            Apply(Text ?? string.Empty);
        else if (change.Property == CharacterSpacingProperty)
            _row.Spacing = CharacterSpacing;
        else if (change.Property == DataContextProperty)
            _dataContextChangedUtc = DateTime.UtcNow;
    }

    private void Apply(string text)
    {
        bool animate = _hasShown
                       && DateTime.UtcNow - _dataContextChangedUtc > RebindWindow
                       && !AccessibilityService.Instance.ReduceMotion;
        _hasShown = true;

        if (text.Length != _cells.Count)
        {
            _cells.Clear();
            _row.Children.Clear();
            foreach (var _ in text)
            {
                var cell = new DigitCell();
                _cells.Add(cell);
                _row.Children.Add(cell);
            }
        }

        int changed = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var cell = _cells[i];
            if (cell.Char == text[i]) continue;

            if (animate)
                cell.RollTo(text[i], TimeSpan.FromTicks(Stagger.Ticks * changed++), RollDuration);
            else
                cell.Snap(text[i]);
        }
    }

    /// <summary>One digit position: two stacked TextBlocks that trade places when rolling.</summary>
    private sealed class DigitCell : Panel
    {
        private readonly TextBlock _a = new();
        private readonly TextBlock _b = new();
        private readonly TranslateTransform _ta = new();
        private readonly TranslateTransform _tb = new();
        private bool _aIsCurrent = true;

        // Handle from DispatcherTimer.RunOnce; disposing it cancels a roll not yet started.
        private IDisposable? _pending;

        public char Char { get; private set; } = '\0';

        public DigitCell()
        {
            ClipToBounds = true;
            _a.RenderTransform = _ta;
            _b.RenderTransform = _tb;
            _b.Opacity = 0;
            Children.Add(_a);
            Children.Add(_b);
        }

        private TextBlock Current => _aIsCurrent ? _a : _b;
        private TextBlock Next => _aIsCurrent ? _b : _a;
        private TranslateTransform CurrentT => _aIsCurrent ? _ta : _tb;
        private TranslateTransform NextT => _aIsCurrent ? _tb : _ta;

        public void Snap(char c)
        {
            CancelPending();
            Char = c;
            ClearTransitions();
            Current.Text = c.ToString();
            CurrentT.Y = 0;
            Current.Opacity = 1;
            Next.Opacity = 0;
            NextT.Y = 0;
        }

        public void RollTo(char c, TimeSpan delay, TimeSpan duration)
        {
            CancelPending();
            Char = c;

            double distance = Bounds.Height > 0 ? Bounds.Height : Current.FontSize * 1.3;

            // Park the incoming digit just below the cell, without animating the move.
            ClearTransitions();
            Next.Text = c.ToString();
            NextT.Y = distance;
            Next.Opacity = 0;

            void Start()
            {
                var outgoing = Current;
                var outgoingT = CurrentT;
                var incoming = Next;
                var incomingT = NextT;

                SetTransitions(duration);
                outgoingT.Y = -distance;
                outgoing.Opacity = 0;
                incomingT.Y = 0;
                incoming.Opacity = 1;
                _aIsCurrent = !_aIsCurrent;
            }

            if (delay <= TimeSpan.Zero)
                Start();
            else
                _pending = DispatcherTimer.RunOnce(Start, delay);
        }

        private void CancelPending()
        {
            _pending?.Dispose();
            _pending = null;
        }

        private void ClearTransitions()
        {
            _ta.Transitions = null;
            _tb.Transitions = null;
            _a.Transitions = null;
            _b.Transitions = null;
        }

        private void SetTransitions(TimeSpan duration)
        {
            var easing = new CubicEaseOut();
            _ta.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = duration, Easing = easing } };
            _tb.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = duration, Easing = easing } };
            _a.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = easing } };
            _b.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = easing } };
        }
    }
}
