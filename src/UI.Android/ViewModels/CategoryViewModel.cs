using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.ViewModels
{
    public sealed class CategoryGroup
    {
        public string Name { get; init; } = string.Empty;
        public List<Credential> Credentials { get; init; } = new();
        public int Count => Credentials.Count;
    }

    public sealed partial class CategoryViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vaultVm;

        [ObservableProperty] private ObservableCollection<CategoryGroup> _groups = new();
        [ObservableProperty] private string? _selectedCategory;

        public CategoryViewModel(VaultViewModel vaultVm)
        {
            _vaultVm = vaultVm;
            Refresh();
            // Refresh when vault changes
            _vaultVm.Credentials.CollectionChanged += (_, _) => Refresh();
        }

        private void Refresh()
        {
            Groups.Clear();

            // Ungrouped first
            var ungrouped = _vaultVm.Credentials
                .Where(c => string.IsNullOrWhiteSpace(c.Group))
                .ToList();
            if (ungrouped.Count > 0)
                Groups.Add(new CategoryGroup { Name = "Uncategorised", Credentials = ungrouped });

            // Named categories
            foreach (var g in _vaultVm.Credentials
                .Where(c => !string.IsNullOrWhiteSpace(c.Group))
                .GroupBy(c => c.Group)
                .OrderBy(g => g.Key))
            {
                Groups.Add(new CategoryGroup { Name = g.Key, Credentials = g.ToList() });
            }
        }

        [RelayCommand]
        private void SelectCategory(string? name)
        {
            SelectedCategory = name;
            // Apply type filter via VaultViewModel
            _vaultVm.SearchQuery = name is "Uncategorised" or null ? string.Empty : name;
        }

        [RelayCommand]
        private async Task OpenCredentialAsync(Credential? c)
        {
            if (c is null) return;
            await Shell.Current.GoToAsync("detail", new Dictionary<string, object> { ["credential"] = c });
        }
    }
}
