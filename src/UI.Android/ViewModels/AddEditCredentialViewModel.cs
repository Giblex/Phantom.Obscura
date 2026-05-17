using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for add/edit credential form.
    /// Supports all EntryType variants (Password, TOTP, WiFi, CreditCard, etc.).
    /// </summary>
    public sealed partial class AddEditCredentialViewModel : BaseViewModel
    {
        [ObservableProperty]
        private Credential _credential = new();

        [ObservableProperty]
        private bool _isEditMode;

        [ObservableProperty]
        private EntryType _selectedEntryType = EntryType.Password;

        [ObservableProperty]
        private bool _passwordVisible;

        public event Action<Credential>? CredentialSaved;

        public List<EntryType> AllEntryTypes { get; } = Enum.GetValues<EntryType>().ToList();

        public void InitNew()
        {
            IsEditMode = false;
            Credential = new Credential
            {
                Id = Guid.NewGuid().ToString(),
                CreatedUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow
            };
            SelectedEntryType = EntryType.Password;
        }

        public void InitEdit(Credential existing)
        {
            IsEditMode = true;
            Credential = new Credential
            {
                Id = existing.Id,
                Title = existing.Title,
                Username = existing.Username,
                Password = existing.Password,
                Url = existing.Url,
                Notes = existing.Notes,
                EntryType = existing.EntryType,
                TotpSecret = existing.TotpSecret,
                CreatedUtc = existing.CreatedUtc,
                LastUpdatedUtc = existing.LastUpdatedUtc
            };
            SelectedEntryType = existing.EntryType;
        }

        partial void OnSelectedEntryTypeChanged(EntryType value)
            => Credential.EntryType = value;

        [RelayCommand]
        private void TogglePasswordVisibility() => PasswordVisible = !PasswordVisible;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            await RunSafeAsync(async () =>
            {
                await Task.CompletedTask; // placeholder for async persistence hook
                Credential.LastUpdatedUtc = DateTimeOffset.UtcNow;
                CredentialSaved?.Invoke(Credential);
            });
        }

        private bool CanSave()
            => !IsBusy && !string.IsNullOrWhiteSpace(Credential.Title);

        [RelayCommand]
        private async Task GeneratePasswordAsync()
        {
            // Simple secure random password; replace with PasswordHealthService if desired
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
            var password = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            Credential.Password = password;
            PasswordVisible = true;
            await Task.CompletedTask;
        }
    }
}
