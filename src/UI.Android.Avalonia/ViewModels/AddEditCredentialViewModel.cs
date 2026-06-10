using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels;

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

