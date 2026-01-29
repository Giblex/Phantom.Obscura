using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class VeraCryptSetupWindow : ThemeAwareWindow
    {
        public VeraCryptSetupWindow()
        {
            InitializeComponent();
            // Exclude from runtime theme switching - setup workflow remains unchanged
            ThemeScope.SetIsThemed(this, false);

            // Set up navigation only when DataContext is assigned
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is VeraCryptSetupWindowViewModel viewModel)
            {
                // Set owner window for dialogs
                viewModel.SetOwnerWindow(this);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GoBack_Click(object? sender, RoutedEventArgs e)
        {
            // Return to WelcomePage
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Show();
                desktop.MainWindow?.Activate();
            }
            Close();
        }
    }
}
