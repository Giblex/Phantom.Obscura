using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomVault.UI.ViewModels.PhantomKey;

namespace PhantomVault.UI.Views.PhantomKey;

/// <summary>
/// Obscura's Unlock-with-PhantomKey dialog. Hosts the shared
/// <c>PhantomKeyUnlockControl</c>. Call <see cref="ShowAsync"/> from wherever
/// Obscura wants to expose the flow — settings screen, setup wizard step,
/// header launcher menu.
/// </summary>
public partial class PhantomKeyUnlockDialog : Window
{
    public PhantomKeyUnlockDialog()
    {
        InitializeComponent();
        DataContext = new PhantomKeyUnlockDialogViewModel();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Open the dialog modally over <paramref name="owner"/>. Returns the VM
    /// so callers can inspect <c>Unlocked</c> after the dialog closes.
    /// </summary>
    public static async Task<PhantomKeyUnlockDialogViewModel> ShowAsync(Window owner)
    {
        var dialog = new PhantomKeyUnlockDialog();
        await dialog.ShowDialog(owner);
        return (PhantomKeyUnlockDialogViewModel)dialog.DataContext!;
    }
}
