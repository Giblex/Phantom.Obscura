using System;
using System.ComponentModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomVault.UI.ViewModels;
using Path = Avalonia.Controls.Shapes.Path;

namespace PhantomVault.UI.Views.Overlays
{
    public partial class SettingsPanelOverlay : UserControl
    {
        private VaultViewModel? _vault;

        // Offline icon state: the last state shown, and a run counter so a sequence that is
        // interrupted (the switch flipped back mid-animation) stops instead of finishing late.
        private bool? _offlineShown;
        private int _offlineRun;

        public SettingsPanelOverlay()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => WireVault();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnBackgroundClicked(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseSettingsPanelCommand.Execute(Unit.Default).Subscribe();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_vault != null) _vault.PropertyChanged -= OnVaultPropertyChanged;
            _vault = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            WireVault();
        }

        private void WireVault()
        {
            if (_vault != null) _vault.PropertyChanged -= OnVaultPropertyChanged;
            _vault = DataContext as VaultViewModel;
            if (_vault == null) return;

            _vault.PropertyChanged += OnVaultPropertyChanged;
            // Opening Settings while already offline shows the green shield straight away,
            // without replaying the tick.
            ShowOfflineIconInstant(_vault.IsOfflineConfirmed);
        }

        private void OnVaultPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(VaultViewModel.IsOfflineConfirmed) || _vault == null) return;
            var confirmed = _vault.IsOfflineConfirmed;
            Dispatcher.UIThread.Post(() => _ = PlayOfflineIconAsync(confirmed));
        }

        // ── Offline icon ────────────────────────────────────────────────────────────────

        private (Control? Off, Control? On, Control? Tick, Path? TickPath) OfflineParts() => (
            this.FindControl<Control>("OfflineShieldOff"),
            this.FindControl<Control>("OfflineShieldOn"),
            this.FindControl<Control>("OfflineTick"),
            this.FindControl<Path>("OfflineTickPath"));

        /// <summary>Sets a value with a timed ease (a transition for just this change).</summary>
        private static void Tween(Animatable target, AvaloniaProperty<double> property, double to, int milliseconds, Easing? easing = null)
        {
            target.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = property,
                    Duration = TimeSpan.FromMilliseconds(milliseconds),
                    Easing = easing ?? new CubicEaseInOut()
                }
            };
            target.SetValue(property, to);
        }

        /// <summary>Sets a value immediately, with no transition.</summary>
        private static void Snap(Animatable target, AvaloniaProperty<double> property, double to)
        {
            target.Transitions = null;
            target.SetValue(property, to);
        }

        private void ShowOfflineIconInstant(bool offline)
        {
            var (off, on, tick, path) = OfflineParts();
            if (off == null || on == null || tick == null || path == null) return;

            _offlineRun++;
            _offlineShown = offline;
            Snap(off, OpacityProperty, offline ? 0 : 1);
            Snap(on, OpacityProperty, offline ? 1 : 0);
            Snap(tick, OpacityProperty, 0);
            Snap(path, Shape.StrokeDashOffsetProperty, 10);
        }

        /// <summary>
        /// Going offline: the grey shield fades, a green tick draws itself, holds, then dissolves
        /// into a green shield. Coming back online: the green shield fades back to grey.
        /// </summary>
        private async Task PlayOfflineIconAsync(bool offline)
        {
            if (_offlineShown == offline) return;
            _offlineShown = offline;

            var (off, on, tick, path) = OfflineParts();
            if (off == null || on == null || tick == null || path == null) return;

            var run = ++_offlineRun;
            bool Cancelled() => run != _offlineRun;

            if (!offline)
            {
                Tween(tick, OpacityProperty, 0, 250);
                Tween(on, OpacityProperty, 0, 700);
                Tween(off, OpacityProperty, 1, 900);
                return;
            }

            // 1. The grey shield steps aside.
            Tween(on, OpacityProperty, 0, 200);
            Tween(off, OpacityProperty, 0, 300);
            await Task.Delay(260);
            if (Cancelled()) return;

            // 2. The tick draws itself, stroke by stroke.
            Snap(path, Shape.StrokeDashOffsetProperty, 10);
            Snap(tick, OpacityProperty, 1);
            Tween(path, Shape.StrokeDashOffsetProperty, 0, 650, new CubicEaseOut());
            await Task.Delay(650 + 450); // draw, then hold
            if (Cancelled()) return;

            // 3. It dissolves into the shield, now green.
            Tween(tick, OpacityProperty, 0, 800);
            Tween(on, OpacityProperty, 1, 900);
            await Task.Delay(900);
            if (Cancelled()) return;

            // Ready to draw again next time.
            Snap(path, Shape.StrokeDashOffsetProperty, 10);
        }
    }
}
