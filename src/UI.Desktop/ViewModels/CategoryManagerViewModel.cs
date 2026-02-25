using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive;
using ReactiveUI;
using PhantomVault.Core.Services;
using PhantomVault.Core.Models;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using PhantomVault.UI.Helpers;


namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Category Manager that allows users to create, edit, delete, and reorder
    /// credential categories. Supports two modes: standalone (with defaults) or manifest-backed
    /// (synced with vault.manifest file).
    /// </summary>
    public class CategoryManagerViewModel : ReactiveObject
    {
        private readonly ManifestService? _manifestService;
        private readonly string? _manifestPath;
        private readonly string? _passphrase;
        private readonly string? _keyfilePath;
        private Window? _ownerWindow;
        private object? _vaultManager; // typically VaultViewModel to perform credential updates
        private readonly PhantomVault.UI.Services.DialogService _dialogService = new PhantomVault.UI.Services.DialogService();
        private CancellationTokenSource? _persistDebounceCts;
        private IconManager? _iconManager;

        public event Action? DismissRequested;

        public bool CloseOwnerOnDismiss { get; set; } = true;

        public CategoryManagerViewModel()
        {
            Categories = new ObservableCollection<CategoryItem>();

            // Provide sensible defaults when no manifest is supplied so the
            // Category Manager isn't empty when opened standalone.
            Categories.Add(new CategoryItem { Name = "General", Icon = "/Assets/Visuals/Cat Icons/Bookmark/3914133 (1)_semi_dark_pastel_blue.png", Order = 0, TileColor = "#BFDBFE" });
            Categories.Add(new CategoryItem { Name = "Logins", Icon = "/Assets/Visuals/Cat Icons/Key/Key_golden_pastel_yellow.png", Order = 1, TileColor = "#FDE68A" });
            Categories.Add(new CategoryItem { Name = "Payment", Icon = "/Assets/Visuals/Cat Icons/Credit cards/Credit cards_teal.png", Order = 2, TileColor = "#A7F3D0" });

            InitializeCommands();
            HookCollectionEvents();
            AutoDetectTileColors();
        }

        /// <summary>
        /// Lightweight ctor that only syncs from the live vault UI (no manifest required).
        /// Ensures the manager shows exactly what the sidebar shows when no manifest is available.
        /// </summary>
        public CategoryManagerViewModel(object? vaultManager)
        {
            Categories = new ObservableCollection<CategoryItem>();
            _vaultManager = vaultManager;

            InitializeCommands();
            if (_vaultManager is VaultViewModel vm)
            {
                SyncFromVaultCategories(vm);
            }
            HookCollectionEvents();
            AutoDetectTileColors();
        }

        public CategoryManagerViewModel(ManifestService manifestService, VaultManifest manifest, string manifestPath, string? passphrase = null, string? keyfilePath = null)
        {
            _manifestService = manifestService;
            _manifestPath = manifestPath;
            _passphrase = passphrase;
            _keyfilePath = keyfilePath;
            Categories = new ObservableCollection<CategoryItem>();

            // Load categories from manifest if present; otherwise create defaults.
            if (manifest.Categories != null && manifest.Categories.Any())
            {
                foreach (var c in manifest.Categories.OrderBy(c => c.Order))
                {
                    Categories.Add(new CategoryItem { Name = c.Name, Icon = IconPathMigrator.Migrate(c.Icon), Order = c.Order, IsTrash = c.IsTrash, TileColor = c.TileColor });
                }
                if (!Categories.Any(c => string.Equals(c.Name, "Deleted", System.StringComparison.OrdinalIgnoreCase)))
                {
                    Categories.Add(new CategoryItem { Name = "Deleted", Icon = "/Assets/Visuals/Cat Icons/rubbish/rubbish_charcoal.png", Order = Categories.Count, IsTrash = true, TileColor = "#6B7280" });
                }
            }
            else
            {
                Categories.Add(new CategoryItem { Name = "General", Icon = "/Assets/Visuals/Cat Icons/Bookmark/3914133 (1)_semi_dark_pastel_blue.png", Order = 0, TileColor = "#BFDBFE" });
                Categories.Add(new CategoryItem { Name = "Logins", Icon = "/Assets/Visuals/Cat Icons/Key/Key_golden_pastel_yellow.png", Order = 1, TileColor = "#FDE68A" });
                Categories.Add(new CategoryItem { Name = "Payment", Icon = "/Assets/Visuals/Cat Icons/Credit cards/Credit cards_teal.png", Order = 2, TileColor = "#A7F3D0" });
                Categories.Add(new CategoryItem { Name = "Secure rubbish bin", Icon = "/Assets/Visuals/Cat Icons/rubbish/rubbish_charcoal.png", Order = 3, IsTrash = true, TileColor = "#6B7280" });
            }

            InitializeCommands();
            HookCollectionEvents();
            AutoDetectTileColors();
        }

        /// <summary>
        /// Alternate ctor that accepts a vault manager instance (usually VaultViewModel) to allow credential updates when categories change.
        /// </summary>
        public CategoryManagerViewModel(ManifestService manifestService, VaultManifest manifest, string manifestPath, object? vaultManager, string? passphrase = null, string? keyfilePath = null)
            : this(manifestService, manifest, manifestPath, passphrase, keyfilePath)
        {
            _vaultManager = vaultManager;

            // Ensure the dialog shows EXACTLY what the side panel shows by syncing from the vault UI
            // as the source of truth on open. This overrides any stale manifest categories so both
            // views match 1:1 in names, icons, and order.
            if (_vaultManager is VaultViewModel vm)
            {
                SyncFromVaultCategories(vm);
            }
        }

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        /// <summary>
        /// Get a safe owner window for dialogs. Returns the set owner or falls back to the application main window.
        /// Throws if no owner is available (should not happen in normal flows).
        /// </summary>
        private Window GetOwnerWindow()
        {
            if (_ownerWindow != null) return _ownerWindow;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                return desktop.MainWindow;
            // Fallback: create a temporary invisible window as owner (last resort to avoid null)
            // In practice, this should never be reached if SetOwnerWindow was called.
            return new Window { Opacity = 0, ShowInTaskbar = false };
        }

        /// <summary>
        /// Get or create an IconManager instance for icon selection.
        /// </summary>
        private IconManager GetIconManager()
        {
            if (_iconManager == null)
            {
                // Point to the application's Assets/Visuals folder
                var iconsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Visuals");
                _iconManager = new IconManager(iconsPath);
            }
            return _iconManager;
        }

        public ObservableCollection<CategoryItem> Categories { get; }

        // Commands
        public ReactiveCommand<Unit, Unit> AddCategoryCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> RemoveCategoryCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> MoveUpCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> MoveDownCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> SaveCategoryCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> MoveItemsCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> ManageSelectedItemsCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> PickIconCommand { get; private set; } = null!;
        public ReactiveCommand<CategoryItem, Unit> CycleColorCommand { get; private set; } = null!;
        public ReactiveCommand<PaletteSelection, Unit> ApplyColorCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> SaveAllCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ResetAllColorsCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ManageSelectedCategoriesCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> SelectAllCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> InvertSelectionCommand { get; private set; } = null!;
        public ReactiveCommand<string[], Unit> SelectByNamesCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> OpenIconLibraryCommand { get; private set; } = null!;

        public bool HasSelection => Categories.Any(c => c.IsSelected);

        private void InitializeCommands()
        {
            AddCategoryCommand = ReactiveCommand.CreateFromTask(AddCategoryAsync);
            RemoveCategoryCommand = ReactiveCommand.CreateFromTask<CategoryItem>(item => RemoveCategoryAsync(item, deleteContents: false));
            MoveUpCommand = ReactiveCommand.CreateFromTask<CategoryItem>(MoveUpAsync);
            MoveDownCommand = ReactiveCommand.CreateFromTask<CategoryItem>(MoveDownAsync);
            SaveCategoryCommand = ReactiveCommand.CreateFromTask<CategoryItem>(async _ => await PersistAsync());
            MoveItemsCommand = ReactiveCommand.CreateFromTask<CategoryItem>(MoveItemsAsync);
            ManageSelectedItemsCommand = ReactiveCommand.CreateFromTask<CategoryItem>(ManageSelectedItemsAsync);
            PickIconCommand = ReactiveCommand.CreateFromTask<CategoryItem>(PickIconAsync);
            CycleColorCommand = ReactiveCommand.Create<CategoryItem>(CycleColor);
            ApplyColorCommand = ReactiveCommand.Create<PaletteSelection>(ApplyColor);
            SaveAllCommand = ReactiveCommand.CreateFromTask(SaveAllAndCloseAsync);
            ResetAllColorsCommand = ReactiveCommand.CreateFromTask(ResetAllColorsAsync);
            ManageSelectedCategoriesCommand = ReactiveCommand.CreateFromTask(ManageSelectedCategoriesAsync);
            SelectAllCommand = ReactiveCommand.CreateFromTask(ToggleSelectAllAsync);
            ClearSelectionCommand = ReactiveCommand.CreateFromTask(ClearSelectionAsync);
            InvertSelectionCommand = ReactiveCommand.CreateFromTask(InvertSelectionAsync);
            SelectByNamesCommand = ReactiveCommand.CreateFromTask<string[]>(SelectByNamesAsync);
            CloseCommand = ReactiveCommand.Create(CloseWindow);
            OpenIconLibraryCommand = ReactiveCommand.CreateFromTask(OpenIconLibraryAsync);
        }

        /// <summary>
        /// Push the current category list to the vault UI immediately so changes are visible
        /// even if manifest persistence is deferred or fails.
        /// </summary>
        private void PublishToVaultManager()
        {
            if (_vaultManager is VaultViewModel vm)
            {
                var models = Categories
                    .Select(c => new CategoryModel
                    {
                        Name = c.Name,
                        Icon = c.Icon,
                        Order = c.Order,
                        IsTrash = c.IsTrash,
                        TileColor = c.TileColor
                    })
                    .ToList();
                if (Dispatcher.UIThread.CheckAccess())
                {
                    vm.UpdateCategoriesFromModels(models);
                }
                else
                {
                    Dispatcher.UIThread.Post(() => vm.UpdateCategoriesFromModels(models));
                }

                // Push color map to sidebar too
                var colorMap = Categories.ToDictionary(c => c.Name, c => c.TileColor, System.StringComparer.OrdinalIgnoreCase);
                if (Dispatcher.UIThread.CheckAccess())
                {
                    vm.UpdateCategoryColors(colorMap);
                }
                else
                {
                    Dispatcher.UIThread.Post(() => vm.UpdateCategoryColors(colorMap));
                }
            }
        }

        /// <summary>
        /// Replace the manager's working list with the categories currently visible in the vault side panel.
        /// Keeps order identical and infers the special Deleted/Trash flag by name.
        /// </summary>
        private void SyncFromVaultCategories(VaultViewModel vm)
        {
            Categories.Clear();
            int order = 0;
            foreach (var c in vm.Categories)
            {
                Categories.Add(new CategoryItem
                {
                    Name = c.Name,
                    Icon = IconPathMigrator.Migrate(c.Icon),
                    Order = order++,
                    IsTrash = string.Equals(c.Name, "Secure rubbish bin", System.StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(c.Name, "Deleted", System.StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(c.Name, "Trash", System.StringComparison.OrdinalIgnoreCase),
                    TileColor = (c as CategoryViewModel)?.TileColor,
                    Count = c.Count
                });
            }
            // Ensure at least one trash category exists; if none in UI, append Secure rubbish bin at end for safety
            if (!Categories.Any(ci => ci.IsTrash || string.Equals(ci.Name, "Secure rubbish bin", System.StringComparison.OrdinalIgnoreCase)))
            {
                Categories.Add(new CategoryItem { Name = "Secure rubbish bin", Icon = "/Assets/Visuals/Cat Icons/rubbish/rubbish_charcoal.png", Order = Categories.Count, IsTrash = true, Count = 0, TileColor = "#6B7280" });
            }
            // Normalize order values
            for (int i = 0; i < Categories.Count; i++) Categories[i].Order = i;
        }

        private async Task ToggleSelectAllAsync()
        {
            bool anyUnselected = Categories.Any(c => !c.IsSelected);
            foreach (var c in Categories)
            {
                c.IsSelected = anyUnselected;
            }
            // No need to await anything here, but keep signature async to match command pattern
            await Task.CompletedTask;
        }

        /// <summary>
        /// Clear all selections.
        /// </summary>
        private Task ClearSelectionAsync()
        {
            foreach (var c in Categories)
                c.IsSelected = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Invert current selection set.
        /// </summary>
        private Task InvertSelectionAsync()
        {
            foreach (var c in Categories)
                c.IsSelected = !c.IsSelected;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Select categories whose names match any of the provided names (case-insensitive).
        /// </summary>
        private async Task SelectByNamesAsync(string[] names)
        {
            if (names == null || names.Length == 0)
            {
                await Task.CompletedTask;
                return;
            }
            var set = new System.Collections.Generic.HashSet<string>(names, System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in Categories)
                c.IsSelected = set.Contains(c.Name);
            await Task.CompletedTask;
        }

        public async Task AddCategoryAsync()
        {
            int nextOrder = Categories.Any() ? Categories.Max(c => c.Order) + 1 : 0;
            
            // Generate unique name
            string baseName = "New Category";
            string uniqueName = baseName;
            int counter = 2;
            while (Categories.Any(c => string.Equals(c.Name, uniqueName, System.StringComparison.OrdinalIgnoreCase)))
            {
                uniqueName = $"{baseName} ({counter})";
                counter++;
            }
            
            // Load default color from preferences
            var settings = SettingsService.Load();
            var defaultColor = settings.DefaultCategoryColor;
            
            Debug.WriteLine($"[CATEGORY-MGR] AddCategory: Loading default color from settings: '{defaultColor ?? "null"}'");
            
            var item = new CategoryItem 
            { 
                Name = uniqueName, 
                Icon = "/Assets/Visuals/Cat Icons/Bookmark/3914133 (1)_purple.png", 
                Order = nextOrder,
                TileColor = defaultColor ?? "#E9D5FF" // Apply saved preference or default purple
            };
            Categories.Add(item);
            
            Debug.WriteLine($"[CATEGORY-MGR] AddCategory: Created '{item.Name}' with color '{item.TileColor ?? "null"}'");
            await PersistAsync();
            // Prompt to move entries into this category
            await PromptAssignOnAddAsync(item);
        }

        public async Task RemoveCategoryAsync(CategoryItem item, bool deleteContents)
        {
            if (item == null) return;

            // Protect the 'Secure rubbish bin' category (and legacy Deleted/Trash names)
            if (item.IsTrash || string.Equals(item.Name, "Secure rubbish bin", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, "Deleted", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, "Trash", System.StringComparison.OrdinalIgnoreCase))
            {
                await _dialogService.ShowWarningAsync("Protected Category", "The 'Secure rubbish bin' category cannot be removed or altered.", _ownerWindow);
                return;
            }

            // Build target category list (exclude the category being removed)
            var targets = Categories.Where(c => c.Name != item.Name).Select(c => c.Name).ToList();

            // Show options to the user: Move / Delete / Move to Trash
            var choice = await _dialogService.ShowCategoryDeleteOptionsAsync(item.Name, targets, _ownerWindow);

            if (choice.Action == PhantomVault.UI.Services.DialogService.CategoryDeleteAction.Cancel)
            {
                return; // user cancelled
            }

            // If a vault manager is available, attempt to perform credential updates
            if (_vaultManager is VaultViewModel vm)
            {
                if (choice.Action == PhantomVault.UI.Services.DialogService.CategoryDeleteAction.Move && !string.IsNullOrEmpty(choice.TargetCategory))
                {
                    var moved = vm.MoveCredentialsToCategory(item.Name, choice.TargetCategory);
                    // Optionally inform the user
                    await _dialogService.ShowInfoAsync("Moved Credentials", $"Moved {moved} credentials to '{choice.TargetCategory}'.", _ownerWindow);
                }
                else if (choice.Action == PhantomVault.UI.Services.DialogService.CategoryDeleteAction.Delete)
                {
                    var removed = vm.DeleteCredentialsInCategory(item.Name);
                    await _dialogService.ShowInfoAsync("Deleted Credentials", $"Permanently deleted {removed} credentials.", _ownerWindow);
                }
                else if (choice.Action == PhantomVault.UI.Services.DialogService.CategoryDeleteAction.MoveToTrash)
                {
                    var moved = vm.MoveCredentialsToTrash(item.Name);
                    await _dialogService.ShowInfoAsync("Moved to Trash", $"Moved {moved} credentials to Trash.", _ownerWindow);
                }
            }

            // Remove the category from the list and persist categories in manifest
            Categories.Remove(item);
            // Persist changes immediately (user performed a destructive action)
            await PersistAsync();
        }

        public Task MoveUpAsync(CategoryItem item)
        {
            if (item == null) return Task.CompletedTask;
            int idx = Categories.IndexOf(item);
            if (idx <= 0) return Task.CompletedTask;
            Categories.Move(idx, idx - 1);
            // Recompute order values
            for (int i = 0; i < Categories.Count; i++) Categories[i].Order = i;
            // Debounced persist so rapid reorders don't spam writes
            DebouncedPersist();
            return Task.CompletedTask;
        }

        public Task MoveDownAsync(CategoryItem item)
        {
            if (item == null) return Task.CompletedTask;
            int idx = Categories.IndexOf(item);
            if (idx < 0 || idx >= Categories.Count - 1) return Task.CompletedTask;
            Categories.Move(idx, idx + 1);
            for (int i = 0; i < Categories.Count; i++) Categories[i].Order = i;
            // Debounced persist so rapid reorders don't spam writes
            DebouncedPersist();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Move a category from one index to another (used by drag-reorder)
        /// </summary>
        public void MoveCategory(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= Categories.Count || toIndex >= Categories.Count) return;
            var item = Categories[fromIndex];
            Categories.Move(fromIndex, toIndex);
            for (int i = 0; i < Categories.Count; i++) Categories[i].Order = i;
            DebouncedPersist();
        }

        private async Task PersistAsync()
        {
            if (_manifestService == null || string.IsNullOrEmpty(_manifestPath))
            {
                // No manifest available; still update the vault UI so the user sees changes.
                PublishToVaultManager();
                return;
            }

            try
            {
                // Validate unique, non-empty names (case-insensitive)
                var duplicate = Categories
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .GroupBy(c => c.Name.Trim(), System.StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null)
                {
                    await _dialogService.ShowWarningAsync("Duplicate Names", "Duplicate category names are not allowed. Please choose unique names.", _ownerWindow);
                    return;
                }
                if (Categories.Any(c => string.IsNullOrWhiteSpace(c.Name)))
                {
                    await _dialogService.ShowWarningAsync("Invalid Name", "Category names cannot be empty.", _ownerWindow);
                    return;
                }

                // Enforce a single 'Deleted' category name and ensure it's present
                var deletedCount = Categories.Count(c => string.Equals(c.Name, "Deleted", System.StringComparison.OrdinalIgnoreCase));
                if (deletedCount == 0)
                {
                    Categories.Add(new CategoryItem { Name = "Deleted", Icon = "/Assets/Visuals/Cat Icons/rubbish/rubbish_charcoal.png", Order = Categories.Count, IsTrash = true, TileColor = "#6B7280" });
                }
                else if (deletedCount > 1)
                {
                    await _dialogService.ShowWarningAsync("Duplicate 'Deleted'", "Only one 'Deleted' category is allowed.", _ownerWindow);
                    return;
                }

                // Always keep 'Deleted' as last item visually and in order
                var deleted = Categories.FirstOrDefault(c => string.Equals(c.Name, "Deleted", System.StringComparison.OrdinalIgnoreCase));
                if (deleted != null)
                {
                    var idx = Categories.IndexOf(deleted);
                    if (idx != Categories.Count - 1)
                    {
                        Categories.Move(idx, Categories.Count - 1);
                    }
                }
                // Normalize order values 0..n-1
                for (int i = 0; i < Categories.Count; i++) Categories[i].Order = i;

                // Read existing manifest, update categories, and write back
                var manifest = _manifestService.ReadManifest(_manifestPath, _passphrase, _keyfilePath);
                manifest.Categories = Categories.Select(c => new CategoryModel { Name = c.Name, Icon = c.Icon, Order = c.Order, IsTrash = c.IsTrash, TileColor = c.TileColor }).ToList();
                _manifestService.WriteManifest(manifest, _manifestPath, _passphrase, _keyfilePath ?? manifest.KeyfilePath);

                // Notify vault manager to refresh its category list
                if (_vaultManager is VaultViewModel vm && manifest.Categories != null)
                {
                    vm.UpdateCategoriesFromModels(manifest.Categories);
                    // Also push color map updates
                    var colorMap = Categories.ToDictionary(c => c.Name, c => c.TileColor, System.StringComparer.OrdinalIgnoreCase);
                    vm.UpdateCategoryColors(colorMap);
                }
            }
            catch
            {
                // Best-effort persistence; surface errors via owner window if possible
                if (_ownerWindow != null)
                {
                    var dialog = new Window { Title = "Save Failed", Content = new Avalonia.Controls.TextBlock { Text = "Failed to save categories to manifest.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Width = 420, Height = 160 };
                    await dialog.ShowDialog(_ownerWindow);
                }
            }
            finally
            {
                // Always publish so the vault page reflects the latest set immediately.
                PublishToVaultManager();
            }
        }

        private async Task SaveAllAndCloseAsync()
        {
            await PersistAsync();
            // Ensure UI updates before closing the manager window
            PublishToVaultManager();
            RequestClose();
        }

        private async Task ResetAllColorsAsync()
        {
            foreach (var c in Categories)
            {
                c.TileColor = null;
            }
            await PersistAsync();
            PublishToVaultManager();
        }

        private void CloseWindow()
        {
            RequestClose();
        }

        private void RequestClose()
        {
            // Fire the DismissRequested event - subscribers (like CategoryManagerWindow) will handle closing
            // CloseOwnerOnDismiss flag is only relevant for overlay mode where we might want different behavior
            DismissRequested?.Invoke();
        }

        private void HookCollectionEvents()
        {
            foreach (var c in Categories) HookItem(c);
            if (Categories is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged += (s, e) =>
                {
                    if (e.NewItems != null)
                    {
                        foreach (var obj in e.NewItems)
                            if (obj is CategoryItem ci) HookItem(ci);
                    }
                    if (e.OldItems != null)
                    {
                        foreach (var obj in e.OldItems)
                            if (obj is CategoryItem ci) UnhookItem(ci);
                    }
                    // No auto-persist; user saves explicitly
                };
            }
        }

        private void HookItem(CategoryItem item)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        private void UnhookItem(CategoryItem item)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Persist on any change (name, icon, order, trash flag)
            if (sender is CategoryItem item)
            {
                if (e.PropertyName == nameof(CategoryItem.IsTrash))
                {
                    if (item.IsTrash)
                    {
                        // Unmark all other categories
                        foreach (var other in Categories.Where(c => !ReferenceEquals(c, item)))
                            other.IsTrash = false;

                        // Auto-rename to "Trash" with conflict resolution
                        var desired = "Trash";
                        if (!string.Equals(item.Name, desired, System.StringComparison.Ordinal))
                        {
                            item.Name = GetUniqueName(desired);
                        }
                    }
                    else
                    {
                        // Guard: prevent unsetting the last Trash without assigning another
                        if (!Categories.Any(c => c.IsTrash))
                        {
                            // Re-enable and warn
                            item.IsTrash = true;
                            _ = _dialogService.ShowWarningAsync("Trash Required", "At least one Trash category must exist.", _ownerWindow);
                        }
                    }
                }
                // Persist on meaningful property changes (debounced)
                if (e.PropertyName == nameof(CategoryItem.Name) || e.PropertyName == nameof(CategoryItem.Icon) || e.PropertyName == nameof(CategoryItem.Order) || e.PropertyName == nameof(CategoryItem.IsTrash) || e.PropertyName == nameof(CategoryItem.TileColor))
                {
                    DebouncedPersist();
                }

                if (e.PropertyName == nameof(CategoryItem.IsSelected))
                {
                    this.RaisePropertyChanged(nameof(HasSelection));
                }
            }
            // No auto-persist; user saves explicitly
        }

        private string GetUniqueName(string baseName)
        {
            if (Categories.All(c => !string.Equals(c.Name, baseName, System.StringComparison.OrdinalIgnoreCase)))
                return baseName;
            int i = 2;
            while (true)
            {
                var candidate = $"{baseName} {i}";
                if (Categories.All(c => !string.Equals(c.Name, candidate, System.StringComparison.OrdinalIgnoreCase)))
                    return candidate;
                i++;
            }
        }

        private void DebouncedPersist(int delayMs = 400)
        {
            _persistDebounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _persistDebounceCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, cts.Token);
                    await PersistAsync();
                }
                catch (TaskCanceledException) { }
            });
        }

        private static readonly string?[] _palette = new string?[]
        {
            "#3b82f6", // blue
            "#10b981", // emerald
            "#f59e0b", // amber
            "#ef4444", // red
            "#8b5cf6", // violet
            "#14b8a6", // teal
            "#64748b", // slate
            null // clear
        };

        private void CycleColor(CategoryItem item)
        {
            if (item == null) return;
            var current = item.TileColor;
            int idx = -1;
            for (int i = 0; i < _palette.Length; i++)
            {
                if (string.Equals(_palette[i] ?? string.Empty, current ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            int next = (idx + 1) % _palette.Length;
            item.TileColor = _palette[next];
            DebouncedPersist();
        }

        private async Task MoveItemsAsync(CategoryItem item)
        {
            if (_vaultManager is VaultViewModel vm)
            {
                // Let user choose target category
                var availableTargets = Categories.Where(c => c.Name != item.Name).Select(c => c.Name).ToList();
                var target = await _dialogService.ShowSelectCategoryAsync(availableTargets, $"Move ALL items in '{item.Name}' to:", _ownerWindow);
                if (!string.IsNullOrEmpty(target))
                {
                    var moved = vm.MoveCredentialsToCategory(item.Name, target);
                    await _dialogService.ShowInfoAsync("Moved Items", $"Moved {moved} items to '{target}'.", _ownerWindow);
                }
            }
        }

        private async Task PickIconAsync(CategoryItem item)
        {
            try
            {
                // Use Icon Library for icon selection
                var iconManagerVm = new IconManagerViewModel(GetIconManager());
                var iconManagerWindow = new IconManagerWindow { DataContext = iconManagerVm };
                iconManagerVm.SetOwnerWindow(iconManagerWindow, GetOwnerWindow());

                await iconManagerWindow.ShowDialog(GetOwnerWindow());

                // Get the confirmed icon path from the Icon Library
                if (!string.IsNullOrEmpty(iconManagerVm.ConfirmedIconPath))
                {
                    // Convert absolute path to relative path for Avalonia resources
                    var baseDir = AppContext.BaseDirectory;
                    var relativePath = iconManagerVm.ConfirmedIconPath.Replace(baseDir, "").Replace("\\", "/").TrimStart('/');
                    item.Icon = $"/{relativePath}";

                    // Auto-detect the dominant color from the icon and apply it
                    var dominantColor = ExtractDominantColor(iconManagerVm.ConfirmedIconPath);
                    if (dominantColor != null)
                    {
                        item.TileColor = dominantColor;
                        Debug.WriteLine($"[CATEGORY-MGR] Auto-detected tile color '{dominantColor}' from icon");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CATEGORY-MGR] Error picking icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Automatically detects and applies tile colors for categories that have an icon but no TileColor set.
        /// </summary>
        private void AutoDetectTileColors()
        {
            foreach (var item in Categories)
            {
                if (!string.IsNullOrEmpty(item.TileColor)) continue; // already has a color
                if (string.IsNullOrEmpty(item.Icon)) continue; // no icon to sample

                var resolved = ResolveIconPath(item.Icon);
                if (resolved == null) continue;

                var color = ExtractDominantColor(resolved);
                if (color != null)
                {
                    item.TileColor = color;
                    Debug.WriteLine($"[CATEGORY-MGR] AutoDetect: Set tile color '{color}' for '{item.Name}'");
                }
            }
        }

        /// <summary>
        /// Resolves a relative icon path (starting with /) to an absolute path
        /// using AppContext.BaseDirectory. Returns the original path if already absolute and exists.
        /// </summary>
        private static string? ResolveIconPath(string path)
        {
            if (System.IO.File.Exists(path)) return path;
            if (path.StartsWith("/") || path.StartsWith("\\"))
            {
                var resolved = System.IO.Path.Combine(AppContext.BaseDirectory, path.TrimStart('/', '\\'));
                if (System.IO.File.Exists(resolved)) return resolved;
            }
            return null;
        }

        /// <summary>
        /// Extracts the dominant (most frequent non-transparent, non-near-black/white) colour
        /// from an image file using SkiaSharp. Returns a hex string like "#AABBCC" or null.
        /// </summary>
        private static string? ExtractDominantColor(string imagePath)
        {
            try
            {
                if (!System.IO.File.Exists(imagePath)) return null;

                using var stream = System.IO.File.OpenRead(imagePath);
                using var skBitmap = SkiaSharp.SKBitmap.Decode(stream);
                if (skBitmap == null) return null;

                // Sample pixels and bucket them by hue to find the dominant chromatic colour
                var colorCounts = new Dictionary<uint, int>();
                int step = Math.Max(1, Math.Max(skBitmap.Width, skBitmap.Height) / 64); // sample ~64x64 grid

                for (int y = 0; y < skBitmap.Height; y += step)
                {
                    for (int x = 0; x < skBitmap.Width; x += step)
                    {
                        var px = skBitmap.GetPixel(x, y);

                        // Skip transparent or near-transparent pixels
                        if (px.Alpha < 80) continue;

                        // Skip very dark (near-black) and very light (near-white) pixels
                        float brightness = (px.Red * 0.299f + px.Green * 0.587f + px.Blue * 0.114f) / 255f;
                        if (brightness < 0.12f || brightness > 0.92f) continue;

                        // Skip low-saturation (grey) pixels
                        int max = Math.Max(px.Red, Math.Max(px.Green, px.Blue));
                        int min = Math.Min(px.Red, Math.Min(px.Green, px.Blue));
                        float saturation = max == 0 ? 0 : (max - min) / (float)max;
                        if (saturation < 0.15f) continue;

                        // Quantise to reduce colour space — shift right by 4 bits (16-colour buckets per channel)
                        uint quantised = ((uint)(px.Red >> 4) << 8) | ((uint)(px.Green >> 4) << 4) | (uint)(px.Blue >> 4);
                        colorCounts.TryGetValue(quantised, out int count);
                        colorCounts[quantised] = count + 1;
                    }
                }

                if (colorCounts.Count == 0) return null;

                // Find the most frequent quantised colour
                var best = colorCounts.OrderByDescending(kv => kv.Value).First().Key;

                // Expand back to 8-bit per channel (shift left 4 + half-step for centering)
                int r = (int)((best >> 8) & 0xF) * 17;
                int g = (int)((best >> 4) & 0xF) * 17;
                int b = (int)(best & 0xF) * 17;

                // Make it a softer/lighter pastel tone suitable for a dark UI accent strip
                // Blend towards a lighter version (60% original, 40% white)
                r = Math.Min(255, (int)(r * 0.7 + 255 * 0.3));
                g = Math.Min(255, (int)(g * 0.7 + 255 * 0.3));
                b = Math.Min(255, (int)(b * 0.7 + 255 * 0.3));

                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CATEGORY-MGR] ExtractDominantColor failed: {ex.Message}");
                return null;
            }
        }

        private async Task OpenIconLibraryAsync()
        {
            // Open Icon Manager as modal window (consistent with PickIconAsync)
            try
            {
                var iconManagerVm = new IconManagerViewModel(GetIconManager());
                var iconManagerWindow = new IconManagerWindow { DataContext = iconManagerVm };
                iconManagerVm.SetOwnerWindow(iconManagerWindow, GetOwnerWindow());
                await iconManagerWindow.ShowDialog(GetOwnerWindow());
                
                // Save icon library path preference
                var settings = SettingsService.Load();
                var iconsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
                settings.LastIconLibraryPath = iconsPath;
                SettingsService.Save(settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CATEGORY-MGR] Error opening icon library: {ex.Message}");
            }
        }

        private void ApplyColor(PaletteSelection selection)
        {
            if (selection == null || selection.Item == null) return;
            
            Debug.WriteLine($"[CATEGORY-MGR] ApplyColor: Applying color '{selection.Color ?? "null"}' to '{selection.Item.Name}'");
            
            selection.Item.TileColor = selection.Color; // can be null to clear
            
            // Save as default color preference if a color was selected (not cleared)
            if (!string.IsNullOrEmpty(selection.Color))
            {
                var settings = SettingsService.Load();
                settings.DefaultCategoryColor = selection.Color;
                SettingsService.Save(settings);
                Debug.WriteLine($"[CATEGORY-MGR] ApplyColor: Saved '{selection.Color}' as default category color");
            }
            
            DebouncedPersist();
            PublishToVaultManager();
        }

        private async Task ManageSelectedItemsAsync(CategoryItem item)
        {
            if (_vaultManager is VaultViewModel vm)
            {
                var creds = vm.GetCredentialsForCategory(item.Name);
                var targets = Categories.Where(c => c.Name != item.Name).Select(c => c.Name).ToList();
                var result = await _dialogService.ShowCategoryItemsManagerAsync(item.Name, creds, targets, _ownerWindow);
                if (result.Action == PhantomVault.UI.Services.DialogService.CategoryItemsAction.Move &&
                    result.Selected.Any() &&
                    !string.IsNullOrEmpty(result.TargetCategory))
                {
                    var moved = vm.RestoreCredentialsToCategory(result.Selected, result.TargetCategory!);
                    await _dialogService.ShowInfoAsync("Moved Items", $"Moved {moved} items to '{result.TargetCategory}'.", _ownerWindow);
                }
            }
        }

        private async Task ManageSelectedCategoriesAsync()
        {
            var selected = Categories.Where(c => c.IsSelected).ToList();
            if (!selected.Any()) return;
            if (_vaultManager is not VaultViewModel vm) return;

            var targets = Categories.Where(c => !c.IsSelected).Select(c => c.Name).ToList();
            var bulk = await _dialogService.ShowBulkCategoryActionAsync(targets, _ownerWindow);
            if (bulk.Action == PhantomVault.UI.Services.DialogService.BulkCategoriesAction.Cancel)
                return;

            int affected = 0;
            if (bulk.Action == PhantomVault.UI.Services.DialogService.BulkCategoriesAction.Move && !string.IsNullOrEmpty(bulk.TargetCategory))
            {
                foreach (var cat in selected)
                {
                    affected += vm.MoveCredentialsToCategory(cat.Name, bulk.TargetCategory!);
                }
                await _dialogService.ShowInfoAsync("Moved Items", $"Moved {affected} items to '{bulk.TargetCategory}'.", _ownerWindow);
            }
            else if (bulk.Action == PhantomVault.UI.Services.DialogService.BulkCategoriesAction.MoveToDeleted)
            {
                foreach (var cat in selected)
                {
                    affected += vm.MoveCredentialsToTrash(cat.Name);
                }
                await _dialogService.ShowInfoAsync("Moved to Deleted", $"Moved {affected} items to 'Deleted'.", _ownerWindow);
            }
            // Persist manifest/category changes after bulk actions
            await PersistAsync();
        }

        private async Task PromptAssignOnAddAsync(CategoryItem newItem)
        {
            if (_vaultManager is VaultViewModel vm)
            {
                // Create and show the new drag-drop dialog
                var dialogVm = new AddCategoryDialogViewModel(vm);
                dialogVm.Initialize(vm, newItem.Name);

                var dialog = new AddCategoryDialog
                {
                    DataContext = dialogVm
                };

                bool applied = false;
                dialogVm.CategoryCreated += (name, entries) =>
                {
                    // Update the category name if changed
                    if (!string.IsNullOrWhiteSpace(name) && name != newItem.Name)
                    {
                        newItem.Name = name;
                    }

                    // Move entries to the new category by updating their Group property
                    foreach (var entry in entries)
                    {
                        // Access the underlying Credential model through reflection or direct access
                        var credentialField = entry.GetType().GetField("_credential",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (credentialField?.GetValue(entry) is Core.Models.Credential credential)
                        {
                            credential.Group = newItem.Name;
                        }
                    }

                    applied = true;
                };

                dialogVm.DialogClosed += () =>
                {
                    dialog.Close();
                };

                await dialog.ShowDialog(_ownerWindow ?? GetMainWindow());

                if (applied)
                {
                    // Refresh counts and notify
                    SyncFromVaultCategories(vm);
                    await PersistAsync();
                }
            }
        }

        private Window GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow ?? new Window();
            }
            return new Window();
        }
    }

    public sealed class PaletteSelection
    {
        public CategoryItem? Item { get; set; }
        public string? Color { get; set; }
    }

    public class CategoryItem : ReactiveObject
    {
        private bool _isSelected = false;
        private string _name = string.Empty;
        private string? _icon;
        private int _order = 0;
        private bool _isTrash = false;
        private string? _tileColor; // hex color string, e.g., #3b82f6
        private int _count = 0;

        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }
        public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
        public string? Icon { get => _icon; set => this.RaiseAndSetIfChanged(ref _icon, value); }
        public int Order { get => _order; set => this.RaiseAndSetIfChanged(ref _order, value); }
        public bool IsTrash { get => _isTrash; set => this.RaiseAndSetIfChanged(ref _isTrash, value); }
        public string? TileColor { get => _tileColor; set => this.RaiseAndSetIfChanged(ref _tileColor, value); }
        public int Count { get => _count; set => this.RaiseAndSetIfChanged(ref _count, value); }
    }
}
