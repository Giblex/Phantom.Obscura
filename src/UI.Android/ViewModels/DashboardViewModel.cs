using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class DashboardViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vault;

        [ObservableProperty] private string _welcomeMessage = "Welcome back";
        [ObservableProperty] private string _vaultName = "Phantom Vault";
        [ObservableProperty] private int _totalCredentials;
        [ObservableProperty] private int _totalCategories;
        [ObservableProperty] private int _totpCount;
        [ObservableProperty] private int _favoriteCount;
        [ObservableProperty] private string _securityScoreEmoji = "🟡";
        [ObservableProperty] private string _securityStatusLabel = "Security check pending";
        [ObservableProperty] private string _securityStatusDetail = "Run a health scan to see your score";

        public DashboardViewModel(VaultViewModel vault) => _vault = vault;

        public void Refresh()
        {
            VaultName = _vault.VaultName;
            var hour = DateTime.Now.Hour;
            WelcomeMessage = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            TotalCredentials = _vault.Credentials.Count;
            var all = _vault.Credentials;
            TotalCategories = all.Select(c => c.Group).Distinct().Count(g => !string.IsNullOrEmpty(g));
            TotpCount = all.Count(c => c.EntryType == PhantomVault.Core.Models.EntryType.TotpGenerator);
            FavoriteCount = all.Count(c => c.IsFavorite);

            if (TotalCredentials == 0)
            {
                SecurityScoreEmoji = "⚪";
                SecurityStatusLabel = "Vault is empty";
                SecurityStatusDetail = "Add credentials to get started";
            }
            else
            {
                var score = (FavoriteCount > 0 ? 1 : 0) + (TotpCount > 0 ? 1 : 0);
                (SecurityScoreEmoji, SecurityStatusLabel, SecurityStatusDetail) = score >= 2
                    ? ("🟢", "Vault looks healthy", "TOTP and favourites are set up")
                    : score == 1
                        ? ("🟡", "Room for improvement", "Consider enabling TOTP for key accounts")
                        : ("🔴", "Security attention needed", "Run a password health scan");
            }
        }

        [RelayCommand] private async Task AddCredentialAsync() => await Shell.Current.GoToAsync("edit");
        [RelayCommand] private async Task OpenHealthAsync() => await Shell.Current.GoToAsync("password-health");
        [RelayCommand] private async Task OpenGeneratorAsync() => await Shell.Current.GoToAsync("password-generator");
        [RelayCommand] private async Task OpenImportAsync() => await Shell.Current.GoToAsync("importexport");
        [RelayCommand] private async Task OpenBackupAsync() => await Shell.Current.GoToAsync("backup-snapshots");
        [RelayCommand] private async Task OpenSecurityAsync() => await Shell.Current.GoToAsync("security-dashboard");
        [RelayCommand]
        private async Task LockVaultAsync()
        {
            _vault.ClearVault();
            await Shell.Current.GoToAsync("//unlock");
        }
    }
}
