using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class QuickSetupWindow : ThemeAwareWindow
    {
        public QuickSetupWindow()
            : this(new SetupWizardViewModel())
        {
        }

        public QuickSetupWindow(SetupWizardViewModel viewModel)
        {
            ThemeScope.SetIsThemed(this, false);
            InitializeComponent();
            viewModel.SetOwnerWindow(this);
            DataContext = viewModel;
            Opened += OnOpened;
        }

        private async void OnOpened(object? sender, EventArgs e)
        {
            if (DataContext is SetupWizardViewModel vm)
            {
                await vm.InitializeQuickSetupAsync();
            }
        }

        private async void CreateVault_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not SetupWizardViewModel vm)
                return;

            if (!await vm.ValidateQuickSetupAsync())
                return;

            await vm.BeginProvisioningAsync();
        }

        private void Back_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
