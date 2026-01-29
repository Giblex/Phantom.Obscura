using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using System;

namespace PhantomVault.UI.Views
{
    public partial class InstallerWindow : ThemeAwareWindow
    {
        private InstallerViewModel? _viewModel;

        public InstallerWindow()
        {
            InitializeComponent();
            // Exclude from runtime theme switching - setup workflow remains unchanged
            ThemeScope.SetIsThemed(this, false);
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.NavigateToWelcome -= OnNavigateToWelcome;
                _viewModel.CloseRequested -= OnCloseRequested;
            }

            if (DataContext is InstallerViewModel viewModel)
            {
                viewModel.SetOwnerWindow(this);
                viewModel.NavigateToWelcome += OnNavigateToWelcome;
                viewModel.CloseRequested += OnCloseRequested;
                _viewModel = viewModel;
            }
            else
            {
                _viewModel = null;
            }
        }

        private void OnNavigateToWelcome(object? sender, EventArgs e)
        {
            var welcomePage = new WelcomePage();
            welcomePage.Show();
            Close();
        }

        private void OnCloseRequested(object? sender, EventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.NavigateToWelcome -= OnNavigateToWelcome;
                _viewModel.CloseRequested -= OnCloseRequested;
            }
            base.OnClosed(e);
        }
    }
}
