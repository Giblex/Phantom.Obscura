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

        // Simple animation cancellation tokens per control
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, System.Threading.CancellationTokenSource> _animCts = new();
        // Debounce tokens for hover enter/leave to avoid quick oscillation when pointer moves near edges
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, System.Threading.CancellationTokenSource> _hoverCts = new();
        // Track last pointer events per control to avoid duplicate handling when the same pointer generates rapid events
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, (long ticks, long pointerId)> _lastPointerEnter = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, (long ticks, long pointerId)> _lastPointerPress = new();

        public ImportWindow()
        {
            InitializeComponent();

            // Attach pointer handlers to the ItemsControl so we can animate tile scale smoothly in code (no extra packages required)
            // Pointer handlers are attached directly on the tile Buttons in XAML (PointerEnter/Leave/Pressed/Released)
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
                // Prefer animating the internal CardInner when available to isolate input surface from visual transform
                var card = tile.FindControl<Border>("CardInner") as Control ?? tile.FindControl<Border>("CardBorder") as Control ?? tile;
                var pId = e.Pointer?.Id ?? -1;
                var pType = e.Pointer?.Type.ToString() ?? "Unknown";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerEnter -> Tile={GetControlId(tile)} Card={GetControlId(card)} Pointer={pType}/{pId} Sender==Source={(sender == e.Source)} Scale={GetCurrentScale(card)} Opacity={card.Opacity}");
#endif

                // Avoid duplicate enter events from the same pointer within a short window
                var now = DateTime.UtcNow.Ticks;
                if (_lastPointerEnter.TryGetValue(card, out var last) && last.pointerId == pId && (now - last.ticks) < TimeSpan.FromMilliseconds(120).Ticks)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerEnter ignored duplicate -> Card={GetControlId(card)} Pointer={pType}/{pId}");
#endif
                    return;
                }
                _lastPointerEnter[card] = (now, pId);

                // Debounce small hover movements to avoid bouncing when pointer skims edges
                if (_hoverCts.TryGetValue(card, out var existing))
                {
                    try { existing.Cancel(); } catch { }
                }

                var cts = new System.Threading.CancellationTokenSource();
                _hoverCts[card] = cts;
                try
                {
                    // Wait a short time; if pointer leaves quickly this will be cancelled and the animation won't start
                    await System.Threading.Tasks.Task.Delay(60, cts.Token);
                    if (cts.Token.IsCancellationRequested) return;

                    // Hover: slightly larger and snappy (reduced to avoid overlap jitter)
                    const double hoverScale = 1.03; // reduced to minimize overlap jitter
                    // Avoid redundant animation if already near target
                    if (Math.Abs(GetCurrentScale(card) - hoverScale) > 0.01)
                    {
                        _ = AnimateTileVisualsAsync(tile, card, hoverScale, 120);
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancelled -> nothing to do
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

                // Cancel any pending hover start
                if (_hoverCts.TryGetValue(card, out var pending))
                {
                    try { pending.Cancel(); } catch { }
                    _hoverCts.TryRemove(card, out _);
                }

                // Delay restore slightly to reduce flicker when pointer briefly leaves due to visual scaling.
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

                // Avoid duplicate presses from same pointer within short window
                var nowP = DateTime.UtcNow.Ticks;
                if (_lastPointerPress.TryGetValue(card, out var lastP) && lastP.pointerId == pIdP && (nowP - lastP.ticks) < TimeSpan.FromMilliseconds(200).Ticks)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerPressed ignored duplicate -> Card={GetControlId(card)} Pointer={pTypeP}/{pIdP}");
#endif
                    return;
                }
                _lastPointerPress[card] = (nowP, pIdP);
                // Hit-test against rounded card; ignore presses outside rounded corners so the click area matches the visual card
                if (!IsPointInsideRoundedRect(tile, e))
                {
                    e.Handled = true;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ImportWindow] PointerPressed ignored (outside rounded area) -> Tile={GetControlId(tile)}");
#endif
                    return;
                }

                // Dim slightly immediately to avoid style conflicts and make press feel tactile
                try { card.Opacity = 0.92; } catch { }
                // Press: slightly smaller for tactile feel - animate visuals including shadow
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
                // restore hover size (if pointer still over) quickly
                _ = AnimateTileVisualsAsync(tile, card, hoverTarget, 120);
                // restore opacity
                try { card.Opacity = tile.IsPointerOver ? 0.98 : 1.0; } catch { }
            }
        }

        private async System.Threading.Tasks.Task AnimateTileVisualsAsync(Control tile, Control card, double targetScale, int durationMs)
        {
            // Use the tile as the coordination key so shadow + card animate together
            if (_animCts.TryGetValue(tile, out var existing))
            {
                try { existing.Cancel(); } catch { }
            }

            var cts = new System.Threading.CancellationTokenSource();
            _animCts[tile] = cts;
            var token = cts.Token;

            try
            {
                // Ensure card has a ScaleTransform
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

                // Find shadow element in template
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

                // Determine shadow target based on scale state
                double shadowEndY;
                double shadowEndOpacity;
                if (end >= 1.03)
                {
                    // hover
                    shadowEndY = 6.0;
                    shadowEndOpacity = 0.12;
                }
                else if (end < 1.0)
                {
                    // press
                    shadowEndY = 2.0;
                    shadowEndOpacity = 0.06;
                }
                else
                {
                    // neutral
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

                // Ensure final values
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
                // expected
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
                // climb logical parent first
                if (cur.Parent is Control p)
                {
                    cur = p;
                    continue;
                }
                break;
            }
            return null;
        }

        // Helpers for logging
        private static string GetControlId(Control c)
        {
            // Prefer Name for easy identification; fall back to hashcode
            return !string.IsNullOrEmpty(c.Name) ? c.Name : c.GetHashCode().ToString();
        }

        private static double GetCurrentScale(Control c)
        {
            if (c?.RenderTransform is Avalonia.Media.ScaleTransform st) return st.ScaleX;
            return 1.0;
        }

        // Return true if the pointer event is inside the rounded rect area of the templated card
        private static bool IsPointInsideRoundedRect(Control button, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                var p = e.GetPosition(button);
                var w = button.Bounds.Width;
                var h = button.Bounds.Height;
                // Corner radius used in template
                double r = 12.0;

                // central rectangle
                if (p.X >= r && p.X <= w - r && p.Y >= r && p.Y <= h - r) return true;

                // check corners
                // top-left
                if (p.X < r && p.Y < r)
                {
                    var dx = r - p.X;
                    var dy = r - p.Y;
                    return (dx * dx + dy * dy) <= (r * r);
                }
                // top-right
                if (p.X > w - r && p.Y < r)
                {
                    var dx = p.X - (w - r);
                    var dy = r - p.Y;
                    return (dx * dx + dy * dy) <= (r * r);
                }
                // bottom-left
                if (p.X < r && p.Y > h - r)
                {
                    var dx = r - p.X;
                    var dy = p.Y - (h - r);
                    return (dx * dx + dy * dy) <= (r * r);
                }
                // bottom-right
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
                return true; // on error, allow
            }
        }

        private async System.Threading.Tasks.Task AnimateScaleAsync(Control control, double target, int durationMs)
        {
            // Cancel existing animation for this control
            if (_animCts.TryGetValue(control, out var existing))
            {
                try { existing.Cancel(); } catch { }
            }

            var cts = new System.Threading.CancellationTokenSource();
            _animCts[control] = cts;
            var token = cts.Token;

            try
            {
                // Ensure RenderTransform is a ScaleTransform (create a per-control instance)
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
                    // cubic ease-out
                    var eased = 1 - Math.Pow(1 - t, 3);
                    var current = initial + (end - initial) * eased;
                    // Reduce micro-jitter by rounding to 3 decimals
                    var rounded = Math.Round(current, 3);
                    st.ScaleX = rounded;
                    st.ScaleY = rounded;

                    if (t >= 1.0) break;
                    await System.Threading.Tasks.Task.Delay(16, token);
                }
                // Ensure exact final value is applied
                st.ScaleX = end;
                st.ScaleY = end;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] AnimateScaleAsync complete -> Target={GetControlId(control)} FinalScale={end} Opacity={control.Opacity}");
#endif
            }
            catch (OperationCanceledException)
            {
                // expected when interrupted
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportWindow] Animation error: {ex.Message}");
            }
#pragma warning restore CA1031
            finally
            {
                // Remove token if it still matches
                _animCts.TryRemove(control, out _);
            }
        }
    }
}
