using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WelcomePageViewModel vm)
            {
                vm.RequestUnlock           += () => ShellViewModel.Current?.NavigateUnlock();
                vm.RequestDashboard        += () => ShellViewModel.Current?.NavigateDashboard();
                vm.RequestDefaultSetup     += () => ShellViewModel.Current?.NavigateDashboard();
                vm.RequestAdvancedSetup    += () => ShellViewModel.Current?.NavigateSettings();
                vm.RequestOpenExistingVault += () => ShellViewModel.Current?.NavigateUnlock();
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void DetectedVaultChoice_Click(object? sender, RoutedEventArgs e)
        => ShellViewModel.Current?.NavigateUnlock();

    private void About_Click(object? sender, RoutedEventArgs e)
        => ShellViewModel.Current?.NavigateSettings();

    private void Import_Click(object? sender, RoutedEventArgs e)
        => ShellViewModel.Current?.NavigateImportExport();
}

