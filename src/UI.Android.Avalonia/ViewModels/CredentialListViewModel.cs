using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels;

public sealed partial class CredentialListViewModel : ObservableObject
{
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "all";
    [ObservableProperty] private string _emptyHint = "No credentials yet. Tap + to add one.";

    public ObservableCollection<CredentialListItem> Items { get; } = new();

    public System.Collections.Generic.IEnumerable<CredentialListItem> FilteredItems
    {
        get
        {
            var q = (FilterText ?? string.Empty).Trim();
            return Items.Where(i =>
                (SelectedCategory == "all" || string.Equals(i.CategoryKey, SelectedCategory, System.StringComparison.OrdinalIgnoreCase)) &&
                (q.Length == 0 ||
                 i.Title.Contains(q, System.StringComparison.OrdinalIgnoreCase) ||
                 i.Username.Contains(q, System.StringComparison.OrdinalIgnoreCase) ||
                 i.Url.Contains(q, System.StringComparison.OrdinalIgnoreCase)));
        }
    }

    public bool HasItems => Items.Count > 0;

    partial void OnFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredItems));
    partial void OnSelectedCategoryChanged(string value) => OnPropertyChanged(nameof(FilteredItems));

    [RelayCommand]
    private void Add() => ShellViewModel.Current?.NavigateAddEdit();

    [RelayCommand]
    private void OpenDetail(CredentialListItem? item)
    {
        if (item == null) return;

        ShellViewModel.Current?.NavigateAddEdit();
    }
}

public sealed partial class CredentialListItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _categoryKey = "logins";
    [ObservableProperty] private string _glyph = "🔐";
    [ObservableProperty] private IBrush _accentBrush = Brushes.SteelBlue;
}

