using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Android.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class VaultViewModel : BaseViewModel
    {
        private readonly MobileVaultService _vaultService;
        private List<Credential> _allCredentials = new();
        private List<Credential> _trashedCredentials = new();
        private VaultDatabase? _database;
        private string _masterPassword = string.Empty;

        [ObservableProperty] private ObservableCollection<Credential> _credentials = new();
        [ObservableProperty] private ObservableCollection<Credential> _trashedItems = new();
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _vaultName = "Phantom Vault";
        [ObservableProperty] private bool _isVaultOpen;
        [ObservableProperty] private Credential? _selectedCredential;
        [ObservableProperty] private bool _showFavoritesOnly;
        [ObservableProperty] private EntryType? _activeTypeFilter;

        /// <summary>All distinct categories (Group) across credentials.</summary>
        public ObservableCollection<string> Categories { get; } = new();

        public VaultViewModel(MobileVaultService vaultService) => _vaultService = vaultService;

        public void SetDatabase(VaultDatabase database, string masterPassword)
        {
            _database = database;
            _masterPassword = masterPassword;
            LoadCredentials(database);
        }

        public void LoadCredentials(VaultDatabase database)
        {
            _allCredentials.Clear();
            _trashedCredentials.Clear();
            if (database.Groups is not null)
                foreach (var g in database.Groups)
                    if (g.Entries is not null)
                        _allCredentials.AddRange(g.Entries);

            VaultName = database.VaultName;
            IsVaultOpen = true;
            RefreshCategories();
            ApplySearch();
            RefreshTrash();
        }

        public void ClearVault()
        {
            _allCredentials.Clear();
            _trashedCredentials.Clear();
            Credentials.Clear();
            TrashedItems.Clear();
            Categories.Clear();
            IsVaultOpen = false;
            SelectedCredential = null;
            _database = null;
            _masterPassword = string.Empty;
        }

        public async void UpsertCredential(Credential credential)
        {
            var existing = _allCredentials.Find(c => c.Id == credential.Id);
            if (existing is not null)
                _allCredentials[_allCredentials.IndexOf(existing)] = credential;
            else
                _allCredentials.Add(credential);

            RefreshCategories();
            ApplySearch();
            await PersistAsync();
        }

        public void AddCredentialFromImport(Credential credential)
        {
            _allCredentials.Add(credential);
            RefreshCategories();
            ApplySearch();
            _ = PersistAsync();
        }

        partial void OnSearchQueryChanged(string value) => ApplySearch();
        partial void OnShowFavoritesOnlyChanged(bool value) => ApplySearch();
        partial void OnActiveTypeFilterChanged(EntryType? value) => ApplySearch();

        private void ApplySearch()
        {
            var q = SearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;

            var filtered = _allCredentials.AsEnumerable();

            if (!string.IsNullOrEmpty(q))
                filtered = filtered.Where(c =>
                    (c.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                    (c.Username?.ToLowerInvariant().Contains(q) ?? false) ||
                    (c.Url?.ToLowerInvariant().Contains(q) ?? false) ||
                    (c.Group?.ToLowerInvariant().Contains(q) ?? false));

            if (ShowFavoritesOnly)
                filtered = filtered.Where(c => c.IsFavorite);

            if (ActiveTypeFilter.HasValue)
                filtered = filtered.Where(c => c.EntryType == ActiveTypeFilter.Value);

            Credentials.Clear();
            foreach (var cred in filtered.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.Title))
                Credentials.Add(cred);
        }

        private void RefreshCategories()
        {
            Categories.Clear();
            foreach (var cat in _allCredentials
                .Select(c => c.Group ?? string.Empty)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()
                .OrderBy(g => g))
                Categories.Add(cat);
        }

        private void RefreshTrash()
        {
            TrashedItems.Clear();
            foreach (var c in _trashedCredentials.OrderBy(c => c.Title))
                TrashedItems.Add(c);
        }

        private async Task PersistAsync()
        {
            if (_database is null || string.IsNullOrEmpty(_masterPassword)) return;

            if (_database.Groups is null || _database.Groups.Count == 0)
                _database.Groups = new List<VaultGroup>
                {
                    new() { Id = System.Guid.NewGuid().ToString(), Name = "General", Entries = new List<Credential>() }
                };

            _database.Groups[0].Entries = new List<Credential>(_allCredentials);
            await RunSafeAsync(async () => await _vaultService.SaveVaultAsync(_database, _masterPassword));
        }

        // ── Commands ─────────────────────────────────────────────────────────

        [RelayCommand] private void ClearSearch() => SearchQuery = string.Empty;

        [RelayCommand] private void SelectCredential(Credential? c) => SelectedCredential = c;

        [RelayCommand]
        private void ToggleFavorite(Credential? c)
        {
            if (c is null) return;
            c.IsFavorite = !c.IsFavorite;
            ApplySearch();
            _ = PersistAsync();
        }

        [RelayCommand]
        private void ToggleFavoritesFilter() => ShowFavoritesOnly = !ShowFavoritesOnly;

        [RelayCommand]
        private void SetTypeFilter(object? parameter)
        {
            EntryType? type = null;
            if (parameter is string s && int.TryParse(s, out var i))
                type = (EntryType)i;
            else if (parameter is EntryType et)
                type = et;
            ActiveTypeFilter = (ActiveTypeFilter.HasValue && (int?)ActiveTypeFilter == (int?)type) ? null : type;
        }

        [RelayCommand]
        private void ClearFilters()
        {
            ShowFavoritesOnly = false;
            ActiveTypeFilter = null;
        }

        /// <summary>Moves credential to trash (soft delete). Persists vault.</summary>
        [RelayCommand]
        private void DeleteCredential(Credential? c)
        {
            if (c is null) return;
            _allCredentials.Remove(c);
            _trashedCredentials.Add(c);
            Credentials.Remove(c);
            if (SelectedCredential == c) SelectedCredential = null;
            RefreshTrash();
            _ = PersistAsync();
        }

        /// <summary>Restores a trashed credential back to the vault.</summary>
        [RelayCommand]
        private void RestoreCredential(Credential? c)
        {
            if (c is null) return;
            _trashedCredentials.Remove(c);
            _allCredentials.Add(c);
            RefreshTrash();
            ApplySearch();
            _ = PersistAsync();
        }

        /// <summary>Permanently deletes a trashed credential — no recovery.</summary>
        [RelayCommand]
        private void PermanentDelete(Credential? c)
        {
            if (c is null) return;
            _trashedCredentials.Remove(c);
            TrashedItems.Remove(c);
            _ = PersistAsync();
        }

        /// <summary>Permanently deletes ALL trashed credentials.</summary>
        [RelayCommand]
        private void PurgeTrash()
        {
            _trashedCredentials.Clear();
            TrashedItems.Clear();
            _ = PersistAsync();
        }

        [RelayCommand]
        private async Task AddCredentialAsync() => await Shell.Current.GoToAsync("edit");
    }
}

