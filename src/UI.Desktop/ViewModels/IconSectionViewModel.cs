using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// Represents a collapsible section (category) in the icon library.
    /// In variant mode (Cat Icons), shows 1 representative per subfolder; clicking shows color variants.
    /// In normal mode (Entry Logos), shows all icons with a display limit.
    /// </summary>
    public sealed class IconSectionViewModel : ReactiveObject
    {
        private const int DefaultDisplayLimit = 120;

        private readonly IconManager _iconManager;
        private readonly Func<IconFileEntryViewModel, bool> _isExcluded;
        private readonly List<IconFileEntryViewModel> _allSectionIcons = new();
        // Variant mode: maps subfolder full path → all variant icons in that subfolder
        private readonly Dictionary<string, List<IconFileEntryViewModel>> _variantGroups = new(StringComparer.OrdinalIgnoreCase);

        private bool _isExpanded;
        private bool _isLoaded;
        private bool _isLoading;
        private bool _showAll;
        private int _loadedCount;
        private int _loadTotalCount;

        public IconSectionViewModel(
            string name,
            string fullPath,
            string relativePath,
            int totalCount,
            int subfolderCount,
            bool isVariantMode,
            IconManager iconManager,
            Func<IconFileEntryViewModel, bool> isExcluded)
        {
            _iconManager = iconManager;
            _isExcluded = isExcluded;

            Name = name;
            FolderFullPath = fullPath;
            FolderRelativePath = relativePath;
            TotalCount = totalCount;
            SubfolderCount = subfolderCount;
            IsVariantMode = isVariantMode;

            DisplayIcons = new ObservableCollection<IconFileEntryViewModel>();
            ToggleExpandedCommand = ReactiveCommand.CreateFromTask(ToggleExpandedAsync);
            ShowAllCommand = ReactiveCommand.Create(ExpandDisplayToAll);
            IconClickedCommand = ReactiveCommand.Create<IconFileEntryViewModel>(OnIconClicked);
        }

        /// <summary>Display name, e.g. "Cat Icons" or "Entry Logos".</summary>
        public string Name { get; }

        /// <summary>Absolute path to the folder on disk.</summary>
        public string FolderFullPath { get; }

        /// <summary>Path relative to the icons root directory.</summary>
        public string FolderRelativePath { get; }

        /// <summary>Total number of supported icon files in this category.</summary>
        public int TotalCount { get; }

        /// <summary>Number of subfolders in this category.</summary>
        public int SubfolderCount { get; }

        /// <summary>True for categories like Cat Icons where subfolders are color-variant groups.</summary>
        public bool IsVariantMode { get; }

        /// <summary>Short count label for the header.</summary>
        public string CountDisplay => IsVariantMode
            ? $"({SubfolderCount} icons, {TotalCount} variants)"
            : $"({TotalCount})";

        /// <summary>Icons currently shown in the UI.</summary>
        public ObservableCollection<IconFileEntryViewModel> DisplayIcons { get; }

        /// <summary>All loaded (deduplicated) icons in this section (representatives in variant mode).</summary>
        public IReadOnlyList<IconFileEntryViewModel> AllIcons => _allSectionIcons;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            private set => this.RaiseAndSetIfChanged(ref _isLoaded, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public bool ShowAll
        {
            get => _showAll;
            private set
            {
                this.RaiseAndSetIfChanged(ref _showAll, value);
                this.RaisePropertyChanged(nameof(HasMore));
                this.RaisePropertyChanged(nameof(ShowMoreText));
            }
        }

        /// <summary>Loading progress text, e.g. "Loading 42/180..."</summary>
        public string LoadingProgressText
        {
            get
            {
                if (_loadTotalCount <= 0) return "Loading...";
                return $"Loading {_loadedCount}/{_loadTotalCount}...";
            }
        }

        /// <summary>True when there are more icons than currently displayed.</summary>
        public bool HasMore => !_showAll && _allSectionIcons.Count > DefaultDisplayLimit;

        /// <summary>Label for the "Show all" button.</summary>
        public string ShowMoreText => $"Show all {_allSectionIcons.Count} icons";

        public ReactiveCommand<Unit, Unit> ToggleExpandedCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAllCommand { get; }

        /// <summary>Command invoked when an icon tile is clicked inside this section.</summary>
        public ReactiveCommand<IconFileEntryViewModel, Unit> IconClickedCommand { get; }

        /// <summary>
        /// Callback invoked on the UI thread after icons finish loading.
        /// Used by <see cref="IconManagerViewModel"/> to rebuild the flat icon list.
        /// </summary>
        public Action<IconSectionViewModel>? OnLoaded { get; set; }

        /// <summary>
        /// Callback invoked when user clicks a variant-mode icon.
        /// Parameters: representative icon, list of all variants in that group.
        /// </summary>
        public Action<IconFileEntryViewModel, IReadOnlyList<IconFileEntryViewModel>>? OnVariantClicked { get; set; }

        /// <summary>
        /// Callback invoked when user clicks a normal-mode icon (select it).
        /// </summary>
        public Action<IconFileEntryViewModel>? OnIconSelected { get; set; }

        // ----- public helpers -----

        /// <summary>Expand and load icons (called programmatically for auto-expand).</summary>
        public async Task ExpandAsync()
        {
            IsExpanded = true;
            if (!IsLoaded)
                await LoadIconsAsync();
        }

        /// <summary>Get variants for a representative icon in variant mode.</summary>
        public IReadOnlyList<IconFileEntryViewModel> GetVariantsFor(IconFileEntryViewModel representative)
        {
            if (representative == null) return Array.Empty<IconFileEntryViewModel>();

            // Primary lookup: by the icon's parent folder path
            var parentDir = Path.GetDirectoryName(representative.FullPath);
            if (parentDir != null && _variantGroups.TryGetValue(parentDir, out var list))
            {
                Debug.WriteLine($"[IconSection] GetVariantsFor: found {list.Count} variants for folder '{Path.GetFileName(parentDir)}'");
                return list;
            }

            Debug.WriteLine($"[IconSection] GetVariantsFor: NO variants found for '{representative.Name}' (parentDir='{parentDir}', groups={_variantGroups.Count})");
            return new List<IconFileEntryViewModel> { representative };
        }

        // ----- private -----

        private void OnIconClicked(IconFileEntryViewModel? icon)
        {
            if (icon == null) return;

            if (IsVariantMode)
            {
                var variants = GetVariantsFor(icon);
                OnVariantClicked?.Invoke(icon, variants);
            }
            else
            {
                OnIconSelected?.Invoke(icon);
            }
        }

        private async Task ToggleExpandedAsync()
        {
            IsExpanded = !IsExpanded;
            if (IsExpanded && !IsLoaded)
                await LoadIconsAsync();
        }

        private void ExpandDisplayToAll()
        {
            ShowAll = true;
            RebuildDisplay();
        }

        private async Task LoadIconsAsync()
        {
            if (IsLoaded || IsLoading) return;
            IsLoading = true;

            try
            {
                if (IsVariantMode)
                    await LoadVariantModeAsync();
                else
                    await LoadNormalModeAsync();

                await Dispatcher.UIThread.InvokeAsync(RebuildDisplay);
                IsLoaded = true;
                OnLoaded?.Invoke(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IconSection] Failed to load icons for {Name}: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Variant mode: enumerate subfolders, pick 1 representative per subfolder,
        /// store full variant lists for popup.
        /// </summary>
        private async Task LoadVariantModeAsync()
        {
            var subfolders = await Task.Run(() => _iconManager.GetCategorySubfolders(FolderFullPath));
            _loadTotalCount = subfolders.Length;
            _loadedCount = 0;
            this.RaisePropertyChanged(nameof(LoadingProgressText));

            var representatives = new List<IconFileEntryViewModel>();
            var variantMap = new Dictionary<string, List<IconFileEntryViewModel>>(StringComparer.OrdinalIgnoreCase);

            // Process subfolders in batches for responsive loading
            const int batchSize = 30;
            for (int i = 0; i < subfolders.Length; i += batchSize)
            {
                var batch = subfolders.Skip(i).Take(batchSize).ToArray();
                await Task.Run(() =>
                {
                    foreach (var sf in batch)
                    {
                        var files = _iconManager.GetIconFilesInFolder(sf.FullPath);
                        var vms = new List<IconFileEntryViewModel>();
                        foreach (var f in files)
                        {
                            var vm = new IconFileEntryViewModel(f);
                            if (!_isExcluded(vm))
                                vms.Add(vm);
                        }

                        if (vms.Count == 0) continue;

                        // Pick the largest file as representative (best quality)
                        var representative = vms.OrderByDescending(v => v.SizeBytes).First();
                        representatives.Add(representative);
                        // Key by subfolder path (not representative file path) for robust lookup
                        variantMap[sf.FullPath] = vms;
                        Debug.WriteLine($"[IconSection] Loaded subfolder '{sf.Name}': {vms.Count} variants, rep='{representative.Name}'");
                    }
                });

                _loadedCount = Math.Min(i + batchSize, subfolders.Length);
                await Dispatcher.UIThread.InvokeAsync(() => this.RaisePropertyChanged(nameof(LoadingProgressText)));
            }

            _allSectionIcons.Clear();
            _allSectionIcons.AddRange(representatives.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));

            _variantGroups.Clear();
            foreach (var kvp in variantMap)
                _variantGroups[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Normal mode: load all icons from the folder, deduplicate by name.
        /// </summary>
        private async Task LoadNormalModeAsync()
        {
            var files = await Task.Run(() => _iconManager.GetIconFilesInFolder(FolderFullPath));
            _loadTotalCount = files.Length;

            var viewModels = new List<IconFileEntryViewModel>();
            await Task.Run(() =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                foreach (var file in files)
                {
                    var vm = new IconFileEntryViewModel(file);
                    if (_isExcluded(vm)) continue;

                    var key = NormalizeBaseName(vm.Name);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    viewModels.Add(vm);
                    count++;
                }
                _loadedCount = count;
            });

            _allSectionIcons.Clear();
            _allSectionIcons.AddRange(viewModels);
        }

        private void RebuildDisplay()
        {
            DisplayIcons.Clear();
            var items = _showAll
                ? (IEnumerable<IconFileEntryViewModel>)_allSectionIcons
                : _allSectionIcons.Take(DefaultDisplayLimit);
            foreach (var icon in items)
                DisplayIcons.Add(icon);

            this.RaisePropertyChanged(nameof(HasMore));
            this.RaisePropertyChanged(nameof(ShowMoreText));
        }

        private static string NormalizeBaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
            var idx = name.LastIndexOf('_');
            if (idx > 0 && idx < name.Length - 1)
            {
                var suffix = name.Substring(idx + 1);
                if (Regex.IsMatch(suffix, "^[a-z0-9-]+$", RegexOptions.IgnoreCase))
                    return name.Substring(0, idx);
            }
            return name;
        }
    }
}
