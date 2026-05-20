using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// In-window panel that surfaces the external PhantomRecovery launcher.
    /// PhantomRecovery runs as a separate process for isolation; this panel
    /// shows availability and offers explicit Launch / Close affordances.
    /// Launch is delegated through <see cref="LaunchRequested"/> so the host
    /// (VaultWindow / VaultViewModel) can resolve the recovery vault path
    /// before invoking <see cref="IntegratedRecoveryService.TryLaunch"/>.
    /// </summary>
    public partial class RecoveryPanel : UserControl
    {
        public event EventHandler? CloseRequested;
        public event EventHandler? LaunchRequested;

        public RecoveryPanel()
            : this(new IntegratedRecoveryService())
        {
        }

        public RecoveryPanel(IntegratedRecoveryService recoveryService)
        {
            if (recoveryService is null) throw new ArgumentNullException(nameof(recoveryService));

            var available = recoveryService.IsAvailable;

            var heading = new TextBlock
            {
                Text = available ? "PhantomRecovery" : "Recovery Not Available",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var status = new TextBlock
            {
                Text = recoveryService.AvailabilityMessage,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.75,
                MaxWidth = 420
            };

            var launchButton = new Button
            {
                Content = "Launch PhantomRecovery",
                HorizontalAlignment = HorizontalAlignment.Center,
                IsEnabled = available
            };
            launchButton.Click += (_, _) => LaunchRequested?.Invoke(this, EventArgs.Empty);

            var closeButton = new Button
            {
                Content = "Close",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

            Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 12,
                MaxWidth = 460,
                Children =
                {
                    heading,
                    status,
                    launchButton,
                    closeButton
                }
            };
        }
    }
}
