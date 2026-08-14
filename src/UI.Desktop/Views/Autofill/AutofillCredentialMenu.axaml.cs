using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Credential suggestion menu shown when the AutoFill badge is clicked.
    ///
    /// Each row offers a primary action plus a hover strip of narrower ones, because
    /// "fill both fields" is only one of the things people actually want — copying just
    /// the username, just the password, or a TOTP code are all common. Passkey rows
    /// replace the whole set with Authenticate, since username and password are
    /// meaningless for them.
    ///
    /// Keyboard: ↑↓ navigate, Enter fill, 1-3 fill by rank, U/P/T copy, Esc dismiss.
    /// </summary>
    public partial class AutofillCredentialMenu : ThemeAwareWindow
    {
        /// <summary>What the caller should do with a chosen row.</summary>
        public sealed record MenuAction(CredentialMatch Match, AutoFillField Field, bool Copy, bool Authenticate);

        private TextBlock? _domainHeader;
        private TextBlock? _countBadge;
        private StackPanel? _rowsPanel;
        private Border? _shell;
        private TextBlock? _toast;

        private readonly List<CredentialMatch> _matches;
        private readonly Action<MenuAction> _onAction;
        private readonly Func<string, Task<TotpSnapshot?>>? _totpProvider;
        private readonly List<RowVisual> _rows = new();

        private DispatcherTimer? _totpTimer;
        private int _selectedIndex;
        private bool _closed;

        private sealed record RowVisual(
            CredentialMatch Match,
            Border Container,
            Border AccentBar,
            TextBlock Title,
            TextBlock Sub,
            StackPanel Actions,
            TextBlock? TotpCode,
            Arc? TotpRing);

        // One palette for the whole menu. Rows used to be graded teal -> blue by
        // rank, which meant borders shifted colour down the list and each row read as
        // a different component. Rank is now conveyed by order and the accent bar
        // alone, and every border shares a single edge colour.
        private static readonly Color Edge = Color.Parse("#2C3B52");
        private static readonly Color Accent = Color.Parse("#3E8C86");
        private static readonly Color Muted = Color.Parse("#8497AC");

        public AutofillCredentialMenu(
            string domain,
            IReadOnlyList<CredentialMatch> matches,
            Action<MenuAction> onAction,
            Func<string, Task<TotpSnapshot?>>? totpProvider = null)
        {
            AvaloniaXamlLoader.Load(this);
            _domainHeader = this.FindControl<TextBlock>("DomainHeader");
            _countBadge = this.FindControl<TextBlock>("CountBadge");
            _rowsPanel = this.FindControl<StackPanel>("RowsPanel");
            _shell = this.FindControl<Border>("Shell");
            _toast = this.FindControl<TextBlock>("Toast");

            _matches = matches.Take(3).ToList();
            _onAction = onAction;
            _totpProvider = totpProvider;

            if (_domainHeader != null)
                _domainHeader.Text = string.IsNullOrWhiteSpace(domain) ? "This site" : domain;
            if (_countBadge != null)
                _countBadge.Text = _matches.Count == 1 ? "1 match" : $"{_matches.Count} matches";

            // Window-level focus loss. Deliberately not an OnLostFocus override:
            // LostFocus is routed, so it also fires when focus moves between this
            // window's own children, which closed the menu mid-interaction.
            Deactivated += (_, _) => CloseOnce();
            Opened += (_, _) => { PlayEntrance(); StartTotpTicker(); };

            BuildRows();
        }

        // ── Entrance ──────────────────────────────────────────────────────────

        private void PlayEntrance()
        {
            if (_shell == null) return;
            _shell.Opacity = 0;
            _shell.RenderTransform = TransformOperations.Parse("translateY(-8px) scale(0.96)");

            // Next frame, so the start state is committed before the target is set —
            // otherwise both land in one layout pass and the transition never runs.
            Dispatcher.UIThread.Post(() =>
            {
                if (_shell == null) return;
                _shell.Opacity = 1;
                _shell.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
            }, DispatcherPriority.Background);
        }

        // ── Rows ──────────────────────────────────────────────────────────────

        private void BuildRows()
        {
            if (_rowsPanel == null) return;
            _rowsPanel.Children.Clear();
            _rows.Clear();

            if (_matches.Count == 0)
            {
                _rowsPanel.Children.Add(EmptyState());
                return;
            }

            for (int i = 0; i < _matches.Count; i++)
                _rowsPanel.Children.Add(BuildRow(_matches[i], i));

            RefreshHighlight();
        }

        private static Border EmptyState() => new()
        {
            Padding = new Thickness(16, 18),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "No saved credentials for this site",
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(Color.Parse("#C2CFDC")),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Open your vault to add one",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#6E8298")),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        private Border BuildRow(CredentialMatch match, int idx)
        {

            var accent = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                Background = new SolidColorBrush(Accent),
                Opacity = 0,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var avatar = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.Parse("#FF1B2740")),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Edge),
                VerticalAlignment = VerticalAlignment.Center,
                Child = match.IsPasskey
                    ? PasskeyGlyph(Muted)
                    : new TextBlock
                    {
                        Text = InitialOf(match.DisplayName, match.Username),
                        FontSize = 12.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Muted),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
            };

            var title = new TextBlock
            {
                Text = match.DisplayName,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#E8F0F6")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var sub = new TextBlock
            {
                Text = match.IsPasskey ? "Passkey — no password needed" : MaskUsername(match.Username),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.Parse("#8497AC")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var info = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { title, sub }
            };

            // Live TOTP readout with a countdown ring, so the code is visibly ageing
            // rather than a static number the user cannot tell is about to roll over.
            TextBlock? totpCode = null;
            Arc? totpRing = null;
            if (match.HasTotp && !match.IsPasskey)
            {
                totpCode = new TextBlock
                {
                    Text = "······",
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                    Foreground = new SolidColorBrush(Accent),
                    VerticalAlignment = VerticalAlignment.Center
                };
                totpRing = new Arc
                {
                    Width = 12,
                    Height = 12,
                    StrokeThickness = 2,
                    Stroke = new SolidColorBrush(Accent),
                    StartAngle = -90,
                    SweepAngle = 360,
                    VerticalAlignment = VerticalAlignment.Center
                };
                info.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 3, 0, 0),
                    Children = { totpRing, totpCode }
                });
            }

            var actions = BuildActionStrip(match);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto,12,*,10,Auto"),
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(accent, 0);
            Grid.SetColumn(avatar, 2);
            Grid.SetColumn(info, 4);
            Grid.SetColumn(actions, 6);
            grid.Children.Add(accent);
            grid.Children.Add(avatar);
            grid.Children.Add(info);
            grid.Children.Add(actions);

            var container = new Border
            {
                Padding = new Thickness(0, 9, 0, 9),
                Background = new SolidColorBrush(Colors.Transparent),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = grid
            };

            container.PointerPressed += (_, e) =>
            {
                // Clicks inside the action strip are handled by their own buttons.
                if (e.Source is Control c && IsWithin(c, actions)) return;
                Primary(idx);
            };
            container.PointerEntered += (_, _) => { _selectedIndex = idx; RefreshHighlight(); };

            _rows.Add(new RowVisual(match, container, accent, title, sub, actions, totpCode, totpRing));
            return container;
        }

        /// <summary>
        /// Narrow secondary actions, revealed on hover so the resting row stays calm.
        /// Passkey rows get a single Authenticate action instead: copying a username or
        /// password from a passkey is meaningless.
        /// </summary>
        private StackPanel BuildActionStrip(CredentialMatch match)
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };

            if (match.IsPasskey)
            {
                strip.Children.Add(ActionChip("Authenticate", "Sign in with this passkey", wide: true,
                    () => Emit(new MenuAction(match, AutoFillField.Both, Copy: false, Authenticate: true))));
                return strip;
            }

            strip.Children.Add(ActionChip("U", "Copy username", wide: false,
                () => Emit(new MenuAction(match, AutoFillField.UsernameOnly, Copy: true, Authenticate: false), "Username copied")));
            strip.Children.Add(ActionChip("P", "Copy password", wide: false,
                () => Emit(new MenuAction(match, AutoFillField.PasswordOnly, Copy: true, Authenticate: false), "Password copied")));
            if (match.HasTotp)
            {
                strip.Children.Add(ActionChip("T", "Copy one-time code", wide: false,
                    () => Emit(new MenuAction(match, AutoFillField.TotpCode, Copy: true, Authenticate: false), "Code copied")));
            }

            return strip;
        }

        private Button ActionChip(string label, string tip, bool wide, Action onClick)
        {
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#B4C4D6")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                // Every chip is the same height and shares one border colour, so the
                // strip reads as a set rather than three unrelated controls.
                Width = wide ? 94 : 26,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#18FFFFFF")),
                BorderBrush = new SolidColorBrush(Edge),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(btn, tip);
            AutomationProperties.SetName(btn, tip);
            btn.Click += (_, e) => { e.Handled = true; onClick(); };
            return btn;
        }

        private static Control PasskeyGlyph(Color c) => new Avalonia.Controls.Shapes.Path
        {
            // Simple key outline.
            Data = Geometry.Parse("M14 2a5 5 0 1 0-4.9 6L8 9.1V11H6v2H4v2H1.5v-2.6l6.4-6.4A5 5 0 0 1 14 2zm-3.2 2.2a1.3 1.3 0 1 0 1.9 1.9 1.3 1.3 0 0 0-1.9-1.9z"),
            Fill = new SolidColorBrush(c),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static bool IsWithin(Control node, Control ancestor)
        {
            for (Visual? v = node; v != null; v = v.GetVisualParent())
                if (ReferenceEquals(v, ancestor)) return true;
            return false;
        }

        private void RefreshHighlight()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i == _selectedIndex;
                var r = _rows[i];
                r.Container.Background = new SolidColorBrush(on ? Color.Parse("#1EFFFFFF") : Colors.Transparent);
                r.AccentBar.Opacity = on ? 1 : 0;
                r.Actions.Opacity = on ? 1 : 0;
                r.Actions.IsHitTestVisible = on;
                r.Title.Foreground = new SolidColorBrush(on ? Color.Parse("#FFFFFF") : Color.Parse("#D2DDE8"));
                r.Sub.Foreground = new SolidColorBrush(on ? Color.Parse("#9DB2C6") : Color.Parse("#75889C"));
            }
        }

        // ── Live TOTP ─────────────────────────────────────────────────────────

        private void StartTotpTicker()
        {
            if (_totpProvider == null || _rows.All(r => r.TotpCode == null)) return;

            _totpTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _totpTimer.Tick += async (_, _) => await RefreshTotpAsync();
            _totpTimer.Start();
            _ = RefreshTotpAsync();
        }

        private async Task RefreshTotpAsync()
        {
            if (_totpProvider == null || _closed) return;
            foreach (var row in _rows.Where(r => r.TotpCode != null))
            {
                try
                {
                    var snap = await _totpProvider(row.Match.CredentialId);
                    if (snap == null || _closed) continue;
                    row.TotpCode!.Text = FormatCode(snap.Code);
                    if (row.TotpRing != null)
                        row.TotpRing.SweepAngle = 360 * snap.Fraction;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutofillMenu] TOTP refresh failed: {ex.Message}");
                }
            }
        }

        private static string FormatCode(string code)
            => code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;

        // ── Interaction ───────────────────────────────────────────────────────

        /// <summary>Primary action for a row: fill everything, or authenticate a passkey.</summary>
        private void Primary(int idx)
        {
            if (idx < 0 || idx >= _matches.Count) return;
            var m = _matches[idx];
            Emit(new MenuAction(m, AutoFillField.Both, Copy: false, Authenticate: m.IsPasskey));
        }

        /// <summary>
        /// Copy actions keep the menu open — people often copy a username and then a
        /// password. Fill and authenticate close it, since focus has to move away.
        /// </summary>
        private void Emit(MenuAction action, string? toast = null)
        {
            if (action.Copy)
            {
                _onAction(action);
                ShowToast(toast ?? "Copied");
                return;
            }

            CloseOnce();
            _onAction(action);
        }

        private void ShowToast(string message)
        {
            if (_toast == null) return;
            _toast.Text = message;
            _toast.Opacity = 1;
            DispatcherTimer.RunOnce(() =>
            {
                if (_toast != null) _toast.Opacity = 0;
            }, TimeSpan.FromMilliseconds(1400));
        }

        /// <summary>
        /// Close guarded against re-entry — Emit, Escape, Deactivated and the owner's
        /// cleanup can all reach here, and closing an already-closed window throws.
        /// </summary>
        private void CloseOnce()
        {
            if (_closed) return;
            _closed = true;
            _totpTimer?.Stop();
            _totpTimer = null;
            try { Close(); } catch (InvalidOperationException) { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _totpTimer?.Stop();
            _totpTimer = null;
            base.OnClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var sel = _selectedIndex >= 0 && _selectedIndex < _matches.Count ? _matches[_selectedIndex] : null;

            switch (e.Key)
            {
                case Key.Escape: CloseOnce(); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Enter: Primary(_selectedIndex); e.Handled = true; break;
                case Key.D1: case Key.NumPad1: Primary(0); e.Handled = true; break;
                case Key.D2: case Key.NumPad2: Primary(1); e.Handled = true; break;
                case Key.D3: case Key.NumPad3: Primary(2); e.Handled = true; break;

                case Key.U when sel is { IsPasskey: false }:
                    Emit(new MenuAction(sel, AutoFillField.UsernameOnly, true, false), "Username copied");
                    e.Handled = true; break;
                case Key.P when sel is { IsPasskey: false }:
                    Emit(new MenuAction(sel, AutoFillField.PasswordOnly, true, false), "Password copied");
                    e.Handled = true; break;
                case Key.T when sel is { HasTotp: true }:
                    Emit(new MenuAction(sel, AutoFillField.TotpCode, true, false), "Code copied");
                    e.Handled = true; break;
            }
        }

        /// <summary>Wraps around the ends rather than clamping — fewer dead keypresses.</summary>
        private void Move(int delta)
        {
            if (_matches.Count == 0) return;
            _selectedIndex = (_selectedIndex + delta + _matches.Count) % _matches.Count;
            RefreshHighlight();
        }

        /// <summary>Position the menu below the badge, kept inside the screen.</summary>
        public void PositionNearBadge(PixelPoint badgePos, double badgeH)
        {
            var screen = Screens.ScreenFromPoint(badgePos) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1.0;
            var bounds = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

            // Width is a fixed literal in the AXAML; Height is SizeToContent, so it can
            // still be NaN before the first measure. Fall back to a sane estimate rather
            // than letting NaN propagate into the position and throw it off-screen.
            double hLogical = double.IsNaN(Height) || Height <= 0 ? 250 : Height;
            int wPx = (int)Math.Round(Width * scale);
            int hPx = (int)Math.Round(hLogical * scale);

            int x = badgePos.X - 8;
            int y = badgePos.Y + (int)Math.Round(badgeH * scale) + 4;

            if (y + hPx > bounds.Y + bounds.Height - 8) y = badgePos.Y - hPx - 4;
            if (x + wPx > bounds.X + bounds.Width - 8) x = bounds.X + bounds.Width - wPx - 8;
            if (x < bounds.X + 8) x = bounds.X + 8;
            if (y < bounds.Y + 8) y = bounds.Y + 8;

            Position = new PixelPoint(x, y);
        }

        // ── Formatting ────────────────────────────────────────────────────────

        private static string InitialOf(string display, string username)
        {
            foreach (var s in new[] { display, username })
                if (!string.IsNullOrWhiteSpace(s))
                    foreach (var ch in s)
                        if (char.IsLetterOrDigit(ch))
                            return char.ToUpperInvariant(ch).ToString();
            return "?";
        }

        private static string MaskUsername(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            int at = u.IndexOf('@');
            if (at > 1) return u[..Math.Min(2, at)] + "•••" + u[at..];
            return u.Length > 3 ? u[..2] + "•••" : u;
        }
    }
}
