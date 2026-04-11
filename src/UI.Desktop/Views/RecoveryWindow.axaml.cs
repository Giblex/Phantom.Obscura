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
            DataContextChanged += RecoveryWindow_DataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void CloseWindow_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RecoveryWindow_DataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is RecoveryViewModel vm)
            {
                vm.CloseRequested -= RecoveryViewModel_CloseRequested;
                vm.CloseRequested += RecoveryViewModel_CloseRequested;
            }
        }

        private void RecoveryViewModel_CloseRequested(object? sender, EventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is RecoveryViewModel vm)
            {
                vm.CloseRequested -= RecoveryViewModel_CloseRequested;
            }

            base.OnClosed(e);

            // Dispose the ViewModel
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
