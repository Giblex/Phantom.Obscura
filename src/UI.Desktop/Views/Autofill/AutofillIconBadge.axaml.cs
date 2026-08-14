using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Autofill
{
    public partial class AutofillIconBadge : ThemeAwareWindow
    {
        private Button? _iconButton;
        private Grid? _root;
        private bool _closed;

        public event EventHandler? IconClicked;

        public AutofillIconBadge()
        {
            AvaloniaXamlLoader.Load(this);
            _iconButton = this.FindControl<Button>("IconButton");
            _root = this.Content as Grid;

            if (_iconButton != null)
                _iconButton.Click += (_, _) => IconClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Badge window size in logical units — must match Width/Height in the AXAML.</summary>
        internal const double BadgeSize = 60;

        /// <summary>
        /// Diameter of the visible ring inside the window — must match the inner
        /// Border in the AXAML. The overlay traces its handoff ring at exactly this
        /// size, so the drawn circle and the badge's own border are the same line.
        /// </summary>
        internal const double RingDiameter = 46;

        /// <summary>Gap between the badge and the left edge of the field, in logical units.</summary>
        internal const double FieldGap = 6;

        /// <summary>
        /// Places the badge immediately left of the field, vertically centred on it.
        ///
        /// <paramref name="fieldRect"/> is in PHYSICAL pixels (that is what
        /// PointToScreen and the Win32 APIs return), but Width/Height on the window
        /// are LOGICAL units. On any display that is not at 100% scaling the two
        /// disagree, which is why the badge previously landed nowhere near the box —
        /// or off-screen entirely, so it looked like it never appeared. Convert
        /// through the screen's scaling factor before positioning.
        /// </summary>
        public void PositionLeftOfField(PixelRect fieldRect)
        {
            var screen = Screens.ScreenFromPoint(new PixelPoint(fieldRect.X, fieldRect.Y))
                         ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1.0;

            int badgePx = (int)Math.Round(BadgeSize * scale);
            int gapPx = (int)Math.Round(FieldGap * scale);

            int x = fieldRect.X - badgePx - gapPx;
            int y = fieldRect.Y + (fieldRect.Height - badgePx) / 2;

            if (screen != null)
            {
                var b = screen.Bounds;
                // If there is no room on the left, tuck it just inside the field.
                if (x < b.X + 2) x = fieldRect.X + 2;
                y = Math.Max(b.Y + 2, Math.Min(b.Y + b.Height - badgePx - 2, y));
            }

            Position = new PixelPoint(x, y);
        }

        /// <summary>Appears at the given screen position (where the animation ball collapsed).</summary>
        public void PositionAt(PixelPoint pos) => Position = pos;

        /// <summary>
        /// Close guarded against re-entry — the fill callback, the menu's Closed
        /// handler and the auto-dismiss timer can all reach here.
        /// </summary>
        public void CloseOnce()
        {
            if (_closed) return;
            _closed = true;
            try { Close(); } catch (InvalidOperationException) { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            base.OnClosed(e);
        }

        /// <summary>
        /// Settles the badge in underneath the ring the overlay just drew.
        ///
        /// Starts at 0.62 rather than 0 so it grows into the existing ring instead of
        /// erupting from a point, and fades in over the first third of the movement.
        /// TransformOperations is required here — a plain ScaleTransform is not a type
        /// the AXAML TransformOperationsTransition can interpolate, so the animation
        /// would silently snap.
        /// </summary>
        public async Task PopInAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_root == null) return;
                _root.Opacity = 0;
                _root.RenderTransform = TransformOperations.Parse("scale(0.62)");
            });

            // One frame for the initial state to be applied before the transition
            // target is set, otherwise both land in the same layout pass and nothing
            // animates.
            await Task.Delay(20);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_root == null) return;
                _root.Opacity = 1;
                _root.RenderTransform = TransformOperations.Parse("scale(1)");
            });

            await Task.Delay(340); // let the spring finish before callers continue
        }
    }
}
