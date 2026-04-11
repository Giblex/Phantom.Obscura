using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhantomVault.UI.Controls;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views;

public partial class VaultUnlockPreferenceDialog : ThemeAwareWindow
{
    private string? _selectedPreference;
    private Border? _selectedCard;

    public string? SelectedPreference => _selectedPreference;

    public VaultUnlockPreferenceDialog()
    {
        InitializeComponent();
        ThemeScope.SetIsThemed(this, true);
    }

    private void PinCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectOption("Pin", PinCard, PinCheckMark, PinCheck, Color.Parse("#6BB3AE"));
    }

    private void HelloCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectOption("WindowsHello", HelloCard, HelloCheckMark, HelloCheck, Color.Parse("#8FB5DF"));
    }

    private void AutoCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectOption("Automatic", AutoCard, AutoCheckMark, AutoCheck, Color.Parse("#B0BEC5"));
    }

    private void SelectOption(string preference, Border card, TextBlock checkMark, Border checkBorder, Color accentColor)
    {
        // Deselect previous
        if (_selectedCard != null)
        {
            _selectedCard.Classes.Remove("selected");
        }

        // Reset all checkmarks
        PinCheckMark.IsVisible = false;
        HelloCheckMark.IsVisible = false;
        AutoCheckMark.IsVisible = false;

        PinCheck.Background = Brushes.Transparent;
        HelloCheck.Background = Brushes.Transparent;
        AutoCheck.Background = Brushes.Transparent;

        // Select new
        _selectedPreference = preference;
        _selectedCard = card;
        card.Classes.Add("selected");
        checkMark.IsVisible = true;
        checkBorder.Background = new SolidColorBrush(accentColor, 0.15);

        ConfirmButton.IsEnabled = true;
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(_selectedPreference);
    }
}
