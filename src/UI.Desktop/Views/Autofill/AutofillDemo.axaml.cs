using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Standalone demo window that exercises the full USB AutoFill animation flow
    /// without needing a real USB insert or an unlocked vault.
    /// Open via: Settings → AutoFill → "Preview animation" button,
    /// or call <see cref="AutofillDemo.ShowDemo"/> from any ViewModel.
    /// </summary>
    public partial class AutofillDemo : Window
    {
        private Button? _simulateUsbBtn;
        private Button? _simulateBrowserBtn;
        private Button? _closeBtn;
        private TextBlock? _statusLabel;
        private TextBox? _emailBox;
        private TextBox? _passwordBox;

        // Fake matches used in the demo
        private static readonly IReadOnlyList<CredentialMatch> DemoMatches = new[]
        {
            new CredentialMatch
            {
                DisplayName = "example.com — Personal",
                Username    = "jane.doe@example.com",
                Domain      = "example.com",
                ConfidenceScore = 95,
                LastUsed = DateTime.UtcNow.AddDays(-1)
            },
            new CredentialMatch
            {
                DisplayName = "example.com — Work",
                Username    = "jdoe@corp.example.com",
                Domain      = "example.com",
                ConfidenceScore = 72,
                HasTotp = true
            },
            new CredentialMatch
            {
                DisplayName = "example.com — Passkey",
                Username    = "jane.doe@example.com",
                Domain      = "example.com",
                ConfidenceScore = 45,
                IsPasskey = true,
                LastUsed = DateTime.UtcNow.AddDays(-120)
            }
        };

        public AutofillDemo()
        {
            AvaloniaXamlLoader.Load(this);

            _simulateUsbBtn   = this.FindControl<Button>("SimulateUsbBtn");
            _simulateBrowserBtn = this.FindControl<Button>("SimulateBrowserBtn");
            _closeBtn         = this.FindControl<Button>("CloseBtn");
            _statusLabel      = this.FindControl<TextBlock>("StatusLabel");
            _emailBox         = this.FindControl<TextBox>("EmailBox");
            _passwordBox      = this.FindControl<TextBox>("PasswordBox");

            if (_simulateUsbBtn != null)
                _simulateUsbBtn.Click += (_, _) => RunDemoAsync(fromBrowser: false);
            if (_simulateBrowserBtn != null)
                _simulateBrowserBtn.Click += (_, _) => RunDemoAsync(fromBrowser: true);
            if (_closeBtn != null)
                _closeBtn.Click += (_, _) => Close();
        }

        /// <summary>Opens the demo window from anywhere in the app.</summary>
        public static void ShowDemo(Window? owner = null)
        {
            var demo = new AutofillDemo();
            if (owner != null)
                demo.ShowDialog(owner);
            else
                demo.Show();
        }

        private async void RunDemoAsync(bool fromBrowser)
        {
            if (_simulateUsbBtn != null) _simulateUsbBtn.IsEnabled = false;
            if (_simulateBrowserBtn != null) _simulateBrowserBtn.IsEnabled = false;
            SetStatus("🔌 USB inserted — detecting fields…");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

                // ── Get the screen position of the Email TextBox ──────────────────
                var fieldRect = GetEmailBoxScreenRect();

                // USB port: bottom-right of primary screen

                SetStatus("⚡ Beam firing…");

                // ── 1. Beam overlay (full screen, non-interactive) ─────────────────
                // The overlay is topmost and borderless. It MUST be closed on every
                // path, including exceptions: a leaked one sits over the whole desktop.
                var overlay = new AutofillUsbIndicatorOverlay();
                try
                {
                    overlay.Show();
                    await overlay.RunAsync(fieldRect, cts.Token);
                }
                catch
                {
                    overlay.ForceClose();
                    throw;
                }

                SetStatus("✨ Icon ready — click it!");

                // ── 2. Icon badge appears left of the email field ──────────────────
                // Shown while the traced ring is still on screen, then the stroke
                // fades out over it, so the drawn ring appears to become the badge
                // rather than one disappearing and another popping in.
                // Positioned from the field rect rather than the overlay's collapse
                // point: both use the same constants, but deriving it here means the
                // badge still lands correctly if the overlay was cancelled early.
                AutofillIconBadge badge;
                try
                {
                    badge = new AutofillIconBadge();
                    badge.Show();
                    badge.PositionLeftOfField(fieldRect);

                    var popIn = badge.PopInAsync();
                    await overlay.FadeOutAsync(cts.Token);
                    await popIn;
                }
                finally
                {
                    overlay.ForceClose();
                }

                // ── 3. Icon click → credential menu ───────────────────────────────
                badge.IconClicked += (_, _) =>
                {
                    var domain = fromBrowser ? "example.com (browser)" : "example.com (native)";
                    var badgePos = badge.Position;
                    var badgeH = badge.Height;

                    var menu = new AutofillCredentialMenu(
                        domain,
                        DemoMatches,
                        action => Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var m = action.Match;

                            if (action.Authenticate)
                            {
                                SetStatus($"🔑 Passkey authentication for '{m.DisplayName}'");
                                return;
                            }

                            if (action.Copy)
                            {
                                SetStatus($"📋 Copied {action.Field} from '{m.DisplayName}' (clears in 30s)");
                                return;
                            }

                            switch (action.Field)
                            {
                                case AutoFillField.UsernameOnly:
                                    if (_emailBox != null) _emailBox.Text = m.Username;
                                    SetStatus("✅ Username filled — review, then press Enter");
                                    break;
                                case AutoFillField.PasswordOnly:
                                    if (_passwordBox != null) _passwordBox.Text = "••••••••";
                                    SetStatus("✅ Password filled — review, then press Enter");
                                    break;
                                case AutoFillField.TotpCode:
                                    SetStatus("✅ One-time code entered");
                                    break;
                                default:
                                    if (_emailBox != null) _emailBox.Text = m.Username;
                                    if (_passwordBox != null) _passwordBox.Text = "••••••••";
                                    SetStatus($"✅ Filled '{m.DisplayName}' — not submitted, review first");
                                    break;
                            }
                        }),
                        // Demo TOTP: a code derived from the clock so the countdown ring
                        // visibly rolls over without needing a real secret.
                        totpProvider: _ =>
                        {
                            int step = 30;
                            long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            return System.Threading.Tasks.Task.FromResult<TotpSnapshot?>(new TotpSnapshot
                            {
                                Code = ((unix / step) % 1_000_000).ToString("D6"),
                                SecondsRemaining = step - (int)(unix % step),
                                StepSeconds = step
                            });
                        });

                    // The badge is retired whenever the menu goes away, whatever the
                    // reason — filled, Escape, or click-away. Doing it here only,
                    // rather than also inside the fill callback, avoids closing the
                    // badge twice.
                    menu.Closed += (_, _) => badge.CloseOnce();

                    menu.Show();
                    menu.PositionNearBadge(badgePos, badgeH);
                    menu.Activate();
                };

                // Auto-dismiss after 12 s
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(12_000);
                    badge.CloseOnce();
                    if (_statusLabel?.Text?.StartsWith("⚡") == false)
                        SetStatus("Demo complete — run again anytime.");
                });
            }
            catch (OperationCanceledException)
            {
                SetStatus("Demo timed out.");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                if (_simulateUsbBtn != null) _simulateUsbBtn.IsEnabled = true;
                if (_simulateBrowserBtn != null) _simulateBrowserBtn.IsEnabled = true;
            }
        }

        /// <summary>
        /// Returns the screen-space bounding rect of the Email TextBox
        /// by walking up to the window's screen position.
        /// </summary>
        private PixelRect GetEmailBoxScreenRect()
        {
            if (_emailBox == null) return new PixelRect(600, 400, 320, 40);
            try
            {
                // PointToScreen handles DPI, title bar, borders — no manual math needed
                var tl = _emailBox.PointToScreen(new Point(0, 0));
                var br = _emailBox.PointToScreen(new Point(_emailBox.Bounds.Width, _emailBox.Bounds.Height));
                return new PixelRect(tl.X, tl.Y, Math.Max(br.X - tl.X, 40), Math.Max(br.Y - tl.Y, 30));
            }
            catch { return new PixelRect(600, 400, 320, 40); }
        }

        private void SetStatus(string msg)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_statusLabel != null) _statusLabel.Text = msg;
            });
        }

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
    }
}


