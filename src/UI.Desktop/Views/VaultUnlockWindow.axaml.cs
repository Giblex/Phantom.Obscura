using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class VaultUnlockWindow : ThemeAwareWindow
    {
        public VaultUnlockWindow()
        {
            ThemeScope.SetIsThemed(this, false);
            Serilog.Log.Information("[VaultUnlockWindow] before InitializeComponent");
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VaultUnlockWindow] InitializeComponent threw");
                throw;
            }
            Serilog.Log.Information("[VaultUnlockWindow] after InitializeComponent");

            BuildTumblerRings();

            // The window is undecorated, so there is no title bar to drag by.
            // Dragging anywhere on the circle keeps it movable.
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    try { BeginMoveDrag(e); } catch (InvalidOperationException) { }
                }
            };
        }

        /// <summary>
        /// Draws the two bands of vault-door hardware around the dial.
        ///
        /// Generated rather than hand-written in XAML: 24 + 60 blocks would be ~84
        /// near-identical elements whose only difference is an angle, and every radius
        /// or count tweak would mean editing all of them. Purely decorative and
        /// hit-test-invisible, so it never interferes with the drag-to-move handler.
        /// </summary>
        private void BuildTumblerRings()
        {
            try
            {
                // Outer: chunky bolts, alternating length so the ring reads as a
                // mechanism with indexed positions rather than a uniform sunburst.
                BuildBlockRing(
                    this.FindControl<Canvas>("TumblerRingOuter"),
                    count: 24, blockWidth: 9,
                    longLength: 20, shortLength: 12,
                    colorHex: "#4A6B84", alternateColorHex: "#2E4A5E");

                // Inner: fine graduations, like the numbered collar on a dial.
                BuildBlockRing(
                    this.FindControl<Canvas>("TumblerRingInner"),
                    count: 60, blockWidth: 2.5,
                    longLength: 9, shortLength: 5,
                    colorHex: "#2A4A63", alternateColorHex: "#1C3547");

                // Deepest plate, one step below the collar. More ticks, thinner, shorter
                // and darker than the ring above: detail gets finer and contrast drops
                // with distance, which is what sells it as further down rather than
                // simply as another ring at the same level.
                BuildBlockRing(
                    this.FindControl<Canvas>("TumblerRingDeep"),
                    count: 90, blockWidth: 1.8,
                    longLength: 6, shortLength: 3,
                    colorHex: "#1B3244", alternateColorHex: "#122536");
            }
            catch (Exception ex)
            {
                // Decoration must never stop the unlock dialog from opening.
                Serilog.Log.Warning(ex, "[VaultUnlockWindow] tumbler ring build failed");
            }
        }

        private static void BuildBlockRing(
            Canvas? canvas,
            int count,
            double blockWidth,
            double longLength,
            double shortLength,
            string colorHex,
            string alternateColorHex)
        {
            if (canvas == null || count <= 0) return;

            // The diameter is taken from the Canvas itself rather than passed in.
            // It used to be a separate argument, and when the bands were rescaled in
            // XAML the call sites kept their old numbers — so the blocks were laid out
            // around a centre ~15px off the Canvas's real centre. Everything still
            // looked static-correct, but rotating about the Canvas centre then swung
            // each ring in an eccentric orbit and the two bands collided.
            // Reading the size here means the layout and the rotation can never disagree.
            var diameter = canvas.Width;
            if (double.IsNaN(diameter) || diameter <= 0) return;

            var radius = diameter / 2.0;
            var majorBrush = new SolidColorBrush(Color.Parse(colorHex));
            var minorBrush = new SolidColorBrush(Color.Parse(alternateColorHex));

            for (var i = 0; i < count; i++)
            {
                // Every 4th block on the fine ring (every 2nd on the coarse one) is a
                // major graduation, which is what gives the collar its indexed look.
                var isMajor = count >= 40 ? i % 5 == 0 : i % 2 == 0;
                var length = isMajor ? longLength : shortLength;

                var block = new Rectangle
                {
                    Width = blockWidth,
                    Height = length,
                    Fill = isMajor ? majorBrush : minorBrush,
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                };

                // Angle measured from 12 o'clock so the ring is symmetric about the
                // vertical axis regardless of count.
                var degrees = 360.0 * i / count;
                var radians = (degrees - 90.0) * Math.PI / 180.0;

                // Seat the block's midpoint on the band, then rotate it to face outward.
                var seat = radius - (length / 2.0);
                var cx = radius + (Math.Cos(radians) * seat);
                var cy = radius + (Math.Sin(radians) * seat);

                Canvas.SetLeft(block, cx - (blockWidth / 2.0));
                Canvas.SetTop(block, cy - (length / 2.0));

                block.RenderTransformOrigin = RelativePoint.Center;
                block.RenderTransform = new RotateTransform(degrees);

                canvas.Children.Add(block);
            }
        }

        // ── Centre logo spin ──────────────────────────────────────────────────
        //
        // The mark turns slowly about its VERTICAL axis while the unlock runs, then
        // decelerates and clicks into place as progress completes — like a dial
        // dropping into a detent.
        //
        // Avalonia has no 3D transform, so the rotation is faked by animating ScaleX
        // through cos(angle). At 0 the face is square on; at 90° the width collapses
        // to nothing (edge on); past that cos goes negative and the image mirrors,
        // which is what the back of a turning disc actually looks like. A logo mark
        // survives that; a mark containing text would not, and would need |cos|.

        private ScaleTransform? _logoScale;
        private DispatcherTimer? _logoSpinTimer;

        private double _logoAngle;              // radians
        private DateTime _logoLastTick;

        private bool _logoSettling;
        private double _logoSettleFrom;
        private double _logoSettleTo;
        private DateTime _logoSettleStart;

        /// <summary>Radians per second while the unlock is still working.</summary>
        private const double LogoSpinRate = 0.85;   // ~7.4s per revolution

        /// <summary>How long the final decelerate-and-click takes.</summary>
        private static readonly TimeSpan LogoSettleDuration = TimeSpan.FromMilliseconds(900);

        private void StartLogoSpin(VaultUnlockViewModel viewModel)
        {
            // Honour the accessibility setting: no idle motion at all when the user
            // has asked for less of it. The mark simply stays square on.
            if (Classes.Contains("reduce-motion")) return;

            var logo = this.FindControl<Image>("CentreLogoKey");
            if (logo == null) return;

            _logoScale = new ScaleTransform(1, 1);

            // Preserve the -34 translate the XAML sets; order matters, scale first so
            // the squash happens about the mark's own centre and not the dial's.
            var group = new TransformGroup();
            group.Children.Add(_logoScale);
            group.Children.Add(new TranslateTransform(0, -34));
            logo.RenderTransform = group;

            _logoLastTick = DateTime.UtcNow;
            _logoSpinTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _logoSpinTimer.Tick += (_, _) => OnLogoSpinTick(viewModel);
            _logoSpinTimer.Start();
        }

        private void OnLogoSpinTick(VaultUnlockViewModel viewModel)
        {
            var now = DateTime.UtcNow;
            var dt = (now - _logoLastTick).TotalSeconds;
            _logoLastTick = now;

            if (_logoSettling)
            {
                var t = Math.Clamp((now - _logoSettleStart).TotalMilliseconds / LogoSettleDuration.TotalMilliseconds, 0, 1);

                // Back-out easing overshoots the target then returns to it. That
                // overshoot-and-return IS the click: the mark turns a few degrees past
                // square-on and rocks back, the way a mechanism drops into a detent.
                const double overshoot = 1.9;
                var c = overshoot + 1;
                var eased = 1 + (c * Math.Pow(t - 1, 3)) + (overshoot * Math.Pow(t - 1, 2));

                _logoAngle = _logoSettleFrom + ((_logoSettleTo - _logoSettleFrom) * eased);
                ApplyLogoAngle();

                if (t >= 1)
                {
                    _logoAngle = _logoSettleTo;
                    ApplyLogoAngle();
                    StopLogoSpin();
                }
                return;
            }

            // Still working: keep turning at a constant slow rate.
            if (viewModel.DisplayProgress < 99.5)
            {
                _logoAngle += LogoSpinRate * dt;
                ApplyLogoAngle();
                return;
            }

            // Progress has arrived. Settle to the next square-on position — a whole
            // number of turns ahead of where we are, so it always finishes facing
            // forward rather than stopping mid-flip.
            _logoSettling = true;
            _logoSettleStart = now;
            _logoSettleFrom = _logoAngle;

            var turns = Math.Ceiling(_logoAngle / (Math.PI * 2));
            var target = turns * Math.PI * 2;

            // Guarantee at least a part-turn of deceleration; landing with nowhere to
            // travel would make the click look like a glitch rather than an arrival.
            if (target - _logoAngle < Math.PI * 0.6) target += Math.PI * 2;
            _logoSettleTo = target;
        }

        private void ApplyLogoAngle()
        {
            if (_logoScale == null) return;

            var face = Math.Cos(_logoAngle);
            _logoScale.ScaleX = face;

            // Edge-on, a real object catches almost no light. Dimming as the face
            // narrows is what stops the squash reading as a flat horizontal stretch.
            var logo = this.FindControl<Image>("CentreLogoKey");
            if (logo != null)
                logo.Opacity = 0.68 + (0.32 * Math.Abs(face));
        }

        private void StopLogoSpin()
        {
            _logoSpinTimer?.Stop();
            _logoSpinTimer = null;
            _logoSettling = false;

            if (_logoScale != null) _logoScale.ScaleX = 1;
            var logo = this.FindControl<Image>("CentreLogoKey");
            if (logo != null) logo.Opacity = 1;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopLogoSpin();
            base.OnClosed(e);
        }

        public VaultUnlockWindow(VaultUnlockViewModel viewModel) : this()
        {
            Serilog.Log.Information("[VaultUnlockWindow] this() returned — setting DataContext");
            DataContext = viewModel;
            Serilog.Log.Information("[VaultUnlockWindow] DataContext set — SetOwnerWindow");
            viewModel.SetOwnerWindow(this);
            Serilog.Log.Information("[VaultUnlockWindow] wiring Opened event");

            Opened += async (s, e) =>
            {
                try
                {
                    StartLogoSpin(viewModel);
                    Serilog.Log.Information("[VaultUnlockWindow] Opened — starting UnlockVaultAsync");
                    await viewModel.UnlockVaultAsync();
                    Serilog.Log.Information("[VaultUnlockWindow] UnlockVaultAsync returned");
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "[VaultUnlockWindow] UnlockVaultAsync threw unhandled");
                }
            };
            Serilog.Log.Information("[VaultUnlockWindow] constructor complete");
        }
    }
}

