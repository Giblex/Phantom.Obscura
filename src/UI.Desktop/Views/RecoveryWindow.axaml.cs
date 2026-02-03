using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Recovery wizard window for secure domain recovery.
    /// </summary>
    public partial class RecoveryWindow : Window
    {
        public RecoveryWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void CloseWindow_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BackToComplete_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is RecoveryViewModel vm)
            {
                // Navigate back to completion step
                // This is a simple workaround since we can't set CurrentStep directly
                // In a full implementation, add a GoBackCommand to the ViewModel
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose the ViewModel
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
