using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class CategoryManagerWindow : ThemeAwareWindow
    {
        public CategoryManagerWindow()
        {
            InitializeComponent();
            AttachViewModel(new CategoryManagerViewModel());
        }

        public CategoryManagerWindow(ManifestService manifestService, VaultManifest manifest, string manifestPath, string? passphrase = null, string? keyfilePath = null)
        {
            InitializeComponent();
            AttachViewModel(new CategoryManagerViewModel(manifestService, manifest, manifestPath, passphrase, keyfilePath));
        }

        public CategoryManagerWindow(ManifestService manifestService, VaultManifest manifest, string manifestPath, object vaultManager, string? passphrase = null, string? keyfilePath = null)
        {
            InitializeComponent();
            AttachViewModel(new CategoryManagerViewModel(manifestService, manifest, manifestPath, vaultManager, passphrase, keyfilePath));
        }

        public CategoryManagerWindow(object vaultManager)
        {
            InitializeComponent();
            AttachViewModel(new CategoryManagerViewModel(vaultManager));
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AttachViewModel(CategoryManagerViewModel viewModel)
        {
            if (DataContext is CategoryManagerViewModel existing)
            {
                existing.DismissRequested -= OnDismissRequested;
            }

            DataContext = viewModel;
            viewModel.CloseOwnerOnDismiss = true;
            viewModel.SetOwnerWindow(this);
            viewModel.DismissRequested += OnDismissRequested;
        }

        private void OnDismissRequested()
        {
            if (!IsVisible)
            {
                return;
            }

            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is CategoryManagerViewModel vm)
            {
                vm.DismissRequested -= OnDismissRequested;
            }

            base.OnClosed(e);
        }
    }
}

