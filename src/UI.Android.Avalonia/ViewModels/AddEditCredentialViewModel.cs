using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Android port of the desktop AddEditCredentialWindow (Phase 3e). The desktop
/// equivalent (Views/AddEditCredentialWindow.axaml, ~845 LOC) hosts a
/// type-aware editor — password, card, identity, API key, Wi-Fi, contact, bank,
/// PIN, etc. — each as a templated section. This first cut implements the
/// Login type only; additional types will land as templated branches keyed off
/// <see cref="CredentialType"/>.
///
/// Save / Cancel currently just navigate back via <see cref="ShellViewModel"/>;
/// the real persistence path lands together with the Core data-binding work.
/// </summary>
public sealed partial class AddEditCredentialViewModel : ObservableObject
{
    [ObservableProperty] private string _credentialType = "login";
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private bool _isPasswordVisible;
    [ObservableProperty] private string _status = string.Empty;

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "Title is required.";
            return;
        }

        Status = "Saved (preview — vault binding pending).";
        ShellViewModel.Current?.GoBackCommand.Execute(null);
    }

    [RelayCommand]
    private void Cancel() => ShellViewModel.Current?.GoBackCommand.Execute(null);
}
