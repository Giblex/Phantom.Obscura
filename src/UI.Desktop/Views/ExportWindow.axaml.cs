using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.ViewModels;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Security;
using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Views
{
    public partial class ExportWindow : ThemeAwareWindow
    {
        public ExportWindow()
        {
            InitializeComponent();
        }

        public ExportWindow(List<Credential> credentials) : this()
        {
            // The export guard enforces the exports-per-hour cooldown and raises a
            // threat event on export floods. It is resolved here because this is the
            // only construction site for ExportViewModel — leaving it null silently
            // disables both the cooldown and the DefenceEngine notification.
            var exportGuard = (Avalonia.Application.Current as App)?.Services?.GetService<IExportGuard>();
            var viewModel = new ExportViewModel(credentials, exportGuard);
            DataContext = viewModel;
            viewModel.SetOwner(this);
            viewModel.CloseRequested += (s, e) => Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

