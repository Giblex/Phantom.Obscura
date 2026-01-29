using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using PhantomVault.Core.Models;
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
            var viewModel = new ExportViewModel(credentials);
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
