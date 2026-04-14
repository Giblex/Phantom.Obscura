using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Stub for RecoveryPanel when PhantomRecovery is not available.
    /// Displays an informational message instead of an empty surface.
    /// </summary>
    public partial class RecoveryPanel : UserControl
    {
#pragma warning disable CS0067 // Event never used - stub implementation
        public event EventHandler? CloseRequested;
#pragma warning restore CS0067
        
        public RecoveryPanel()
        {
            Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 12,
                MaxWidth = 420,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Recovery Not Available",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "This recovery wizard needs an initialized recovery store from an unlocked vault. " +
                               "The standalone recovery surface is not ready from this Obscura entry point yet.",
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Opacity = 0.7
                    }
                }
            };
        }
    }
}
