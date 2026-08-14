using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace PhantomVault.UI.Views.Autofill
{
    /// <summary>
    /// Non-interactive animation overlay for the AutoFill flow.
    ///
    /// Sequence:
    ///   1. The PhantomObscura mark springs up out of the system tray and drops back.
    ///   2. One continuous stroke draws from the left edge of the target field, around
    ///      the field, back out to the left, and around the ring where the badge lands.
    ///   3. The badge takes over the finished ring and the stroke fades.
    ///
    /// SAFETY — this window is topmost and borderless, so if it ever leaks it sits over
    /// everything the user is trying to click. Three independent guards exist:
    ///   * WS_EX_TRANSPARENT/NOACTIVATE are applied to the native handle, so the OS
    ///     routes every click straight through to whatever is underneath.
    ///   * A watchdog force-closes the window after <see cref="WatchdogMs"/> no matter
    ///     what the animation is doing.
    ///   * Callers must wrap the whole sequence in try/finally and Close().
    /// An earlier build had none of these and an exception mid-sequence left a
    /// full-screen input-swallowing window on top of the desktop.
    ///
    /// Coordinates: the canvas works in LOGICAL units relative to the overlay's own
    /// region origin. Field rects and Win32 tray positions arrive in PHYSICAL pixels.
    /// </summary>
    public sealed class AutofillUsbIndicatorOverlay : Window
    {
        // Deep, desaturated line colours. These are drawn strokes, not glows, and read
        // against dark application chrome without lighting up the screen.
        private static readonly Color Teal = Color.Parse("#0F7A6E");
        private static readonly Color TealBlue = Color.Parse("#146B8C");
        private static readonly Color Blue = Color.Parse("#1D4E96");

        private const double StrokeWidth = 1.8;
        private const int FrameMs = 16;      // 60fps. 8ms saturated the dispatcher.
        private const int WatchdogMs = 7000;

        private readonly Canvas _canvas;
        private DispatcherTimer? _watchdog;

        private double _scale = 1.0;
        private double _originX;   // logical, world -> canvas offset
        private double _originY;

        private Avalonia.Controls.Shapes.Path? _stroke;

        public PixelPoint CollapsedIconPosition { get; private set; }

        public AutofillUsbIndicatorOverlay()
        {
            ShowInTaskbar = false;
            Topmost = true;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = false;

            _canvas = new Canvas();
            Content = _canvas;

            var screen = Screens?.Primary;
            if (screen != null) _scale = screen.Scaling;

            Opened += (_, _) =>
            {
                MakeClickThrough();
                StartWatchdog();
            };
        }

        // ── Safety ────────────────────────────────────────────────────────────

        /// <summary>
        /// IsHitTestVisible only affects Avalonia's own routing — the native window
        /// still owns its screen region and swallows clicks. WS_EX_TRANSPARENT makes
        /// the OS hit-test fall through to the window underneath.
        /// </summary>
        private void MakeClickThrough()
        {
            try
            {
                var handle = TryGetPlatformHandle();
                if (handle == null || handle.Handle == IntPtr.Zero) return;
                var hwnd = handle.Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutofillOverlay] click-through failed: {ex.Message}");
            }
        }

        /// <summary>Last-resort close, independent of the animation's own control flow.</summary>
        private void StartWatchdog()
        {
            _watchdog = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(WatchdogMs)
            };
            _watchdog.Tick += (_, _) => ForceClose();
            _watchdog.Start();
        }

        /// <summary>Idempotent close. Safe from anywhere, including a failed animation.</summary>
        public void ForceClose()
        {
            try
            {
                _watchdog?.Stop();
                _watchdog = null;
                Close();
            }
            catch (InvalidOperationException) { /* already closed */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            _watchdog?.Stop();
            _watchdog = null;
            base.OnClosed(e);
        }

        // ── Region management ─────────────────────────────────────────────────

        /// <summary>
        /// Sizes the overlay to just the area it needs to draw in.
        ///
        /// The overlay used to cover the whole screen. Every animation frame then
        /// forced a full-screen layered-window recomposite, which is enough to stall
        /// the desktop compositor on a high-resolution display. Keeping the surface to
        /// a few hundred pixels removes that entirely.
        /// </summary>
        private void SetRegion(double worldX, double worldY, double w, double h)
        {
            var screen = Screens?.Primary;
            var bounds = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

            // Clamp to the screen so we never allocate a surface off in space.
            double px = worldX * _scale, py = worldY * _scale;
            double pw = w * _scale, ph = h * _scale;
            px = Math.Max(bounds.X, Math.Min(px, bounds.X + bounds.Width - 8));
            py = Math.Max(bounds.Y, Math.Min(py, bounds.Y + bounds.Height - 8));
            pw = Math.Min(pw, bounds.X + bounds.Width - px);
            ph = Math.Min(ph, bounds.Y + bounds.Height - py);

            Position = new PixelPoint((int)px, (int)py);
            Width = pw / _scale;
            Height = ph / _scale;

            _originX = px / _scale;
            _originY = py / _scale;
        }

        private double Cx(double worldX) => worldX - _originX;
        private double Cy(double worldY) => worldY - _originY;

        // ── Entry point ───────────────────────────────────────────────────────

        public async Task RunAsync(PixelRect fieldRectPhysical, CancellationToken ct = default)
        {
            double fx = fieldRectPhysical.X / _scale;
            double fy = fieldRectPhysical.Y / _scale;
            double fw = fieldRectPhysical.Width / _scale;
            double fh = fieldRectPhysical.Height / _scale;

            var trayPhys = GetSystemTrayCenter();
            await TrayLogoJump(trayPhys.X / _scale, trayPhys.Y / _scale, ct);
            if (ct.IsCancellationRequested) return;
            await TraceFieldIntoIcon(fx, fy, fw, fh, ct);
        }

        // ── 1. Tray logo jump ─────────────────────────────────────────────────

        /// <summary>
        /// Anticipate → launch with overshoot → hover → drop. The squash-and-stretch is
        /// what sells it as a jump; a plain fade-and-translate reads as a sliding image.
        /// </summary>
        private async Task TrayLogoJump(double tx, double ty, CancellationToken ct)
        {
            const double size = 46;
            const double apex = 74;
            const double dip = 6;
            const double pad = 24;

            SetRegion(tx - size, ty - apex - size - pad, size * 2 + pad, apex + size * 2 + pad * 2);

            var bmp = LoadLogo();
            if (bmp == null) return; // nothing to draw; skip rather than throw

            var scale = new ScaleTransform(1, 1);
            var rotate = new RotateTransform(0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(rotate);

            var logo = new Image
            {
                Source = bmp,
                Width = size,
                Height = size,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                RenderTransform = group
            };

            // Contact shadow. Without a ground reference a rising sprite reads as
            // "drifting upward" rather than "jumping" — the shadow shrinking and
            // fading as the mark climbs is what actually conveys height.
            var shadow = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = size * 0.8,
                Height = size * 0.18,
                Opacity = 0,
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#66000000"), 0),
                        new GradientStop(Color.Parse("#00000000"), 1)
                    }
                }
            };

            Canvas.SetLeft(logo, Cx(tx - size / 2));
            double baseTop = Cy(ty);
            Canvas.SetTop(logo, baseTop);

            double shadowBaseTop = baseTop + size - shadow.Height / 2;
            await UI(() =>
            {
                _canvas.Children.Add(shadow);
                _canvas.Children.Add(logo);
            });

            // Height fraction (0 grounded, 1 at apex) -> shadow size and opacity.
            void SetShadow(double height01)
            {
                double s = 1 - 0.55 * height01;
                shadow.Width = size * 0.8 * s;
                shadow.Height = size * 0.18 * s;
                shadow.Opacity = (1 - 0.75 * height01) * 0.9;
                Canvas.SetLeft(shadow, Cx(tx) - shadow.Width / 2);
                Canvas.SetTop(shadow, shadowBaseTop);
            }

            // Crouch. Squash only — no vertical overshoot to fight the launch.
            await Animate(140, t =>
            {
                double e = SineOut(t);
                logo.Opacity = e * 0.9;
                scale.ScaleX = 1 + 0.14 * e;
                scale.ScaleY = 1 - 0.17 * e;
                Canvas.SetTop(logo, baseTop + dip * e);
                SetShadow(0);
                shadow.Opacity = e * 0.9;
            }, ct);

            // Rise. SineOut decelerates to exactly zero velocity at the apex, which is
            // what a thrown object does. The previous version used a Back ease on
            // POSITION, so the mark overshot the apex and snapped back down — that
            // reversal is what read as jerky. Squash/stretch is driven by remaining
            // velocity (cos), so it eases off naturally instead of being keyed to time.
            double apexTop = baseTop - apex;
            await Animate(400, t =>
            {
                double e = SineOut(t);
                double vel = Math.Cos(t * Math.PI / 2); // 1 at launch → 0 at apex
                logo.Opacity = 1;
                scale.ScaleX = 1 - 0.12 * vel;
                scale.ScaleY = 1 + 0.16 * vel;
                rotate.Angle = -4.5 * vel;
                Canvas.SetTop(logo, baseTop + dip - (apex + dip) * e);
                SetShadow(e);
            }, ct);

            // Hover. Starts and ends at zero vertical velocity so it joins the rise and
            // the fall without a visible seam.
            await Animate(320, t =>
            {
                scale.ScaleX = 1; scale.ScaleY = 1;
                rotate.Angle = 1.8 * Math.Sin(t * Math.PI * 2);
                double bob = 3.5 * Math.Sin(t * Math.PI * 2);
                Canvas.SetTop(logo, apexTop + bob);
                SetShadow(1 - bob / apex);
            }, ct);

            // Fall. SineIn leaves the apex at zero velocity and accelerates, mirroring
            // the rise, so apex → descent is continuous.
            await Animate(360, t =>
            {
                double e = SineIn(t);
                double vel = Math.Sin(t * Math.PI / 2); // 0 at apex → 1 at landing
                logo.Opacity = 1;
                scale.ScaleX = 1 + 0.16 * vel;
                scale.ScaleY = 1 - 0.19 * vel;
                rotate.Angle = 0;
                Canvas.SetTop(logo, apexTop + (apex + dip) * e);
                SetShadow(1 - e);
            }, ct);

            // Landing settle: a small secondary hop, then recover from the squash.
            // Real jumps do not stop dead on contact, and ending on the impact frame
            // was the most obviously synthetic part of the sequence.
            double landedTop = apexTop + apex + dip;
            await Animate(150, t =>
            {
                double hop = Math.Sin(t * Math.PI) * 11;   // up and back down
                double settle = 1 - Math.Sin(t * Math.PI); // squash relaxes at the ends
                scale.ScaleX = 1 + 0.16 * settle;
                scale.ScaleY = 1 - 0.19 * settle;
                Canvas.SetTop(logo, landedTop - hop);
                SetShadow(hop / apex);
            }, ct);

            // Sink away rather than blinking out.
            await Animate(180, t =>
            {
                double e = EaseIn(t);
                logo.Opacity = 1 - e;
                shadow.Opacity = (1 - e) * 0.9;
                scale.ScaleX = 1 + 0.10 * e;
                scale.ScaleY = 1 - 0.30 * e;
                Canvas.SetTop(logo, landedTop + 10 * e);
            }, ct);

            await UI(() =>
            {
                _canvas.Children.Remove(logo);
                _canvas.Children.Remove(shadow);
            });
        }

        /// <summary>
        /// Loads the mark as a bitmap. Deliberately the PNG, not ok.svg: that SVG is a
        /// single 314KB path authored at 2000x2000, which is needlessly expensive to
        /// rasterise every frame of an animation.
        /// </summary>
        private static Bitmap? LoadLogo()
        {
            try
            {
                return new Bitmap(AssetLoader.Open(new Uri("avares://PhantomVault.UI/Assets/ok.png")));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutofillOverlay] logo load failed: {ex.Message}");
                return null;
            }
        }

        // ── 2. Continuous stroke: field border → connector → icon ring ────────

        /// <summary>
        /// One open figure, so a single dash-offset animation draws the whole thing in
        /// sequence and the stroke visibly leaves the field and continues into the icon
        /// ring, rather than two effects firing back to back.
        /// </summary>
        private async Task TraceFieldIntoIcon(
            double fx, double fy, double fw, double fh, CancellationToken ct)
        {
            const double pad = 3;
            const double corner = 6;

            double x = fx - pad, y = fy - pad;
            double w = fw + pad * 2, h = fh + pad * 2;
            double cy = y + h / 2;
            double r = Math.Min(corner, Math.Min(w, h) / 2);

            double iconCx = fx - AutofillIconBadge.FieldGap - AutofillIconBadge.BadgeSize / 2;
            double iconCy = fy + fh / 2;
            // Exactly the badge's visible ring, so the traced circle and the badge's
            // own border are the same line.
            double ringR = AutofillIconBadge.RingDiameter / 2;

            CollapsedIconPosition = new PixelPoint(
                (int)Math.Round((iconCx - AutofillIconBadge.BadgeSize / 2) * _scale),
                (int)Math.Round((iconCy - AutofillIconBadge.BadgeSize / 2) * _scale));

            // Region: the field plus the icon on its left, with a little breathing room.
            double regionX = iconCx - ringR - 12;
            double regionY = y - 12;
            SetRegion(regionX, regionY, (x + w) - regionX + 12, h + 24);

            double ringEntryX = iconCx + ringR;

            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                // Start at the left edge midpoint and run clockwise, so the lap ends on
                // the side the icon lives on.
                c.BeginFigure(new Point(Cx(x), Cy(cy)), isFilled: false);
                c.LineTo(new Point(Cx(x), Cy(y + r)));
                c.ArcTo(new Point(Cx(x + r), Cy(y)), new Size(r, r), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(Cx(x + w - r), Cy(y)));
                c.ArcTo(new Point(Cx(x + w), Cy(y + r)), new Size(r, r), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(Cx(x + w), Cy(y + h - r)));
                c.ArcTo(new Point(Cx(x + w - r), Cy(y + h)), new Size(r, r), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(Cx(x + r), Cy(y + h)));
                c.ArcTo(new Point(Cx(x), Cy(y + h - r)), new Size(r, r), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(Cx(x), Cy(cy)));

                // Break out toward the icon, then draw its ring.
                c.LineTo(new Point(Cx(ringEntryX), Cy(iconCy)));
                c.ArcTo(new Point(Cx(iconCx - ringR), Cy(iconCy)), new Size(ringR, ringR), 0, false, SweepDirection.Clockwise);
                c.ArcTo(new Point(Cx(ringEntryX), Cy(iconCy)), new Size(ringR, ringR), 0, false, SweepDirection.Clockwise);
                c.EndFigure(isClosed: false);
            }

            double rectLen = 2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * Math.PI * r;
            double connLen = Math.Abs(x - ringEntryX);
            double totalLen = rectLen + connLen + 2 * Math.PI * ringR;

            // StrokeDashArray is in multiples of StrokeThickness, not pixels. Passing
            // raw pixel lengths makes the reveal finish early and the trace feel
            // mistimed, which is what an earlier version did.
            double dashUnits = totalLen / StrokeWidth;

            _stroke = new Avalonia.Controls.Shapes.Path
            {
                Data = geo,
                StrokeThickness = StrokeWidth,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Blue, 0.0),
                        new GradientStop(TealBlue, 0.5),
                        new GradientStop(Teal, 1.0)
                    }
                },
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(dashUnits, dashUnits),
                StrokeDashOffset = dashUnits,
                IsHitTestVisible = false
            };

            var stroke = _stroke;
            await UI(() => _canvas.Children.Add(stroke));

            await Animate(640, t => stroke.StrokeDashOffset = dashUnits * (1 - EaseInOut(t)), ct);
            await Delay(180, ct);
        }

        /// <summary>
        /// Fades the finished stroke out. Called once the badge is up, so the badge's
        /// own border is already sitting under the drawn ring.
        /// </summary>
        public async Task FadeOutAsync(CancellationToken ct = default)
        {
            var stroke = _stroke;
            if (stroke == null) return;
            await Animate(170, t => stroke.Opacity = 1 - EaseOut(t), ct);
            await UI(() => _canvas.Children.Clear());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task Animate(int ms, Action<double> step, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Normal priority, not Render: at Render the ticks outrank input handling,
            // so a slow frame starves the UI thread instead of just dropping a frame.
            var timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(FrameMs)
            };

            void Finish(bool ok) { timer.Stop(); tcs.TrySetResult(ok); }

            timer.Tick += (_, _) =>
            {
                if (ct.IsCancellationRequested) { Finish(false); return; }
                try
                {
                    double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                    step(t);
                    if (t >= 1) Finish(true);
                }
                catch (Exception ex)
                {
                    // A throwing frame must not leave the caller awaiting forever —
                    // that is how the overlay used to get stranded on screen.
                    System.Diagnostics.Debug.WriteLine($"[AutofillOverlay] frame failed: {ex.Message}");
                    Finish(false);
                }
            };

            try { step(0); } catch { /* first frame is best-effort */ }
            timer.Start();
            await tcs.Task;
        }

        private static Task UI(Action a) => Dispatcher.UIThread.InvokeAsync(a).GetTask();

        private static Task Delay(int ms, CancellationToken ct)
            => ct.IsCancellationRequested ? Task.CompletedTask : Task.Delay(ms, ct).ContinueWith(_ => { });

        private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
        private static double EaseIn(double t) => t * t * t;
        private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        // Sine pair for the jump. Their velocities meet at zero, so rise → hover → fall
        // reads as one continuous arc rather than three stitched segments.
        private static double SineOut(double t) => Math.Sin(t * Math.PI / 2);
        private static double SineIn(double t) => 1 - Math.Cos(t * Math.PI / 2);

        // ── Win32 ─────────────────────────────────────────────────────────────

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint f);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static PixelPoint GetSystemTrayCenter()
        {
            try
            {
                var wa = new RECT();
                SystemParametersInfo(0x30, 0, ref wa, 0); // SPI_GETWORKAREA
                int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
                int tbH = sh - wa.bottom;
                int tbMY = wa.bottom + (tbH > 0 ? tbH / 2 : 20);
                return new PixelPoint(sw - 100, tbMY);
            }
            catch
            {
                return new PixelPoint(GetSystemMetrics(0) - 100, GetSystemMetrics(1) - 22);
            }
        }
    }
}
