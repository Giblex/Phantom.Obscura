using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using Avalonia.VisualTree;
using PhantomVault.Core.Models;
using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Views
{
    public partial class ImportWindow : ThemeAwareWindow
    {
        public event EventHandler<List<Credential>>? ImportCompleted;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, System.Threading.CancellationTokenSource> _animCts = new();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, System.Threading.CancellationTokenSource> _hoverCts = new();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, (long ticks, long pointerId)> _lastPointerEnter = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, (long ticks, long pointerId)> _lastPointerPress = new();

        public ImportWindow()
        {
            InitializeComponent();

        }

        public ImportWindow(List<Credential>? existingCredentials = null) : this()
        {
            var viewModel = new ImportViewModel(existingCredentials);
            DataContext = viewModel;
            viewModel.SetOwner(this);
            viewModel.ImportCompleted += OnImportCompleted;
            viewModel.CloseRequested += (s, e) => Close();
        }

        public ImportWindow(ImportViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.SetOwner(this);
            viewModel.ImportCompleted += OnImportCompleted;
            viewModel.CloseRequested += (s, e) => Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnImportCompleted(object? sender, List<Credential> credentials)
        {
            ImportCompleted?.Invoke(this, credentials);
        }

        private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private async void OnPointerEnter(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            var tile = FindTileButton(sender as Control ?? e.Source as Control);
            if (tile != null)
            {

                var card = tile.FindControl<Border>("CardInner") as Control ?? tile.FindControl<Border>("CardBorder") as Control ?? tile;
                var pId = e.Pointer?.Id ?? -1;
                var pType = e.Pointer?.Type.ToString() ?? "Unknown";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerEnter -> Tile={GetControlId(tile)} Card={GetControlId(card)} Pointer={pType}/{pId} Sender==Source={(sender == e.Source)} Scale={GetCurrentScale(card)} Opacity={card.Opacity}");
#endif

                var now = DateTime.UtcNow.Ticks;
                if (_lastPointerEnter.TryGetValue(card, out var last) && last.pointerId == pId && (now - last.ticks) < TimeSpan.FromMilliseconds(120).Ticks)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerEnter ignored duplicate -> Card={GetControlId(card)} Pointer={pType}/{pId}");
#endif
                    return;
                }
                _lastPointerEnter[card] = (now, pId);

                if (_hoverCts.TryGetValue(card, out var existing))
                {
                    try { existing.Cancel(); } catch { }
                }

                var cts = new System.Threading.CancellationTokenSource();
                _hoverCts[card] = cts;
                try
                {

                    await System.Threading.Tasks.Task.Delay(60, cts.Token);
                    if (cts.Token.IsCancellationRequested) return;

                    const double hoverScale = 1.03;

                    if (Math.Abs(GetCurrentScale(card) - hoverScale) > 0.01)
                    {
                        _ = AnimateTileVisualsAsync(tile, card, hoverScale, 120);
                    }
                }
                catch (OperationCanceledException)
                {

                }
                finally
                {
                    _hoverCts.TryRemove(card, out _);
                }
            }
        }

        private async void OnPointerLeave(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            var tile = FindTileButton(sender as Control ?? e.Source as Control);
            if (tile != null)
            {
                var card = tile.FindControl<Border>("CardInner") as Control ?? tile.FindControl<Border>("CardBorder") as Control ?? tile;
                var pIdL = e.Pointer?.Id ?? -1;
                var pTypeL = e.Pointer?.Type.ToString() ?? "Unknown";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerLeave -> Tile={GetControlId(tile)} Card={GetControlId(card)} Pointer={pTypeL}/{pIdL} Sender==Source={(sender == e.Source)} Scale={GetCurrentScale(card)} Opacity={card.Opacity}");
#endif

                if (_hoverCts.TryGetValue(card, out var pending))
                {
                    try { pending.Cancel(); } catch { }
                    _hoverCts.TryRemove(card, out _);
                }

                var cts = new System.Threading.CancellationTokenSource();
                _hoverCts[card] = cts;
                try
                {
                    await System.Threading.Tasks.Task.Delay(80, cts.Token);
                    if (cts.Token.IsCancellationRequested) return;

                    const double neutral = 1.0;
                    if (Math.Abs(GetCurrentScale(card) - neutral) > 0.01)
                    {
                        _ = AnimateTileVisualsAsync(tile, card, neutral, 120);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    _hoverCts.TryRemove(card, out _);
                }
            }
        }

        private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var tile = FindTileButton(sender as Control ?? e.Source as Control);
            if (tile != null)
            {
                var card = tile.FindControl<Border>("CardInner") as Control ?? tile.FindControl<Border>("CardBorder") as Control ?? tile;
                var pIdP = e.Pointer?.Id ?? -1;
                var pTypeP = e.Pointer?.Type.ToString() ?? "Unknown";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerPressed -> Tile={GetControlId(tile)} Card={GetControlId(card)} Pointer={pTypeP}/{pIdP} Sender==Source={(sender == e.Source)} Scale={GetCurrentScale(card)} Opacity={card.Opacity}");
#endif

                var nowP = DateTime.UtcNow.Ticks;
                if (_lastPointerPress.TryGetValue(card, out var lastP) && lastP.pointerId == pIdP && (nowP - lastP.ticks) < TimeSpan.FromMilliseconds(200).Ticks)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerPressed ignored duplicate -> Card={GetControlId(card)} Pointer={pTypeP}/{pIdP}");
#endif
                    return;
                }
                _lastPointerPress[card] = (nowP, pIdP);

                if (!IsPointInsideRoundedRect(tile, e))
                {
                    e.Handled = true;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerPressed ignored (outside rounded area) -> Tile={GetControlId(tile)}");
#endif
                    return;
                }

                try { card.Opacity = 0.92; } catch { }

                _ = AnimateTileVisualsAsync(tile, card, 0.98, 80);
            }
        }

        private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            var tile = FindTileButton(sender as Control ?? e.Source as Control);
            if (tile != null)
            {
                var card = tile.FindControl<Border>("CardInner") as Control ?? tile.FindControl<Border>("CardBorder") as Control ?? tile;
                var hoverTarget = (tile.IsPointerOver) ? 1.03 : 1.0;
                var pIdR = e.Pointer?.Id ?? -1;
                var pTypeR = e.Pointer?.Type.ToString() ?? "Unknown";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerReleased -> Tile={GetControlId(tile)} Card={GetControlId(card)} Pointer={pTypeR}/{pIdR} IsPointerOver={tile.IsPointerOver} CurrentScale={GetCurrentScale(card)} => TargetScale={hoverTarget}");
#endif

                _ = AnimateTileVisualsAsync(tile, card, hoverTarget, 120);

                try { card.Opacity = tile.IsPointerOver ? 0.98 : 1.0; } catch { }
            }
        }

        private async System.Threading.Tasks.Task AnimateTileVisualsAsync(Control tile, Control card, double targetScale, int durationMs)
        {

            if (_animCts.TryGetValue(tile, out var existing))
            {
                try { existing.Cancel(); } catch { }
            }

            var cts = new System.Threading.CancellationTokenSource();
            _animCts[tile] = cts;
            var token = cts.Token;

            try
            {

                Avalonia.Media.ScaleTransform st;
                if (card.RenderTransform is Avalonia.Media.ScaleTransform existingSt)
                {
                    st = existingSt;
                }
                else
                {
                    st = new Avalonia.Media.ScaleTransform(1, 1);
                    card.RenderTransform = st;
                }

                var shadow = tile.FindControl<Border>("Shadow");
                Avalonia.Media.TranslateTransform? tt = null;
                double shadowStartY = 4.0;
                double shadowStartOpacity = 0.08;
                if (shadow != null)
                {
                    if (shadow.RenderTransform is Avalonia.Media.TranslateTransform existingTt)
                    {
                        tt = existingTt;
                        shadowStartY = existingTt.Y;
                    }
                    else
                    {
                        tt = new Avalonia.Media.TranslateTransform(0, shadowStartY);
                        shadow.RenderTransform = tt;
                    }
                    shadowStartOpacity = shadow.Opacity;
                }

                var start = DateTime.UtcNow;
                var initial = st.ScaleX;
                var end = targetScale;
                var duration = TimeSpan.FromMilliseconds(durationMs);

                double shadowEndY;
                double shadowEndOpacity;
                if (end >= 1.03)
                {

                    shadowEndY = 6.0;
                    shadowEndOpacity = 0.12;
                }
                else if (end < 1.0)
                {

                    shadowEndY = 2.0;
                    shadowEndOpacity = 0.06;
                }
                else
                {

                    shadowEndY = 4.0;
                    shadowEndOpacity = 0.08;
                }

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var elapsed = DateTime.UtcNow - start;
                    double t = Math.Min(1.0, elapsed.TotalMilliseconds / Math.Max(1, duration.TotalMilliseconds));
                    var eased = 1 - Math.Pow(1 - t, 3);

                    var current = initial + (end - initial) * eased;
                    var rounded = Math.Round(current, 3);
                    st.ScaleX = rounded;
                    st.ScaleY = rounded;

                    if (shadow != null && tt != null)
                    {
                        var sy = shadowStartY + (shadowEndY - shadowStartY) * eased;
                        tt.Y = Math.Round(sy, 2);
                        var so = shadowStartOpacity + (shadowEndOpacity - shadowStartOpacity) * eased;
                        shadow.Opacity = Math.Round(so, 3);
                    }

                    if (t >= 1.0) break;
                    await System.Threading.Tasks.Task.Delay(16, token);
                }

                st.ScaleX = end;
                st.ScaleY = end;
                if (shadow != null && tt != null)
                {
                    tt.Y = shadowEndY;
                    shadow.Opacity = shadowEndOpacity;
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] AnimateTileVisualsAsync complete -> Tile={GetControlId(tile)} Card={GetControlId(card)} FinalScale={end}");
#endif
            }
            catch (OperationCanceledException)
            {

            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] Animation error: {ex.Message}");
            }
#pragma warning restore CA1031
            finally
            {
                _animCts.TryRemove(tile, out _);
            }
        }

        private Control? FindTileButton(Control? start)
        {
            var cur = start;
            while (cur != null)
            {
                if (cur.Classes.Contains("import-tile")) return cur;

                if (cur.Parent is Control p)
                {
                    cur = p;
                    continue;
                }
                break;
            }
            return null;
        }

        private static string GetControlId(Control c)
        {

            return !string.IsNullOrEmpty(c.Name) ? c.Name : c.GetHashCode().ToString();
        }

        private static double GetCurrentScale(Control c)
        {
            if (c?.RenderTransform is Avalonia.Media.ScaleTransform st) return st.ScaleX;
            return 1.0;
        }

        private static bool IsPointInsideRoundedRect(Control button, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                var p = e.GetPosition(button);
                var w = button.Bounds.Width;
                var h = button.Bounds.Height;

                double r = 12.0;

                if (p.X >= r && p.X <= w - r && p.Y >= r && p.Y <= h - r) return true;

                if (p.X < r && p.Y < r)
                {
                    var dx = r - p.X;
                    var dy = r - p.Y;
                    return (dx * dx + dy * dy) <= (r * r);
                }

                if (p.X > w - r && p.Y < r)
                {
                    var dx = p.X - (w - r);
                    var dy = r - p.Y;
                    return (dx * dx + dy * dy) <= (r * r);
                }

                if (p.X < r && p.Y > h - r)
                {
                    var dx = r - p.X;
                    var dy = p.Y - (h - r);
                    return (dx * dx + dy * dy) <= (r * r);
                }

                if (p.X > w - r && p.Y > h - r)
                {
                    var dx = p.X - (w - r);
                    var dy = p.Y - (h - r);
                    return (dx * dx + dy * dy) <= (r * r);
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private async System.Threading.Tasks.Task AnimateScaleAsync(Control control, double target, int durationMs)
        {

            if (_animCts.TryGetValue(control, out var existing))
            {
                try { existing.Cancel(); } catch { }
            }

            var cts = new System.Threading.CancellationTokenSource();
            _animCts[control] = cts;
            var token = cts.Token;

            try
            {

                Avalonia.Media.ScaleTransform st;
                if (control.RenderTransform is Avalonia.Media.ScaleTransform existingSt)
                {
                    st = existingSt;
                }
                else
                {
                    st = new Avalonia.Media.ScaleTransform(1, 1);
                    control.RenderTransform = st;
                }

                var start = DateTime.UtcNow;
                var initial = st.ScaleX;
                var end = target;
                var duration = TimeSpan.FromMilliseconds(durationMs);

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var elapsed = DateTime.UtcNow - start;
                    double t = Math.Min(1.0, elapsed.TotalMilliseconds / Math.Max(1, duration.TotalMilliseconds));

                    var eased = 1 - Math.Pow(1 - t, 3);
                    var current = initial + (end - initial) * eased;

                    var rounded = Math.Round(current, 3);
                    st.ScaleX = rounded;
                    st.ScaleY = rounded;

                    if (t >= 1.0) break;
                    await System.Threading.Tasks.Task.Delay(16, token);
                }

                st.ScaleX = end;
                st.ScaleY = end;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] AnimateScaleAsync complete -> Target={GetControlId(control)} FinalScale={end} Opacity={control.Opacity}");
#endif
            }
            catch (OperationCanceledException)
            {

            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] Animation error: {ex.Message}");
            }
#pragma warning restore CA1031
            finally
            {

                _animCts.TryRemove(control, out _);
            }
        }
    }
}

