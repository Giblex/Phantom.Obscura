using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

/// <summary>
/// Android adapter for the desktop's <c>WelcomePage</c>. The desktop counterpart
/// inherits <c>ThemeAwareWindow</c> and carries 800+ LOC of pulse-ring + trace
/// animation logic. That harness is window-scoped and will be reintroduced
/// alongside the AOT pass in a later phase; for now this code-behind wires the
/// click handlers into the in-process <see cref="ShellViewModel"/> navigator.
/// </summary>
public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();
        // Bridge VM commands → shell navigation. The desktop equivalent uses
        // ReactiveUI Interaction handlers wired in App.axaml.cs; we keep it
        // explicit here so the wiring is easy to follow.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WelcomePageViewModel vm)
            {
                vm.RequestUnlock           += () => ShellViewModel.Current?.NavigateUnlock();
                vm.RequestDashboard        += () => ShellViewModel.Current?.NavigateDashboard();
                vm.RequestDefaultSetup     += () => ShellViewModel.Current?.NavigateAddEdit();   // Get-Started → AddEdit for demo
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
