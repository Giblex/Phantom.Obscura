using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels;

public class CategoryLandingViewModel : ReactiveObject
{
    private readonly VaultService _vaultService;
    private ObservableCollection<CategoryTileViewModel> _categoryTiles = new();

    public CategoryLandingViewModel(VaultService vaultService)
    {
        _vaultService = vaultService;
        
        NavigateToCategoryCommand = ReactiveCommand.Create<string>(NavigateToCategory);
        
        // Initialize with sample counts - will be updated when vault loads
        LoadCategories();
    }

    public ObservableCollection<CategoryTileViewModel> CategoryTiles
    {
        get => _categoryTiles;
        set => this.RaiseAndSetIfChanged(ref _categoryTiles, value);
    }

    public ReactiveCommand<string, Unit> NavigateToCategoryCommand { get; }

    private void LoadCategories()
    {
        // Initialize tiles with zero counts - VaultViewModel can update these later
        CategoryTiles = new ObservableCollection<CategoryTileViewModel>
        {
            new CategoryTileViewModel
            {
                Name = "Dashboard",
                Icon = "📊",
                Count = 0,
                Color = "#00D9FF",
                FilterType = "Dashboard"
            },
            new CategoryTileViewModel
            {
                Name = "All",
                Icon = "🔑",
                Count = 0,
                Color = "#007AFF",
                FilterType = "All"
            },
            new CategoryTileViewModel
            {
                Name = "Passwords",
                Icon = "🔐",
                Count = 0,
                Color = "#5856D6",
                FilterType = "Password"
            },
            new CategoryTileViewModel
            {
                Name = "Credit Cards",
                Icon = "💳",
                Count = 0,
                Color = "#FF9500",
                FilterType = "CreditCard"
            },
            new CategoryTileViewModel
            {
                Name = "Identities",
                Icon = "👤",
                Count = 0,
                Color = "#FF2D55",
                FilterType = "Identity"
            },
            new CategoryTileViewModel
            {
                Name = "Passkeys",
                Icon = "🔐",
                Count = 0,
                Color = "#32D74B",
                FilterType = "Passkey"
            },
            new CategoryTileViewModel
            {
                Name = "TOTP Codes",
                Icon = "⏱",
                Count = 0,
                Color = "#FFD60A",
                FilterType = "TOTP"
            },
            new CategoryTileViewModel
            {
                Name = "Favorites",
                Icon = "⭐",
                Count = 0,
                Color = "#FF9F0A",
                FilterType = "Favorites"
            }
        };
    }

    public void UpdateCounts(int total, int passwords, int creditCards, int identities, int passkeys, int totp, int favorites)
    {
        if (CategoryTiles.Count >= 8)
        {
            // CategoryTiles[0] is Dashboard, no count needed
            CategoryTiles[1].Count = total;  // All
            CategoryTiles[2].Count = passwords;
            CategoryTiles[3].Count = creditCards;
            CategoryTiles[4].Count = identities;
            CategoryTiles[5].Count = passkeys;
            CategoryTiles[6].Count = totp;
            CategoryTiles[7].Count = favorites;
        }
    }

    private void NavigateToCategory(string filterType)
    {
        // This will be handled by the main window to navigate to vault view with filter
        var message = new NavigateToVaultWithFilterMessage(filterType);
        MessageBus.Current.SendMessage(message);
    }
}

public class CategoryTileViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private string _icon = string.Empty;
    private int _count;
    private string _color = string.Empty;
    private string _filterType = string.Empty;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Icon
    {
        get => _icon;
        set => this.RaiseAndSetIfChanged(ref _icon, value);
    }

    public int Count
    {
        get => _count;
        set => this.RaiseAndSetIfChanged(ref _count, value);
    }

    public string Color
    {
        get => _color;
        set => this.RaiseAndSetIfChanged(ref _color, value);
    }

    public string FilterType
    {
        get => _filterType;
        set => this.RaiseAndSetIfChanged(ref _filterType, value);
    }
}

public record NavigateToVaultWithFilterMessage(string FilterType);
