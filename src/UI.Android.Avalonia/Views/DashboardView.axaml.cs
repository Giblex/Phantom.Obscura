using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OpenCategories_Click(object? sender, RoutedEventArgs e)  => ShellViewModel.Current?.NavigateCategories();
    private void AddCredential_Click(object? sender, RoutedEventArgs e)   => ShellViewModel.Current?.NavigateAddEdit();
    private void IconDownloader_Click(object? sender, RoutedEventArgs e)  => ShellViewModel.Current?.NavigateIconDownloader();
    private void Security_Click(object? sender, RoutedEventArgs e)        => ShellViewModel.Current?.NavigateSecurityDashboard();
    private void Theme_Click(object? sender, RoutedEventArgs e)           => ShellViewModel.Current?.NavigateThemeSettings();
    private void Settings_Click(object? sender, RoutedEventArgs e)        => ShellViewModel.Current?.NavigateSettings();
    private void Lock_Click(object? sender, RoutedEventArgs e)            => ShellViewModel.Current?.GoBackCommand.Execute(null);
}
