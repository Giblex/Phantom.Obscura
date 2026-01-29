using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class UsbSetupWindow : Window
    {
        public UsbSetupWindow()
        {
            InitializeComponent();
            // Exclude from runtime theme switching - setup workflow remains unchanged
            ThemeScope.SetIsThemed(this, false);
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}