using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PhantomVault.UI.Desktop.Controls;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views.Details;

public partial class PasswordDetailView : UserControl
{
    public PasswordDetailView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void TotpCodeTile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border tile && tile.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = 0.97;
            st.ScaleY = 0.97;
        }
    }

    private void TotpCodeTile_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border tile && tile.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = 1.0;
            st.ScaleY = 1.0;
        }
    }

    // Username and password are CopyableFields now; only the TOTP tile copies from here. It uses
    // the same copy path and feedback: bounce straight away, "Copied" only once it landed.
    private async void CopyTotpCode_Tapped(object? sender, TappedEventArgs e)
    {
        // The vault (single card) or an account tile (shared card): copy that entry's code.
        if (DataContext is not IDetailHost host || host.SelectedCredential == null) return;

        var tile = sender as Control;
        if (tile != null) CopyFeedback.Bounce(tile);

        if (await host.CopyFieldValueAsync(host.SelectedCredential.CurrentTotpCode, "TOTP code") && tile != null)
            CopyFeedback.ShowCopied(tile);
    }
}
