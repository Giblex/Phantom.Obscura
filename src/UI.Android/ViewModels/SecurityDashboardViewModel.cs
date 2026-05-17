using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class SecurityDashboardViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vault;
        private readonly PasswordHealthService _healthService;

        [ObservableProperty] private string _overallScoreEmoji = "⚪";
        [ObservableProperty] private string _overallScoreLabel = "Not analysed yet";
        [ObservableProperty] private string _overallScoreDetail = "Tap Scan to run a security analysis";
        [ObservableProperty] private string _lastScanTime = string.Empty;
        [ObservableProperty] private int _weakCount;
        [ObservableProperty] private int _reusedCount;
        [ObservableProperty] private int _oldCount;
        [ObservableProperty] private int _totpCoveragePercent;
        [ObservableProperty] private double _totpCoverageRatio;

        public SecurityDashboardViewModel(VaultViewModel vault, PasswordHealthService healthService)
        {
            _vault = vault;
            _healthService = healthService;
        }

        public void Refresh()
        {
            var all = _vault.Credentials;
            if (all.Count == 0) return;
            var totpCount = all.Count(c => c.EntryType == EntryType.TotpGenerator);
            TotpCoveragePercent = all.Count > 0 ? (int)(totpCount * 100.0 / all.Count) : 0;
            TotpCoverageRatio = all.Count > 0 ? totpCount / (double)all.Count : 0;
        }

        [RelayCommand]
        private async Task ScanAsync()
        {
            await RunSafeAsync(async () =>
            {
                var credentials = _vault.Credentials
                    .Where(c => !string.IsNullOrEmpty(c.Password))
                    .ToList();

                var report = await _healthService.AnalyzeAsync(credentials);
                WeakCount = report.WeakCount;
                ReusedCount = report.ReusedCount;
                OldCount = report.OldCount;

                var total = WeakCount + ReusedCount + OldCount;
                (OverallScoreEmoji, OverallScoreLabel, OverallScoreDetail) = total == 0
                    ? ("🟢", "All clear!", "No weak, reused, or old passwords found")
                    : total <= 2
                        ? ("🟡", "Minor issues found", $"{total} password(s) need attention")
                        : ("🔴", "Security issues detected", $"{total} password(s) need attention");

                LastScanTime = $"Last scan: {DateTime.Now:HH:mm}";
                Refresh();
            }, "Scanning…");
        }

        [RelayCommand] private async Task OpenPasswordHealthAsync() => await Shell.Current.GoToAsync("password-health");
        [RelayCommand] private async Task OpenDuplicateScanAsync() => await Shell.Current.GoToAsync("duplicate-scan");
        [RelayCommand] private async Task OpenSecuritySettingsAsync() => await Shell.Current.GoToAsync("security");
        [RelayCommand] private async Task OpenTotpSettingsAsync() => await Shell.Current.GoToAsync("totp-settings");
    }
}
