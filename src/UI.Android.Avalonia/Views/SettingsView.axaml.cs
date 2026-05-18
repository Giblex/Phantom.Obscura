using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OpenThemeSettings_Click(object? sender, RoutedEventArgs e)
        => ShellViewModel.Current?.NavigateThemeSettings();
}
