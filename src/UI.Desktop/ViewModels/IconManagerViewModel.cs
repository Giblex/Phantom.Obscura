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
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Icons;
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

        // Sized for a few scrolled batches plus the prefetched one.
        private static int _capacity = 600;

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
                        // Prefetch decodes off the UI thread, so another caller may have added the
                        // same path meanwhile; keep theirs rather than list the path twice.
                        if (_cache.TryGetValue(path, out var existing))
                        {
                            newBmp.Dispose();
                            return existing;
                        }

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

    /// <summary>One preset in the colour row under the colour wheel.</summary>
    public sealed record ColourSwatch(string Name, Color Color)
    {
        public IBrush Brush { get; } = new SolidColorBrush(Color);
    }

    /// <summary>
    /// The icon library / picker.
    ///
    /// Icons come from <see cref="IconLibraryIndex"/> (organised and de-duplicated once per
    /// session) and are shown in batches: the first <see cref="BatchSize"/> appear straight away,
    /// the next batch is decoded in the background, and the view asks for more as the user nears
    /// the end. When opened for a category or an entry, the closest matches (see
    /// <see cref="IconRanker"/>) are pulled into a row above everything else. Line icons are
    /// recoloured with the colour wheel instead of offering ten pre-coloured copies.
    /// </summary>
    public sealed class IconManagerViewModel : ReactiveObject
    {
        private const int BatchSize = 48;
        private const int SuggestionCount = 12;
        private const double SuggestionThreshold = 0.55;
        private const double SearchThreshold = 0.45;

        private static readonly object IndexGate = new();
        private static Task<IconLibraryIndex>? _sharedIndex;

        private readonly IconManager _iconManager;
        private readonly IconPickContext _context;
        private readonly DialogService _dialogService = new();
        private readonly Dictionary<string, IconTileViewModel> _tiles = new(StringComparer.OrdinalIgnoreCase);

        private IconLibraryIndex? _index;
        private List<IconTileViewModel> _source = new();
        private int _shown;
        private Window? _ownerWindow;

        private bool _isBusy;
        private string _statusMessage = "Organising icons…";
        private string _searchText = string.Empty;
        private int _selectedTabIndex;
        private int _libraryFilterIndex;
        private IconTileViewModel? _selectedTile;
        private Color _selectedColour = Color.Parse("#7FC8DC");
        private bool _keepOriginalColours;
        private bool _isPickerMode;
        private string? _confirmedIconPath;

        public IconManagerViewModel(IconManager iconManager)
            : this(iconManager, IconPickContext.General)
        {
        }

        public IconManagerViewModel(IconManager iconManager, IconPickContext context)
        {
            _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));
            _context = context ?? IconPickContext.General;
            _isPickerMode = _context.Purpose != IconPickPurpose.General;

            SelectIconCommand = ReactiveCommand.Create<IconTileViewModel?>(tile => SelectedTile = tile);
            ClearSelectionCommand = ReactiveCommand.Create(() => { SelectedTile = null; });
            SetPresetColourCommand = ReactiveCommand.Create<Color>(color =>
            {
                SelectedColour = color;
                KeepOriginalColours = false;
            });
            ApplyCommand = ReactiveCommand.CreateFromTask(ApplyAsync, this.WhenAnyValue(x => x.CanApplySelection));
            ImportIconCommand = ReactiveCommand.CreateFromTask(ImportIconsAsync);
            DownloadMoreCommand = ReactiveCommand.CreateFromTask(DownloadMoreAsync);
            OpenMyIconsFolderCommand = ReactiveCommand.Create(OpenMyIconsFolder);
            RevealSelectedCommand = ReactiveCommand.Create(RevealSelected);
            DeleteSelectedCommand = ReactiveCommand.CreateFromTask(DeleteSelectedAsync);
            RefreshCommand = ReactiveCommand.CreateFromTask(() => LoadAsync(rebuild: true));
            CloseCommand = ReactiveCommand.Create(() => { _ownerWindow?.Close(); });

            this.WhenAnyValue(vm => vm.SearchText)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => Rebuild());

            _ = LoadAsync(rebuild: false);
        }

        // ---- context -------------------------------------------------------------------

        public IconPickContext Context => _context;

        public string Title => _context.Purpose != IconPickPurpose.General && !string.IsNullOrWhiteSpace(_context.Name)
            ? $"Icon for “{_context.Name!.Trim()}”"
            : "Icon Library";

        public string Subtitle => _context.Purpose switch
        {
            IconPickPurpose.Category => "Line icons that match this category come first. Pick one, then choose its colour.",
            IconPickPurpose.Entry => "Logos closest to this entry come first.",
            _ => "Browse the library, or upload and download your own icons."
        };

        /// <summary>True when a pick goes back to whoever opened the library (category, entry, picker).</summary>
        public bool IsPickerMode
        {
            get => _isPickerMode;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isPickerMode, value);
                this.RaisePropertyChanged(nameof(ApplyButtonText));
                this.RaisePropertyChanged(nameof(CanApplySelection));
            }
        }

        public bool HasOwnerWindow => _ownerWindow != null;

        public void SetOwnerWindow(Window window, Window? callingOwner = null)
        {
            _ownerWindow = window;
            IsPickerMode = _context.Purpose != IconPickPurpose.General || callingOwner != null;
        }

        /// <summary>The chosen icon's path (a recoloured copy for line icons), set when a pick is applied.</summary>
        public string? ConfirmedIconPath
        {
            get => _confirmedIconPath;
            private set => this.RaiseAndSetIfChanged(ref _confirmedIconPath, value);
        }

        // ---- lists ---------------------------------------------------------------------

        public ObservableCollection<IconTileViewModel> VisibleIcons { get; } = new();
        public ObservableCollection<IconTileViewModel> Suggested { get; } = new();

        public bool HasSuggestions => Suggested.Count > 0;
        public string SuggestedTitle => _context.Purpose == IconPickPurpose.Entry ? "BEST MATCHES" : "SUGGESTED";
        public bool HasMore => _shown < _source.Count;
        public bool IsEmpty => !IsBusy && _source.Count == 0 && Suggested.Count == 0;
        public string CountText => $"{_source.Count + Suggested.Count:N0} icons";

        public string EmptyText => SelectedTabIndex switch
        {
            1 => "No downloaded icons yet. Use “Download icons” to fetch some.",
            2 => "Nothing here yet. Upload an icon, or download one.",
            _ => string.IsNullOrWhiteSpace(SearchText) ? "No icons found." : $"No icons match “{SearchText.Trim()}”."
        };

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isBusy, value);
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
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

        /// <summary>0 = Library, 1 = Favicons (downloaded), 2 = My icons (uploaded + downloaded).</summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (value == _selectedTabIndex) return;
                this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
                this.RaisePropertyChanged(nameof(IsLibraryTab));
                this.RaisePropertyChanged(nameof(IsDownloadsTab));
                this.RaisePropertyChanged(nameof(IsMyIconsTab));
                SelectedTile = null;
                Rebuild();
            }
        }

        public bool IsLibraryTab => SelectedTabIndex == 0;
        public bool IsDownloadsTab => SelectedTabIndex == 1;
        public bool IsMyIconsTab => SelectedTabIndex == 2;

        public string[] LibraryFilters { get; } = { "All icons", "Line icons", "Logos" };

        public int LibraryFilterIndex
        {
            get => _libraryFilterIndex;
            set
            {
                if (value == _libraryFilterIndex) return;
                this.RaiseAndSetIfChanged(ref _libraryFilterIndex, value);
                Rebuild();
            }
        }

        // ---- selection + colour --------------------------------------------------------

        public IconTileViewModel? SelectedTile
        {
            get => _selectedTile;
            set
            {
                if (ReferenceEquals(_selectedTile, value)) return;
                if (_selectedTile != null) _selectedTile.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedTile, value);
                if (_selectedTile != null) _selectedTile.IsSelected = true;

                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(CanRecolour));
                this.RaisePropertyChanged(nameof(ShowTintedPreview));
                this.RaisePropertyChanged(nameof(ShowPlainPreview));
                this.RaisePropertyChanged(nameof(CanDeleteSelected));
                this.RaisePropertyChanged(nameof(CanApplySelection));
            }
        }

        public bool HasSelection => SelectedTile != null;
        public bool CanRecolour => SelectedTile?.IsTintable == true;
        public bool ShowTintedPreview => CanRecolour && !KeepOriginalColours;
        public bool ShowPlainPreview => HasSelection && !ShowTintedPreview;
        public bool CanDeleteSelected => SelectedTile?.CanDelete == true;

        public Color SelectedColour
        {
            get => _selectedColour;
            set
            {
                var opaque = Color.FromRgb(value.R, value.G, value.B);
                if (opaque == _selectedColour) return;
                this.RaiseAndSetIfChanged(ref _selectedColour, opaque);
                this.RaisePropertyChanged(nameof(HexText));
                this.RaisePropertyChanged(nameof(PreviewBrush));
            }
        }

        public IBrush PreviewBrush => new SolidColorBrush(SelectedColour);

        public string HexText
        {
            get => $"#{SelectedColour.R:X2}{SelectedColour.G:X2}{SelectedColour.B:X2}";
            set
            {
                var text = value?.Trim() ?? string.Empty;
                if (text.Length > 0 && text[0] != '#') text = "#" + text;
                if (Color.TryParse(text, out var parsed))
                    SelectedColour = parsed;
                else
                    this.RaisePropertyChanged(); // put the last good value back in the box
            }
        }

        /// <summary>Keep a line icon's shipped colour instead of recolouring it.</summary>
        public bool KeepOriginalColours
        {
            get => _keepOriginalColours;
            set
            {
                this.RaiseAndSetIfChanged(ref _keepOriginalColours, value);
                this.RaisePropertyChanged(nameof(ShowTintedPreview));
                this.RaisePropertyChanged(nameof(ShowPlainPreview));
                this.RaisePropertyChanged(nameof(CanApplySelection));
            }
        }

        /// <summary>The ten colours the category glyphs used to ship in, plus white and the accent.</summary>
        public ColourSwatch[] PresetColours { get; } =
        {
            new("Charcoal", Color.Parse("#3A4452")),
            new("Semi-dark pastel blue", Color.Parse("#5A7AB0")),
            new("Electric blue", Color.Parse("#2F7BFF")),
            new("Aqua", Color.Parse("#3FD0D4")),
            new("Teal", Color.Parse("#1FA59A")),
            new("Purple", Color.Parse("#9B6BFF")),
            new("Baby pink", Color.Parse("#F7B6C8")),
            new("Pink red peach", Color.Parse("#F58F7C")),
            new("Deeper pink red", Color.Parse("#E0445E")),
            new("Golden pastel yellow", Color.Parse("#F4D06F")),
            new("Signal", Color.Parse("#7FC8DC")),
            new("White", Color.Parse("#FFFFFF")),
        };

        public string ApplyButtonText => IsPickerMode ? "Use this icon" : "Save copy to My icons";

        /// <summary>
        /// In picker mode any icon can be applied. When just browsing, the only thing to "apply" is
        /// saving a recoloured line icon to My icons.
        /// </summary>
        public bool CanApplySelection =>
            SelectedTile != null && (IsPickerMode || (SelectedTile.IsTintable && !KeepOriginalColours));

        // ---- commands ------------------------------------------------------------------

        public ReactiveCommand<IconTileViewModel?, Unit> SelectIconCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; }
        public ReactiveCommand<Color, Unit> SetPresetColourCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportIconCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadMoreCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenMyIconsFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> RevealSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        // ---- loading -------------------------------------------------------------------

        private async Task LoadAsync(bool rebuild)
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Organising icons…";

                _index = await GetIndexAsync(_iconManager.IconsDirectory, rebuild);

                int libraryCount = _index.LineIcons.Count + _index.Logos.Count;
                StatusMessage = _index.DuplicatesRemoved > 0
                    ? $"{libraryCount:N0} icons · {_index.DuplicatesRemoved:N0} duplicates hidden"
                    : $"{libraryCount:N0} icons";

                Rebuild();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconLibrary] Failed to build the icon library.");
                StatusMessage = "The icon library could not be loaded. Try Refresh.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>The index is shared for the session; it is rebuilt after uploads, deletes and downloads.</summary>
        private static Task<IconLibraryIndex> GetIndexAsync(string visualsRoot, bool rebuild)
        {
            lock (IndexGate)
            {
                if (rebuild || _sharedIndex == null || _sharedIndex.IsFaulted || _sharedIndex.IsCanceled)
                    _sharedIndex = Task.Run(() => IconLibraryIndex.Build(IconLibraryIndex.DefaultSources(visualsRoot)));
                return _sharedIndex;
            }
        }

        private void Rebuild()
        {
            if (_index == null) return;

            var query = SearchText?.Trim() ?? string.Empty;
            var pool = PoolForCurrentTab().ToList();

            Suggested.Clear();
            List<IconItem> ordered;

            if (query.Length > 0)
            {
                ordered = Search(pool, query);
            }
            else
            {
                ordered = pool;
                if (SelectedTabIndex == 0 && _context.Purpose != IconPickPurpose.General)
                {
                    var queries = _context.Purpose == IconPickPurpose.Entry
                        ? IconRanker.QueriesForEntry(_context.Name, _context.Url)
                        : IconRanker.QueriesForCategory(_context.Name);

                    var best = IconRanker.Rank(pool, queries, SuggestionThreshold, SuggestionCount).ToList();

                    // Entry types with a logo family (cards, bank accounts): offer every logo of
                    // that family, uncapped. Order: family logos that also match the name
                    // ("Mastercard" for "Mastercard Gold"), then the rest of the family. Loose
                    // title-word hits outside the family ("Gold …") are dropped unless near-exact.
                    var family = _context.Purpose == IconPickPurpose.Entry
                        ? IconRanker.MatchKind(pool, _context.Kind)
                        : Array.Empty<IconItem>();

                    if (family.Count > 0)
                    {
                        var familySet = new HashSet<IconItem>(family);
                        var nameAndFamily = best.Where(familySet.Contains).ToList();
                        var strongOutside = best
                            .Where(i => !familySet.Contains(i) && IconRanker.Score(i, queries) >= 0.95)
                            .ToList();
                        var picked = new HashSet<IconItem>(nameAndFamily.Concat(strongOutside));
                        best = nameAndFamily
                            .Concat(strongOutside)
                            .Concat(family.Where(i => !picked.Contains(i)))
                            .ToList();
                    }

                    foreach (var item in best) Suggested.Add(TileFor(item));

                    var suggested = new HashSet<IconItem>(best);
                    ordered = pool.Where(i => !suggested.Contains(i)).ToList();
                }
            }

            _source = ordered.Select(TileFor).ToList();
            _shown = 0;
            VisibleIcons.Clear();
            LoadMore();

            this.RaisePropertyChanged(nameof(HasSuggestions));
            this.RaisePropertyChanged(nameof(CountText));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(EmptyText));
        }

        private IEnumerable<IconItem> PoolForCurrentTab()
        {
            var index = _index!;
            return SelectedTabIndex switch
            {
                1 => index.Downloads,
                2 => index.Uploads.Concat(index.Downloads),
                _ => LibraryFilterIndex switch
                {
                    1 => index.LineIcons,
                    2 => index.Logos,
                    // Categories want glyphs first; entries want brand logos first.
                    _ => _context.Purpose == IconPickPurpose.Entry
                        ? index.Logos.Concat(index.LineIcons)
                        : index.LineIcons.Concat(index.Logos)
                }
            };
        }

        private static List<IconItem> Search(List<IconItem> pool, string query)
        {
            var key = IconRanker.Normalize(query);
            if (key.Length == 0) return pool;

            // One or two characters: plain prefix match; fuzzy ranking needs more to go on.
            if (key.Length < 3)
                return pool.Where(i => i.MatchKey.StartsWith(key, StringComparison.Ordinal)).ToList();

            return IconRanker.Rank(pool, new[] { query }, SearchThreshold, int.MaxValue).ToList();
        }

        /// <summary>Shows the next batch and starts decoding the one after it in the background.</summary>
        public void LoadMore()
        {
            if (_shown >= _source.Count) return;

            var batch = _source.Skip(_shown).Take(BatchSize).ToList();
            foreach (var tile in batch) VisibleIcons.Add(tile);
            _shown += batch.Count;
            this.RaisePropertyChanged(nameof(HasMore));

            var upcoming = _source.Skip(_shown).Take(BatchSize).ToList();
            if (upcoming.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    foreach (var tile in upcoming) tile.Prefetch();
                });
            }
        }

        private IconTileViewModel TileFor(IconItem item)
        {
            if (_tiles.TryGetValue(item.FilePath, out var existing) && existing.Item == item)
                return existing;

            var tile = new IconTileViewModel(item);
            _tiles[item.FilePath] = tile;
            return tile;
        }

        // ---- actions -------------------------------------------------------------------

        private async Task ApplyAsync()
        {
            var tile = SelectedTile;
            if (tile == null) return;

            try
            {
                var path = tile.FilePath;
                if (tile.IsTintable && !KeepOriginalColours)
                {
                    var colour = SelectedColour;
                    // Picks are written where they will not clutter My icons; a browse-mode save is
                    // meant to show up there.
                    var folder = IsPickerMode ? IconLibraryIndex.RecolouredDirectory : IconLibraryIndex.UserIconsDirectory;
                    path = await Task.Run(() => IconRecolour.SaveRecoloured(tile.FilePath, colour, folder, tile.Name));
                }

                if (!IsPickerMode)
                {
                    await LoadAsync(rebuild: true);
                    StatusMessage = $"Saved “{Path.GetFileName(path)}” to My icons.";
                    return;
                }

                ConfirmedIconPath = path;
                _ownerWindow?.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconLibrary] Failed to apply an icon.");
                await _dialogService.ShowErrorAsync("Icon", "The icon could not be prepared. Try another icon or colour.", _ownerWindow);
            }
        }

        private async Task ImportIconsAsync()
        {
            if (_ownerWindow?.StorageProvider == null) return;

            try
            {
                var patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.svg", "*.ico", "*.webp" };
                var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Upload icons",
                    AllowMultiple = true,
                    FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = patterns } }
                });
                if (files == null || files.Count == 0) return;

                Directory.CreateDirectory(IconLibraryIndex.UserIconsDirectory);

                int uploaded = 0;
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file.Name);
                    if (string.IsNullOrWhiteSpace(name) || !patterns.Contains("*" + Path.GetExtension(name).ToLowerInvariant()))
                        continue;

                    var target = UniquePath(Path.Combine(IconLibraryIndex.UserIconsDirectory, name));
                    await using var source = await file.OpenReadAsync();
                    await using var destination = File.Create(target);
                    await source.CopyToAsync(destination);
                    uploaded++;
                }

                await LoadAsync(rebuild: true);
                SelectedTabIndex = 2;
                StatusMessage = uploaded == 1 ? "Uploaded 1 icon." : $"Uploaded {uploaded} icons.";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[IconLibrary] Upload failed.");
                await _dialogService.ShowErrorAsync("Upload", "The icon could not be uploaded. Check that it is a supported image and try again.", _ownerWindow);
            }
        }

        private async Task DeleteSelectedAsync()
        {
            var tile = SelectedTile;
            if (tile == null || !tile.CanDelete) return;

            var confirm = await _dialogService.ShowConfirmationAsync("Delete Icon", $"Delete “{tile.Name}” from your icons?", _ownerWindow);
            if (!confirm) return;

            try
            {
                File.Delete(tile.FilePath);
                SelectedTile = null;
                await LoadAsync(rebuild: true);
                StatusMessage = $"Deleted “{tile.Name}”.";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[IconLibrary] Failed to delete an icon.");
                await _dialogService.ShowErrorAsync("Delete Icon", "The icon could not be deleted. Close anything using it and try again.", _ownerWindow);
            }
        }

        private async Task DownloadMoreAsync()
        {
            try
            {
                var downloaderViewModel = new IconDownloaderViewModel();
                var window = new IconDownloaderWindow { DataContext = downloaderViewModel };
                downloaderViewModel.SetOwnerWindow(window);

                if (_ownerWindow != null)
                    await window.ShowDialog(_ownerWindow);
                else
                    window.Show();

                await LoadAsync(rebuild: true);
                SelectedTabIndex = 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IconLibrary] Failed to open the icon downloader.");
                await _dialogService.ShowErrorAsync("Icon Library", "The icon downloader could not be opened. Try again.", _ownerWindow);
            }
        }

        private void RevealSelected()
        {
            var path = SelectedTile?.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[IconLibrary] Failed to reveal an icon.");
            }
        }

        private void OpenMyIconsFolder()
        {
            try
            {
                Directory.CreateDirectory(IconLibraryIndex.UserIconsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = IconLibraryIndex.UserIconsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[IconLibrary] Failed to open the My icons folder.");
            }
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var directory = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
                if (!File.Exists(candidate)) return candidate;
            }
        }
    }

    /// <summary>One icon as shown in the picker grid.</summary>
    public sealed class IconTileViewModel : ReactiveObject
    {
        private Avalonia.Media.Imaging.Bitmap? _bitmap;
        private bool _isSelected;

        public IconTileViewModel(IconItem item)
        {
            Item = item;
        }

        public IconItem Item { get; }
        public string Name => Item.Name;
        public string FilePath => Item.FilePath;

        /// <summary>Line icon: drawn as a silhouette in the chosen colour.</summary>
        public bool IsTintable => Item.IsTintable;

        /// <summary>Logo or user icon: drawn with its own colours.</summary>
        public bool IsPlain => !Item.IsTintable;

        /// <summary>Only the user's own icons can be deleted, never the shipped library.</summary>
        public bool CanDelete => Item.Kind is IconKind.Upload or IconKind.Download;

        public string KindLabel => Item.Kind switch
        {
            IconKind.LineIcon => "Line icon · recolourable",
            IconKind.Logo => "Logo",
            IconKind.Upload => "Uploaded",
            _ => "Downloaded"
        };

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public Avalonia.Media.Imaging.Bitmap? Bitmap => _bitmap ??= IconBitmapCache.GetOrAdd(FilePath);

        /// <summary>Decodes into the shared cache off the UI thread so the tile paints at once when shown.</summary>
        public void Prefetch() => IconBitmapCache.GetOrAdd(FilePath);
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
                    if (!string.IsNullOrEmpty(_iconPath) && System.IO.File.Exists(_iconPath))
                    {
                        var bmp = IconBitmapCache.GetOrAdd(_iconPath);
                        if (bmp != null)
                        {
                            _iconBitmap = bmp;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IconFileEntryViewModel] Bitmap load failed for {_iconPath}: {ex.Message}");
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
