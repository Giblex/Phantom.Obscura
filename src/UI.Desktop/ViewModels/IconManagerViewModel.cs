using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using System.Security.Cryptography;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using ReactiveUI;
using Serilog;

namespace PhantomVault.UI.ViewModels
{

    internal static class IconBitmapCache
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _cache = new();
        private static readonly LinkedList<string> _lru = new();
        private static int _capacity = 200;

        private static readonly HashSet<string> _svgExtensions = new(StringComparer.OrdinalIgnoreCase) { ".svg" };

        public static int Capacity
        {
            get => _capacity;
            set
            {
                lock (_sync)
                {
                    _capacity = Math.Max(1, value);
                    EvictAsNecessary();
                }
            }
        }

        public static Avalonia.Media.Imaging.Bitmap? GetOrAdd(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            lock (_sync)
            {
                if (_cache.TryGetValue(path, out var bmp))
                {

                    _lru.Remove(path);
                    _lru.AddFirst(path);
                    return bmp;
                }
            }

            try
            {
                Avalonia.Media.Imaging.Bitmap? newBmp = null;
                var ext = Path.GetExtension(path);

                if (_svgExtensions.Contains(ext))
                {
                    newBmp = LoadSvgAsBitmap(path);
                }
                else
                {
                    newBmp = new Avalonia.Media.Imaging.Bitmap(path);
                }

                if (newBmp != null)
                {
                    lock (_sync)
                    {
                        _cache[path] = newBmp;
                        _lru.AddFirst(path);
                        EvictAsNecessary();
                    }
                }
                return newBmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-BITMAP-CACHE] ERROR loading {path}: {ex.Message}");
                return null;
            }
        }

        private static Avalonia.Media.Imaging.Bitmap? LoadSvgAsBitmap(string path)
        {
            try
            {
                using var svg = new Svg.Skia.SKSvg();
                var picture = svg.Load(path);
                if (picture == null) return null;

                var bounds = picture.CullRect;
                int width = Math.Max(1, (int)bounds.Width);
                int height = Math.Max(1, (int)bounds.Height);

                if (width > 256 || height > 256)
                {
                    float scale = 256f / Math.Max(width, height);
                    width = Math.Max(1, (int)(width * scale));
                    height = Math.Max(1, (int)(height * scale));
                }

                using var skBitmap = new SkiaSharp.SKBitmap(width, height);
                using var canvas = new SkiaSharp.SKCanvas(skBitmap);
                canvas.Clear(SkiaSharp.SKColors.Transparent);

                float scaleX = width / bounds.Width;
                float scaleY = height / bounds.Height;
                float s = Math.Min(scaleX, scaleY);
                canvas.Translate((width - bounds.Width * s) / 2f, (height - bounds.Height * s) / 2f);
                canvas.Scale(s, s);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(picture);
                canvas.Flush();

                using var image = SkiaSharp.SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var stream = data.AsStream();
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-BITMAP-CACHE] SVG render failed for {path}: {ex.Message}");
                return null;
            }
        }

        private static void EvictAsNecessary()
        {
            while (_lru.Count > _capacity)
            {
                var last = _lru.Last!.Value;
                _lru.RemoveLast();
                if (_cache.TryGetValue(last, out var bmp))
                {

                    _cache.Remove(last);
                }
            }
        }

        public static void Clear()
        {
            lock (_sync)
            {
                foreach (var bmp in _cache.Values)
                {
                    try { bmp.Dispose(); } catch { }
                }
                _cache.Clear();
                _lru.Clear();
            }
        }
    }
    public sealed class IconManagerViewModel : ReactiveObject
    {

        private static readonly string[] _excludedKeywords = new[]
        {
            "visa","master","mastercard","amex","americanexpress","american express","discover","paypal",
            "citi","hsbc","bank","credit","debit","card","cards","logo","logos","brand","branding","pay",
            "westernunion","maestro","cirrus","stripe","visa"
        };
        private readonly IconManager _iconManager;
        private readonly DialogService _dialogService = new();
        private readonly List<IconFileEntryViewModel> _allIcons = new();
        private Window? _ownerWindow;
        private Window? _callingOwnerWindow;
        private bool _isBusy;
        private string _statusMessage = "Ready";
        private string _searchText = string.Empty;
        private IconFileEntryViewModel? _selectedIcon;
        private string? _confirmedIconPath;
        private bool _isGridView = true;
        private int _pageIndex;
        private int _pageSize = 500;

        public IconManagerViewModel(IconManager iconManager)
        {
            _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));

            Icons = new ObservableCollection<IconFileEntryViewModel>();

            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
            ImportIconCommand = ReactiveCommand.CreateFromTask(ImportIconsAsync);
            DeleteIconCommand = ReactiveCommand.CreateFromTask<IconFileEntryViewModel?>(DeleteIconAsync);
            RevealIconCommand = ReactiveCommand.Create<IconFileEntryViewModel?>(RevealIcon);
            OpenIconsFolderCommand = ReactiveCommand.Create(OpenIconsFolder);
            DownloadFlatIconsCommand = ReactiveCommand.CreateFromTask(DownloadFlatIconsAsync);
            CloseCommand = ReactiveCommand.Create(Close);
            SelectIconCommand = ReactiveCommand.Create<IconFileEntryViewModel?>(SelectIcon);
            HandleIconClickCommand = ReactiveCommand.Create<IconFileEntryViewModel?>(HandleIconClick);
            ToggleGridViewCommand = ReactiveCommand.Create(() => { IsGridView = !IsGridView; });
            PrevPageCommand = ReactiveCommand.Create(() => MovePage(-1), this.WhenAnyValue(vm => vm.PageIndex, idx => idx > 0));
            NextPageCommand = ReactiveCommand.Create(() => MovePage(1), this.WhenAnyValue(vm => vm.PageIndex, idx => idx < TotalPages - 1));
            SetPageSizeCommand = ReactiveCommand.Create<int>(SetPageSize);
            ShowVariantsCommand = ReactiveCommand.Create<IconFileEntryViewModel>(ShowVariantsForIcon);
            ApplyVariantToCategoryCommand = ReactiveCommand.CreateFromTask<IconFileEntryViewModel>(ApplyVariantToCategoryAsync);

            this.WhenAnyValue(vm => vm.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFilter());

            RefreshCommand.Execute().Subscribe(
                _ => { },
                ex =>
                {
                    Log.Error(ex, "[IconManager] Initial refresh failed.");
                    StatusMessage = "Icons could not be loaded. Verify the icon library is accessible and try again.";
                    IsBusy = false;
                });
        }

        public ObservableCollection<IconFileEntryViewModel> Icons { get; }
        public ObservableCollection<IconFileEntryViewModel> TopIcons { get; } = new ObservableCollection<IconFileEntryViewModel>();
        public ObservableCollection<IconSectionViewModel> Sections { get; } = new();
        public ObservableCollection<IconSectionViewModel> FilteredSections { get; } = new();
        public ObservableCollection<IconFileEntryViewModel> VariantIcons { get; } = new ObservableCollection<IconFileEntryViewModel>();
        private IconFileEntryViewModel? _variantOwnerIcon;
        public IconFileEntryViewModel? VariantOwnerIcon { get => _variantOwnerIcon; set => this.RaiseAndSetIfChanged(ref _variantOwnerIcon, value); }
        private bool _isVariantPopupOpen;
        public bool IsVariantPopupOpen { get => _isVariantPopupOpen; set => this.RaiseAndSetIfChanged(ref _isVariantPopupOpen, value); }
        private int _selectedVariantIndex;
        public int SelectedVariantIndex { get => _selectedVariantIndex; set { if (value == _selectedVariantIndex) return; this.RaiseAndSetIfChanged(ref _selectedVariantIndex, value); this.RaisePropertyChanged(nameof(SelectedVariant)); } }

        public IconFileEntryViewModel? SelectedVariant => (SelectedVariantIndex >= 0 && SelectedVariantIndex < VariantIcons.Count) ? VariantIcons[SelectedVariantIndex] : null;
        public ObservableCollection<IconFileEntryViewModel> PagedIcons { get; } = new ObservableCollection<IconFileEntryViewModel>();
        public IEnumerable<IconFileEntryViewModel> GridIcons => PagedIcons;

        public IconFileEntryViewModel? SelectedIcon
        {
            get => _selectedIcon;
            set => this.RaiseAndSetIfChanged(ref _selectedIcon, value);
        }

        public string? ConfirmedIconPath
        {
            get => _confirmedIconPath;
            private set => this.RaiseAndSetIfChanged(ref _confirmedIconPath, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => this.RaiseAndSetIfChanged(ref _searchText, value);
        }

        public int FilteredIconCount => Icons.Count;
        public int TotalIconCount => Sections.Sum(s => s.TotalCount);

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportIconCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel?, Unit> DeleteIconCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel?, Unit> RevealIconCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenIconsFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadFlatIconsCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel?, Unit> SelectIconCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleGridViewCommand { get; }
        public ReactiveCommand<Unit, Unit> PrevPageCommand { get; }
        public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
        public ReactiveCommand<int, Unit> SetPageSizeCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel, Unit> ShowVariantsCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel, Unit> ApplyVariantToCategoryCommand { get; }
        public ReactiveCommand<IconFileEntryViewModel?, Unit> HandleIconClickCommand { get; }

        public void SetOwnerWindow(Window window, Window? callingOwner = null)
        {
            _ownerWindow = window;
            _callingOwnerWindow = callingOwner;
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[ICON-MGR] SetOwnerWindow: owner={_ownerWindow?.GetType().Name ?? "null"}, callingOwner={_callingOwnerWindow?.GetType().Name ?? "null"}");
#endif
        }

        public bool HasOwnerWindow => _ownerWindow != null;

        private void SelectIcon(IconFileEntryViewModel? icon)
        {
            SelectedIcon = icon;

            if (icon != null && _callingOwnerWindow != null)
            {
                ConfirmedIconPath = icon.FullPath;
                _ownerWindow?.Close();
            }
        }

        public bool IsGridView
        {
            get => _isGridView;
            set => this.RaiseAndSetIfChanged(ref _isGridView, value);
        }

        public int PageIndex
        {
            get => _pageIndex;
            private set
            {
                if (value == _pageIndex) return;
                this.RaiseAndSetIfChanged(ref _pageIndex, value);
                this.RaisePropertyChanged(nameof(CurrentPageNumber));
                this.RaisePropertyChanged(nameof(IsFirstPage));
                this.RaisePropertyChanged(nameof(IsLastPage));
                this.RaisePropertyChanged(nameof(PageIndicatorText));
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value == _pageSize) return;
                this.RaiseAndSetIfChanged(ref _pageSize, value);
                UpdatePagedIcons();
                this.RaisePropertyChanged(nameof(TotalPages));
                this.RaisePropertyChanged(nameof(CurrentPageNumber));
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Icons.Count / Math.Max(1, PageSize)));

        public int CurrentPageNumber => PageIndex + 1;

        public bool IsFirstPage => PageIndex == 0;

        public bool IsLastPage => PageIndex >= TotalPages - 1;

        public string PageIndicatorText => $"Page {CurrentPageNumber} of {TotalPages}";

        public int[] PageSizeOptions { get; } = new[] { 50, 100, 250, 500, 1000 };

        private async Task RefreshAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Scanning icon library...";

                Debug.WriteLine($"[ICON-MANAGER] Scanning directory: {_iconManager.IconsDirectory}");

                if (!Directory.Exists(_iconManager.IconsDirectory))
                {
                    Debug.WriteLine($"[ICON-MANAGER] Directory does not exist, creating: {_iconManager.IconsDirectory}");
                    try
                    {
                        Directory.CreateDirectory(_iconManager.IconsDirectory);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[IconManager] Icon directory is inaccessible.");
                        StatusMessage = "The icon directory cannot be accessed. Verify its permissions and try again.";
                        IsBusy = false;
                        return;
                    }
                }

                IconCategoryInfo[] categories;
                try
                {
                    categories = await Task.Run(() => _iconManager.GetIconCategories());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[IconManager] Failed to scan icon categories.");
                    StatusMessage = "The icon library could not be scanned. Try refreshing it.";
                    IsBusy = false;
                    return;
                }

                Debug.WriteLine($"[ICON-MANAGER] Found {categories.Length} icon categories");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Sections.Clear();
                    FilteredSections.Clear();
                    _allIcons.Clear();
                    Icons.Clear();
                    TopIcons.Clear();
                    PagedIcons.Clear();

                    int totalFiles = 0;
                    foreach (var cat in categories)
                    {
                        var section = new IconSectionViewModel(
                            cat.Name,
                            cat.FullPath,
                            cat.RelativePath,
                            cat.FileCount,
                            cat.SubfolderCount,
                            cat.IsVariantCategory,
                            _iconManager,
                            IsExcluded);
                        section.OnLoaded = OnSectionLoaded;
                        section.OnVariantClicked = HandleVariantClicked;
                        section.OnIconSelected = HandleIconSelected;
                        Sections.Add(section);
                        FilteredSections.Add(section);
                        totalFiles += cat.FileCount;
                    }

                    this.RaisePropertyChanged(nameof(TotalIconCount));
                    StatusMessage = totalFiles == 0
                        ? "No icons found. Import or download icons to get started."
                        : $"Found {totalFiles:N0} icon(s) in {Sections.Count} categories. Loading...";
                });

                foreach (var section in Sections)
                {
                    await section.ExpandAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconManager] Refresh failed.");
                StatusMessage = "Failed to refresh icons.";
                await _dialogService.ShowErrorAsync("Icon Manager", "The icon library could not be refreshed. Verify it is accessible and try again.", _ownerWindow);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnSectionLoaded(IconSectionViewModel section)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allIcons.Clear();
                foreach (var s in Sections.Where(s => s.IsLoaded))
                {
                    _allIcons.AddRange(s.AllIcons);
                }

                ApplyFilter();
                StatusMessage = $"Loaded {_allIcons.Count} icon(s) from {Sections.Count(s => s.IsLoaded)}/{Sections.Count} categories.";
                this.RaisePropertyChanged(nameof(TotalIconCount));
            });
        }

        private void HandleVariantClicked(IconFileEntryViewModel representative, IReadOnlyList<IconFileEntryViewModel> variants)
        {
            VariantIcons.Clear();
            VariantOwnerIcon = representative;
            foreach (var v in variants)
                VariantIcons.Add(v);
            SelectedVariantIndex = 0;
            IsVariantPopupOpen = true;
        }

        private void HandleIconSelected(IconFileEntryViewModel icon)
        {
            SelectIcon(icon);
        }

        private void HandleIconClick(IconFileEntryViewModel? icon)
        {
            if (icon == null) return;

            Debug.WriteLine($"[IconManager] HandleIconClick: '{icon.Name}' path='{icon.FullPath}'");

            foreach (var section in Sections)
            {
                if (!section.IsLoaded) continue;
                if (!section.DisplayIcons.Contains(icon)) continue;

                Debug.WriteLine($"[IconManager] Found in section '{section.Name}', IsVariantMode={section.IsVariantMode}");

                if (section.IsVariantMode)
                {
                    var variants = section.GetVariantsFor(icon);
                    Debug.WriteLine($"[IconManager] Got {variants.Count} variant(s) for '{icon.Name}'");
                    HandleVariantClicked(icon, variants);
                }
                else
                {
                    SelectIcon(icon);
                }
                return;
            }

            Debug.WriteLine($"[IconManager] HandleIconClick: no owning section found, selecting directly");
            SelectIcon(icon);
        }

        private async Task ImportIconsAsync()
        {
            if (_ownerWindow?.StorageProvider == null)
            {
                await _dialogService.ShowWarningAsync("Icon Manager", "File picker is unavailable in this context.", _ownerWindow);
                return;
            }

            var iconFilters = IconManager.SupportedFileExtensions
                .Select(ext => "*" + ext)
                .ToArray();

            var fileType = new FilePickerFileType("Icon files")
            {
                Patterns = iconFilters
            };

            var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select icons to import",
                AllowMultiple = true,
                FileTypeFilter = new[] { fileType }
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(_iconManager.IconsDirectory);

            var imported = 0;
            foreach (var storageFile in files)
            {
                try
                {
                    var localPath = storageFile.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        if (await CopyFromLocalPathAsync(localPath))
                        {
                            imported++;
                        }

                        continue;
                    }

                    if (string.IsNullOrEmpty(storageFile.Name))
                    {
                        continue;
                    }

                    var destinationPath = Path.Combine(_iconManager.IconsDirectory, storageFile.Name);
                    destinationPath = await EnsureUniqueNameAsync(destinationPath);

                    await using var source = await storageFile.OpenReadAsync();
                    await using var target = File.Create(destinationPath);
                    await source.CopyToAsync(target);
                    imported++;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[IconManager] Failed to import icon {IconName}.", storageFile.Name);
                    await _dialogService.ShowWarningAsync("Icon Import", $"'{storageFile.Name}' could not be imported. Verify it is a supported image and try again.", _ownerWindow);
                }
            }

            if (imported > 0)
            {
                StatusMessage = $"Imported {imported} icon(s).";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "No icons imported.";
            }
        }

        private async Task<bool> CopyFromLocalPathAsync(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!IconManager.SupportedFileExtensions.Contains(extension))
            {
                await _dialogService.ShowWarningAsync("Icon Import", $"Unsupported file type: {Path.GetFileName(path)}", _ownerWindow);
                return false;
            }

            var destinationPath = Path.Combine(_iconManager.IconsDirectory, Path.GetFileName(path));
            destinationPath = await EnsureUniqueNameAsync(destinationPath);

            File.Copy(path, destinationPath, overwrite: false);
            return true;
        }

        private async Task<string> EnsureUniqueNameAsync(string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            var replace = await _dialogService.ShowConfirmationAsync(
                "Replace Icon",
                $"An icon named '{Path.GetFileName(destinationPath)}' already exists. Replace it?",
                _ownerWindow);

            if (replace)
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch
                {

                }
                return destinationPath;
            }

            var directory = Path.GetDirectoryName(destinationPath) ?? _iconManager.IconsDirectory;
            var fileName = Path.GetFileNameWithoutExtension(destinationPath);
            var extension = Path.GetExtension(destinationPath);
            var counter = 1;

            string candidate;
            do
            {
                candidate = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                counter++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private async Task DeleteIconAsync(IconFileEntryViewModel? icon)
        {
            if (icon == null)
            {
                return;
            }

            var confirm = await _dialogService.ShowConfirmationAsync(
                "Delete Icon",
                $"Delete '{icon.Name}' from the icon library?",
                _ownerWindow);

            if (!confirm)
            {
                return;
            }

            try
            {
                File.Delete(icon.FullPath);
                StatusMessage = $"Deleted '{icon.Name}'.";
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconManager] Failed to delete an icon.");
                await _dialogService.ShowErrorAsync("Delete Icon", "The icon could not be deleted. Verify the icon library is writable and try again.", _ownerWindow);
            }
        }

        private void RevealIcon(IconFileEntryViewModel? icon)
        {
            var path = icon?.FullPath ?? _iconManager.IconsDirectory;
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
                else if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[IconManager] Failed to reveal an icon in the file manager.");
                _ = _dialogService.ShowErrorAsync("Reveal Icon", "The icon location could not be opened. Verify the file still exists and try again.", _ownerWindow);
            }
        }

        private void OpenIconsFolder()
        {
            try
            {
                Directory.CreateDirectory(_iconManager.IconsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = _iconManager.IconsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconManager] Failed to open the icon library location.");
                _ = _dialogService.ShowErrorAsync("Icon Manager", "The icon library location could not be opened. Verify it is accessible and try again.", _ownerWindow);
            }
        }

        private void Close()
        {

            if (SelectedIcon != null && _callingOwnerWindow != null)
            {
                ConfirmedIconPath = SelectedIcon.FullPath;
            }
            _ownerWindow?.Close();
        }

        private void ApplyFilter()
        {
            var search = SearchText?.Trim();

            FilteredSections.Clear();
            foreach (var section in Sections)
            {
                if (string.IsNullOrEmpty(search) ||
                    section.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredSections.Add(section);
                }
                else if (section.IsLoaded &&
                         section.AllIcons.Any(i =>
                             i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                             i.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)))
                {
                    FilteredSections.Add(section);
                }
            }

            IEnumerable<IconFileEntryViewModel> filtered = _allIcons;

            if (!string.IsNullOrEmpty(search))
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                filtered = filtered.Where(icon =>
                    icon.Name.Contains(search, comparison) ||
                    icon.RelativePath.Contains(search, comparison));
            }

            Icons.Clear();

            filtered = filtered.Where(i => !IsExcluded(i));

            try
            {
                var unique = filtered
                    .GroupBy(i => NormalizeBaseName(i.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(i => i.SizeBytes).First())
                    .ToList();

                foreach (var icon in unique)
                {
                    Icons.Add(icon);
                }
            }
            catch
            {

                foreach (var icon in filtered) Icons.Add(icon);
            }

            this.RaisePropertyChanged(nameof(FilteredIconCount));

            TopIcons.Clear();
            try
            {

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var grouped = filtered
                    .GroupBy(a => GetFolderKey(a), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First());

                foreach (var rep in grouped)
                {
                    if (TopIcons.Count >= 60) break;
                    var key = rep.Hash;
                    if (string.IsNullOrEmpty(key)) key = rep.FullPath?.ToLowerInvariant() ?? Guid.NewGuid().ToString();
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    TopIcons.Add(rep);
                }
            }
            catch (Exception ex)
            {

                TopIcons.Clear();
                foreach (var i in filtered.Take(60)) TopIcons.Add(i);
                Debug.WriteLine($"[ICON-MANAGER] TopIcons grouping failed: {ex.Message}");
            }
            this.RaisePropertyChanged(nameof(GridIcons));
            UpdatePagedIcons();
        }

        private static string ComputeFileHash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);

            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private void UpdatePagedIcons()
        {

            if (PageIndex < 0) PageIndex = 0;
            var total = Icons.Count;
            if (total == 0)
            {
                PagedIcons.Clear();
                this.RaisePropertyChanged(nameof(TotalPages));
                this.RaisePropertyChanged(nameof(CurrentPageNumber));
                this.RaisePropertyChanged(nameof(IsFirstPage));
                this.RaisePropertyChanged(nameof(IsLastPage));
                return;
            }

            var totalPages = TotalPages;
            if (PageIndex >= totalPages)
            {
                PageIndex = Math.Max(0, totalPages - 1);
            }

            var items = Icons.Skip(PageIndex * PageSize).Take(PageSize).ToList();
            PagedIcons.Clear();
            foreach (var i in items)
            {
                PagedIcons.Add(i);
            }

            Debug.WriteLine($"[ICON-MANAGER] Paging: PageIndex={PageIndex} PageSize={PageSize} PagedCount={PagedIcons.Count} Total={Icons.Count}");
            Console.WriteLine($"[ICON-MANAGER] Paging: PageIndex={PageIndex} PageSize={PageSize} PagedCount={PagedIcons.Count} Total={Icons.Count}");

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(CurrentPageNumber));
            this.RaisePropertyChanged(nameof(IsFirstPage));
            this.RaisePropertyChanged(nameof(IsLastPage));
            this.RaisePropertyChanged(nameof(PageIndicatorText));
            this.RaisePropertyChanged(nameof(PageIndicatorText));
        }

        private static string NormalizeBaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;

            var rx = System.Text.RegularExpressions.RegexOptions.None;
            var key = name.Trim().ToLowerInvariant();

            key = System.Text.RegularExpressions.Regex.Replace(key, "@\\d+x$", string.Empty, rx);
            key = key.Replace('_', '-').Replace(' ', '-');

            var styleTokens = new HashSet<string>(StringComparer.Ordinal)
            {
                "outline", "filled", "fill", "solid", "line", "lines", "linear",
                "bold", "regular", "thin", "light", "duotone", "flat", "color",
                "colour", "mono", "monochrome", "round", "rounded", "sharp", "alt"
            };

            bool changed = true;
            while (changed)
            {
                changed = false;
                var idx = key.LastIndexOf('-');
                if (idx <= 0 || idx >= key.Length - 1) break;

                var suffix = key.Substring(idx + 1);
                bool isIndex = System.Text.RegularExpressions.Regex.IsMatch(suffix, "^\\d+$");
                bool isPixelSize = System.Text.RegularExpressions.Regex.IsMatch(suffix, "^\\d+(x\\d+)?(px)?$");
                bool isStyle = styleTokens.Contains(suffix);

                if (isIndex || isPixelSize || isStyle)
                {
                    key = key.Substring(0, idx);
                    changed = true;
                }
            }

            return key;
        }

        private static bool IsExcluded(IconFileEntryViewModel icon)
        {
            if (icon == null) return false;
            var name = (icon.Name ?? string.Empty).ToLowerInvariant();
            var path = (icon.RelativePath ?? string.Empty).ToLowerInvariant();
            foreach (var k in _excludedKeywords)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (name.Contains(k) || path.Contains(k)) return true;
            }
            return false;
        }

        private void ShowVariantsForIcon(IconFileEntryViewModel icon)
        {
            if (icon == null) return;
            VariantIcons.Clear();
            VariantOwnerIcon = icon;

            foreach (var section in Sections.Where(s => s.IsLoaded && s.IsVariantMode))
            {
                var sectionVariants = section.GetVariantsFor(icon);
                if (sectionVariants.Count > 1)
                {
                    Debug.WriteLine($"[ICON-MANAGER] ShowVariantsForIcon: found {sectionVariants.Count} variants from section '{section.Name}'");
                    foreach (var v in sectionVariants)
                        VariantIcons.Add(v);
                    SelectedVariantIndex = 0;
                    IsVariantPopupOpen = true;
                    return;
                }
            }

            var folderKey = GetFolderKey(icon);
            var variants = _allIcons.Where(a => string.Equals(GetFolderKey(a), folderKey, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!variants.Any()) variants.Add(icon);
            Debug.WriteLine($"[ICON-MANAGER] ShowVariantsForIcon: fallback found {variants.Count} variants by folder key '{folderKey}'");
            foreach (var v in variants)
                VariantIcons.Add(v);
            SelectedVariantIndex = 0;
            IsVariantPopupOpen = true;
        }

        public async Task ApplySelectedVariantAsync()
        {
            var variant = SelectedVariant;
            if (variant == null) return;
            await ApplyVariantToCategoryAsync(variant);
        }

        public void SelectNextVariant()
        {
            if (VariantIcons.Count == 0) return;
            SelectedVariantIndex = Math.Min(VariantIcons.Count - 1, SelectedVariantIndex + 1);
        }

        public void SelectPreviousVariant()
        {
            if (VariantIcons.Count == 0) return;
            SelectedVariantIndex = Math.Max(0, SelectedVariantIndex - 1);
        }

        private static string GetFolderKey(IconFileEntryViewModel icon)
        {
            if (string.IsNullOrEmpty(icon.RelativePath)) return string.Empty;
            try
            {
                var dir = Path.GetDirectoryName(icon.RelativePath);
                if (string.IsNullOrEmpty(dir)) return string.Empty;

                return dir.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task ApplyVariantToCategoryAsync(IconFileEntryViewModel variant)
        {
            if (variant == null) return;
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ICON-MGR] ApplyVariant: variant={variant.Name}, path={variant.FullPath}");
#endif

                IsVariantPopupOpen = false;

                SelectedIcon = variant;
                ConfirmedIconPath = variant.FullPath;

                _ownerWindow?.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconManager] Failed to apply an icon.");
                await _dialogService.ShowErrorAsync("Apply Icon", "The selected icon could not be applied. Verify the vault is unlocked and try again.", _ownerWindow);
            }
        }

        private void MovePage(int delta)
        {
            var target = PageIndex + delta;
            if (target < 0) target = 0;
            if (target > TotalPages - 1) target = TotalPages - 1;
            PageIndex = target;
            UpdatePagedIcons();
        }

        private void SetPageSize(int newSize)
        {
            if (newSize <= 0) return;
            PageSize = newSize;
            PageIndex = 0;
            UpdatePagedIcons();
        }

        private async Task DownloadFlatIconsAsync()
        {
            try
            {
                var downloaderViewModel = new IconDownloaderViewModel();
                var window = new IconDownloaderWindow
                {
                    DataContext = downloaderViewModel
                };
                downloaderViewModel.SetOwnerWindow(window);

                if (_ownerWindow != null)
                {
                    await window.ShowDialog(_ownerWindow);
                }
                else
                {
#pragma warning disable CS8625
                    await window.ShowDialog((Window?)null);
#pragma warning restore CS8625
                }

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconManager] Failed to open the icon downloader.");
                await _dialogService.ShowErrorAsync("Icon Manager", "The icon downloader could not be opened. Try again.", _ownerWindow);
            }
        }
    }

    public sealed class IconFileEntryViewModel : ReactiveObject
    {
        public IconFileEntryViewModel(IconFileInfo info)
        {
            Name = info.Name;
            RelativePath = info.RelativePath;
            FullPath = info.FullPath;
            SizeBytes = info.SizeBytes;
            LastModifiedUtc = info.LastModifiedUtc;
            _iconPath = info.FullPath;
        }

        public string Name { get; }
        public string RelativePath { get; }
        public string FullPath { get; }
        public long SizeBytes { get; }
        public DateTime LastModifiedUtc { get; }

        public string SizeDisplay => FormatSize(SizeBytes);
        public string LastModifiedDisplay => LastModifiedUtc.ToLocalTime().ToString("g");

        private readonly string _iconPath;
        private Avalonia.Media.Imaging.Bitmap? _iconBitmap;
        public string? Hash { get; set; }

        public Avalonia.Media.Imaging.Bitmap? IconBitmap
        {
            get
            {
                if (_iconBitmap != null)
                {
                    return _iconBitmap;
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[IconFileEntryViewModel] Attempting bitmap load: {_iconPath}");
                    if (!string.IsNullOrEmpty(_iconPath) && System.IO.File.Exists(_iconPath))
                    {
                        var bmp = IconBitmapCache.GetOrAdd(_iconPath);
                        if (bmp != null)
                        {
                            _iconBitmap = bmp;
                            System.Diagnostics.Debug.WriteLine($"[IconFileEntryViewModel] Bitmap loaded (cached): {_iconPath} size={_iconBitmap.PixelSize}");
                        }
                    }
                }
                catch
                {

                }

                return _iconBitmap;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            double size = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:F1} {1}", size, units[unitIndex]);
        }
    }
}

