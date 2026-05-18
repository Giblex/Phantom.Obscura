using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class ThemeSettingsView : UserControl
{
    public ThemeSettingsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Close_Click(object? sender, RoutedEventArgs e)
        => ShellViewModel.Current?.GoBackCommand.Execute(null);
}
