using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PhantomVault.UI.Views
{
    public partial class DashboardView : UserControl
    {
        private TranslateTransform? _sheetTranslate;
        private bool _isDragging;
        private double _dragStartY;
        private double _dragStartTranslateY;
        private bool _isAnimating;
        private DateTime _dragStartTime;
        private double _lastDragY;
        private double _dragVelocity;
        private bool _grabWired;
        private ViewModels.VaultViewModel? _vaultVm;
        private bool _vmWired;

        public DashboardView()
        {
            InitializeComponent();
            AttachedToVisualTree += OnAttachedToVisualTree;
        }

        /// <summary>
        /// One-time setup: create the TranslateTransform, wire grab handle events,
        /// and subscribe to VaultViewModel.IsShowingDashboard changes.
        /// </summary>
        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            // Find the hosting "DashboardSheet" Panel in VaultWindow
            if (_sheetTranslate == null)
            {
                var sheetPanel = FindDashboardSheet();
                if (sheetPanel != null)
                {
                    _sheetTranslate = new TranslateTransform();
                    sheetPanel.RenderTransform = _sheetTranslate;
                    // Start off-screen above (behind header)
                    _sheetTranslate.Y = -1200;
                }
            }

            // Wire grab handle events (once)
            if (!_grabWired)
            {
                var grabHandle = this.FindControl<Border>("GrabHandle");
                if (grabHandle != null)
                {
                    grabHandle.PointerPressed += OnGrabPointerPressed;
                    grabHandle.PointerMoved += OnGrabPointerMoved;
                    grabHandle.PointerReleased += OnGrabPointerReleased;
                    grabHandle.PointerCaptureLost += OnGrabPointerCaptureLost;
                    _grabWired = true;
                }
            }

            // Subscribe to VaultViewModel.IsShowingDashboard property changes (once)
            if (!_vmWired)
            {
                _vaultVm = FindVaultViewModel();
                if (_vaultVm != null)
                {
                    _vaultVm.PropertyChanged += OnVaultVmPropertyChanged;
                    _vmWired = true;
                }
            }
        }

        /// <summary>
        /// Reacts to VaultViewModel.IsShowingDashboard changes — animates open/close.
        /// </summary>
        private void OnVaultVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModels.VaultViewModel.IsShowingDashboard)) return;
            if (_vaultVm == null || _sheetTranslate == null) return;

            if (_vaultVm.IsShowingDashboard)
            {
                // Dashboard opened — slide down from above
                var h = Bounds.Height > 0 ? Bounds.Height : 1200;
                _sheetTranslate.Y = -h;
                _isAnimating = false;
                Dispatcher.UIThread.Post(() => AnimateSheetTo(0, 420), DispatcherPriority.Loaded);
            }
            else
            {
                // Dashboard closed (e.g. sidebar button pressed) — glide up to close
                // Only animate if the sheet is reasonably visible (not already closed by drag)
                if (_sheetTranslate.Y > -10 && !_isAnimating)
                {
                    var h = Bounds.Height > 0 ? Bounds.Height : 1200;
                    Dispatcher.UIThread.Post(() => AnimateSheetTo(-h, 350), DispatcherPriority.Loaded);
                }
            }
        }

        private ViewModels.VaultViewModel? FindVaultViewModel()
        {
            Visual? parent = this.GetVisualParent();
            while (parent != null)
            {
                if (parent is Control c && c.DataContext is ViewModels.VaultViewModel vm)
                    return vm;
                parent = parent.GetVisualParent();
            }
            return null;
        }

        private Panel? FindDashboardSheet()
        {
            Visual? current = this.GetVisualParent();
            while (current != null)
            {
                if (current is Panel panel && panel.Name == "DashboardSheet")
                    return panel;
                current = current.GetVisualParent();
            }
            return null;
        }

        private void OnGrabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_sheetTranslate == null || _isAnimating) return;
            if (sender is not Border handle) return;

            _isDragging = true;
            // Use position relative to window root for stable coordinates —
            // GetPosition(this) drifts because 'this' moves with the TranslateTransform.
            var root = (Visual?)this.GetVisualRoot();
            _dragStartY = root != null ? e.GetPosition(root).Y : e.GetPosition(this).Y;
            _dragStartTranslateY = _sheetTranslate.Y;
            _dragStartTime = DateTime.UtcNow;
            _lastDragY = _dragStartY;
            _dragVelocity = 0;
            e.Pointer.Capture(handle);
            e.Handled = true;
        }

        private void OnGrabPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging || _sheetTranslate == null) return;

            var root = (Visual?)this.GetVisualRoot();
            var currentY = root != null ? e.GetPosition(root).Y : e.GetPosition(this).Y;
            var delta = currentY - _dragStartY;

            // Track velocity for fling gesture
            _dragVelocity = currentY - _lastDragY;
            _lastDragY = currentY;

            // Only allow dragging upward (negative Y = dismissing toward top)
            var newY = Math.Min(0, _dragStartTranslateY + delta);
            _sheetTranslate.Y = newY;
        }

        private void OnGrabPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            e.Pointer.Capture(null);
            SnapSheet();
        }

        private void OnGrabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            SnapSheet();
        }

        private void SnapSheet()
        {
            if (_sheetTranslate == null) return;

            var height = Bounds.Height;
            if (height <= 0) height = 800;

            // progress: 1 = fully open (Y=0), 0 = fully closed (Y=-height)
            var progress = 1.0 - Math.Abs(_sheetTranslate.Y) / height;

            // Fling detection: fast upward swipe dismisses regardless of position
            var isFling = _dragVelocity < -12;

            if (!isFling && progress > 0.45)
            {
                // Snap open with spring
                AnimateSheetTo(0, 420);
            }
            else
            {
                // Snap closed — animate up off screen then update ViewModel
                var closeDuration = isFling ? 280 : 350;
                AnimateSheetTo(-height, closeDuration, onComplete: () =>
                {
                    if (_vaultVm != null)
                        _vaultVm.IsShowingDashboard = false;
                });
            }
        }

        /// <summary>
        /// iOS-style spring ease: fast start, smooth deceleration with subtle overshoot.
        /// </summary>
        private static double SpringEase(double t)
        {
            return 1.0 - Math.Exp(-5.5 * t) * Math.Cos(1.8 * t);
        }

        private async void AnimateSheetTo(double targetY, double durationMs, Action? onComplete = null)
        {
            if (_sheetTranslate == null) return;
            _isAnimating = true;

            var startY = _sheetTranslate.Y;
            var distance = targetY - startY;
            if (Math.Abs(distance) < 1)
            {
                _sheetTranslate.Y = targetY;
                _isAnimating = false;
                onComplete?.Invoke();
                return;
            }

            var startTime = DateTime.UtcNow;

            while (true)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed >= durationMs)
                {
                    _sheetTranslate.Y = targetY;
                    break;
                }

                var t = elapsed / durationMs;
                var eased = SpringEase(t);
                _sheetTranslate.Y = startY + distance * eased;

                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }

            _isAnimating = false;
            onComplete?.Invoke();
        }
    }
}
