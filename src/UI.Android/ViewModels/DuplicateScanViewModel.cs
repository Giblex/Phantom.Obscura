using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.ViewModels
{
    public sealed class DuplicateGroup
    {
        public string Password { get; init; } = string.Empty;
        public List<Credential> Credentials { get; init; } = new();
        public string Summary => $"{Credentials.Count} credentials share this password";
    }

    public sealed partial class DuplicateScanViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vaultVm;

        [ObservableProperty] private ObservableCollection<DuplicateGroup> _duplicates = new();
        [ObservableProperty] private int _totalDuplicateCredentials;
        [ObservableProperty] private bool _hasResults;
        [ObservableProperty] private bool _scanned;

        public DuplicateScanViewModel(VaultViewModel vaultVm) => _vaultVm = vaultVm;

        [RelayCommand]
        private void Scan()
        {
            Duplicates.Clear();

            var groups = _vaultVm.Credentials
                .Where(c => !string.IsNullOrEmpty(c.Password))
                .GroupBy(c => c.Password)
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count());

            int count = 0;
            foreach (var g in groups)
            {
                var list = g.ToList();
                count += list.Count;
                Duplicates.Add(new DuplicateGroup { Password = g.Key, Credentials = list });
            }

            TotalDuplicateCredentials = count;
            HasResults = Duplicates.Count > 0;
            Scanned = true;
            StatusMessage = HasResults
                ? $"Found {Duplicates.Count} duplicate password group(s)"
                : "No duplicate passwords found ✓";
        }

        [RelayCommand]
        private async Task OpenCredentialAsync(Credential? c)
        {
            if (c is null) return;
            await Shell.Current.GoToAsync("detail", new Dictionary<string, object> { ["credential"] = c });
        }
    }
}
