using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    public sealed class IconPickerViewModel : ReactiveObject
    {
        private Window? _ownerWindow;
        private string _selectedIcon = string.Empty;
        private string _searchText = string.Empty;
        private string _statusMessage = "Select an icon or search categories";
        private Color _selectedIconColor = Color.Parse("#FFB5E5FF"); // Default pastel blue
        private readonly IconManager? _iconManager;
        private SecureIconDownloaderService? _iconDownloader; // Not readonly - lazy initialized
        private readonly DialogService _dialogService = new();

        // Asset and Flaticon icons
        private string? _autoDetectedAssetPath;
        private Bitmap? _autoDetectedAssetBitmap;
        private bool _hasAutoDetectedAsset;
        private bool _isSearchingFlaticon;
        private ObservableCollection<FlaticonResult> _flaticonResults = new();
        private FlaticonResult? _selectedFlaticonResult;

        // Available icon files from Assets/Icons/Logos/Coloured Icons
        public ObservableCollection<string> AvailableIconPaths { get; } = new();

        // Pastel color options for icon backgrounds
        public Color[] AvailableColors { get; } = new[]
        {
            Color.Parse("#FFB5E5FF"), // Pastel Blue
            Color.Parse("#FFFFC1E3"), // Pastel Pink
            Color.Parse("#FFFFDFBB"), // Pastel Peach
            Color.Parse("#FFC7E5C7"), // Pastel Green
            Color.Parse("#FFFFE5B4"), // Pastel Yellow
            Color.Parse("#FFE5D4FF"), // Pastel Purple
            Color.Parse("#FFFFC9C9"), // Pastel Red
            Color.Parse("#FFD4F4FF"), // Pastel Cyan
            Color.Parse("#FFFFE4F0"), // Pastel Rose
            Color.Parse("#FFE8F5E9")  // Pastel Mint
        };

        public IconPickerViewModel(string currentIcon = "", Color? currentColor = null, string? searchHint = null, IconManager? iconManager = null)
        {
            _selectedIcon = currentIcon;
            _selectedIconColor = currentColor ?? Color.Parse("#FFB5E5FF");
            _iconManager = iconManager;

            // Don't initialize icon downloader here - it will be created lazily when needed
            _iconDownloader = null;

            // Load available icons from Coloured Icons folder
            LoadAvailableIcons();

            SelectIconCommand = ReactiveCommand.Create<string>(SelectIcon);
            SelectColorCommand = ReactiveCommand.Create<Color>(SelectColor);
            ConfirmCommand = ReactiveCommand.Create(Confirm);
            CancelCommand = ReactiveCommand.Create(Cancel);
            ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
            SearchFlaticonCommand = ReactiveCommand.CreateFromTask(SearchFlaticonAsync);
            SelectFlaticonResultCommand = ReactiveCommand.CreateFromTask<FlaticonResult>(SelectFlaticonResultAsync);
            BrowseIconLibraryCommand = ReactiveCommand.CreateFromTask(BrowseIconLibraryAsync);
            UploadCustomIconCommand = ReactiveCommand.CreateFromTask(UploadCustomIconAsync);

            // Subscribe to search changes
            this.WhenAnyValue(x => x.SearchText)
                .Subscribe(_ => UpdateCategoryVisibility());

            // Auto-detect asset icon if search hint provided
            if (!string.IsNullOrWhiteSpace(searchHint))
            {
                SearchText = searchHint;
                _ = AutoDetectAssetIconAsync(searchHint);
            }
        }

        private void LoadAvailableIcons()
        {
            try
            {
                // Get the base directory for the application
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var iconsPath = Path.Combine(baseDir, "Assets", "Icons", "Logos", "Coloured Icons");

                if (!Directory.Exists(iconsPath))
                {
                    Debug.WriteLine($"[ICON-PICKER] Icons directory not found: {iconsPath}");
                    return;
                }

                // Recursively find all PNG files in Coloured Icons folder
                var pngFiles = Directory.GetFiles(iconsPath, "*.png", SearchOption.AllDirectories);

                foreach (var file in pngFiles.OrderBy(f => Path.GetFileName(f)))
                {
                    // Convert to relative path for Assets
                    var relativePath = file.Replace(baseDir, "").Replace("\\", "/").TrimStart('/');
                    AvailableIconPaths.Add($"/{relativePath}");

                    if (AvailableIconPaths.Count >= 100) break; // Limit to first 100 icons for performance
                }

                Debug.WriteLine($"[ICON-PICKER] Loaded {AvailableIconPaths.Count} icons from {iconsPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-PICKER] Error loading icons: {ex.Message}");
            }
        }

        private SecureIconDownloaderService? EnsureIconDownloader()
        {
            if (_iconDownloader == null)
            {
                try
                {
                    _iconDownloader = new SecureIconDownloaderService();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ICON-PICKER] Failed to initialize icon downloader: {ex.Message}");
                    return null;
                }
            }
            return _iconDownloader;
        }

        public string SelectedIcon
        {
            get => _selectedIcon;
            private set => this.RaiseAndSetIfChanged(ref _selectedIcon, value);
        }

        public Color SelectedIconColor
        {
            get => _selectedIconColor;
            set => this.RaiseAndSetIfChanged(ref _selectedIconColor, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => this.RaiseAndSetIfChanged(ref _searchText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        // Category visibility
        private bool _showSocialCategory = true;
        private bool _showTechCategory = true;
        private bool _showFinanceCategory = true;
        private bool _showEntertainmentCategory = true;
        private bool _showSecurityCategory = true;
        private bool _showWorkCategory = true;
        private bool _showCloudCategory = true;
        private bool _showMiscCategory = true;

        public bool ShowSocialCategory
        {
            get => _showSocialCategory;
            set => this.RaiseAndSetIfChanged(ref _showSocialCategory, value);
        }

        public bool ShowTechCategory
        {
            get => _showTechCategory;
            set => this.RaiseAndSetIfChanged(ref _showTechCategory, value);
        }

        public bool ShowFinanceCategory
        {
            get => _showFinanceCategory;
            set => this.RaiseAndSetIfChanged(ref _showFinanceCategory, value);
        }

        public bool ShowEntertainmentCategory
        {
            get => _showEntertainmentCategory;
            set => this.RaiseAndSetIfChanged(ref _showEntertainmentCategory, value);
        }

        public bool ShowSecurityCategory
        {
            get => _showSecurityCategory;
            set => this.RaiseAndSetIfChanged(ref _showSecurityCategory, value);
        }

        public bool ShowWorkCategory
        {
            get => _showWorkCategory;
            set => this.RaiseAndSetIfChanged(ref _showWorkCategory, value);
        }

        public bool ShowCloudCategory
        {
            get => _showCloudCategory;
            set => this.RaiseAndSetIfChanged(ref _showCloudCategory, value);
        }

        public bool ShowMiscCategory
        {
            get => _showMiscCategory;
            set => this.RaiseAndSetIfChanged(ref _showMiscCategory, value);
        }

        // Commands
        public ReactiveCommand<string, Unit> SelectIconCommand { get; }
        public ReactiveCommand<Color, Unit> SelectColorCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
        public ReactiveCommand<Unit, Unit> SearchFlaticonCommand { get; }
        public ReactiveCommand<FlaticonResult, Unit> SelectFlaticonResultCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseIconLibraryCommand { get; }
        public ReactiveCommand<Unit, Unit> UploadCustomIconCommand { get; }

        // Properties for asset and Flaticon icons
        public string? AutoDetectedAssetPath
        {
            get => _autoDetectedAssetPath;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedAssetPath, value);
        }

        public Bitmap? AutoDetectedAssetBitmap
        {
            get => _autoDetectedAssetBitmap;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedAssetBitmap, value);
        }

        public bool HasAutoDetectedAsset
        {
            get => _hasAutoDetectedAsset;
            private set => this.RaiseAndSetIfChanged(ref _hasAutoDetectedAsset, value);
        }

        public bool IsSearchingFlaticon
        {
            get => _isSearchingFlaticon;
            private set => this.RaiseAndSetIfChanged(ref _isSearchingFlaticon, value);
        }

        public ObservableCollection<FlaticonResult> FlaticonResults
        {
            get => _flaticonResults;
            private set => this.RaiseAndSetIfChanged(ref _flaticonResults, value);
        }

        public FlaticonResult? SelectedFlaticonResult
        {
            get => _selectedFlaticonResult;
            set => this.RaiseAndSetIfChanged(ref _selectedFlaticonResult, value);
        }

        private void SelectIcon(string icon)
        {
            SelectedIcon = icon;
            StatusMessage = $"Selected: {icon}";
        }

        private void SelectColor(Color color)
        {
            SelectedIconColor = color;
        }

        private void Confirm()
        {
            _ownerWindow?.Close(SelectedIcon);
        }

        private void Cancel()
        {
            _ownerWindow?.Close(null);
        }

        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        private void UpdateCategoryVisibility()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Show all categories
                ShowSocialCategory = true;
                ShowTechCategory = true;
                ShowFinanceCategory = true;
                ShowEntertainmentCategory = true;
                ShowSecurityCategory = true;
                ShowWorkCategory = true;
                ShowCloudCategory = true;
                ShowMiscCategory = true;
                StatusMessage = "Select an icon or search categories";
            }
            else
            {
                var search = SearchText.ToLower();
                ShowSocialCategory = "social communication mail message chat phone mobile".Contains(search);
                ShowTechCategory = "tech technology computer web internet device".Contains(search);
                ShowFinanceCategory = "finance money bank card payment shopping".Contains(search);
                ShowEntertainmentCategory = "entertainment music game video camera photo media".Contains(search);
                ShowSecurityCategory = "security privacy lock key shield safe password".Contains(search);
                ShowWorkCategory = "work productivity document file folder office".Contains(search);
                ShowCloudCategory = "cloud storage drive backup".Contains(search);
                ShowMiscCategory = "misc settings tools home travel education".Contains(search);
                StatusMessage = $"Searching for: {SearchText}";
            }
        }

        private async Task AutoDetectAssetIconAsync(string searchTerm)
        {
            if (_iconManager == null) return;

            try
            {
                // Create temp credential for icon detection
                var tempCredential = new Core.Models.Credential
                {
                    Title = searchTerm
                };

                var iconPath = _iconManager.FindIconPathForCredential(tempCredential);
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            AutoDetectedAssetPath = iconPath;
                            AutoDetectedAssetBitmap = new Bitmap(iconPath);
                            HasAutoDetectedAsset = true;
                            StatusMessage = $"Found matching icon in assets for '{searchTerm}'";
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ICON-PICKER] Failed to load asset bitmap: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-PICKER] Auto-detect failed: {ex.Message}");
            }
        }

        private async Task SearchFlaticonAsync()
        {
            var downloader = EnsureIconDownloader();

            if (string.IsNullOrWhiteSpace(SearchText) || downloader == null)
            {
                await _dialogService.ShowWarningAsync("Search Flaticon",
                    downloader == null ? "Icon downloader is not available" : "Please enter a search term",
                    _ownerWindow);
                return;
            }

            if (!downloader.HasApiKeyConfigured)
            {
                await _dialogService.ShowWarningAsync("Search Flaticon",
                    "Flaticon API key is required. Open the Icon Downloader to save your API key first.",
                    _ownerWindow);
                return;
            }

            try
            {
                IsSearchingFlaticon = true;
                StatusMessage = $"Searching Flaticon for '{SearchText}'...";

                downloader.EnableInternet();
                var results = await downloader.SearchIconsAsync(SearchText);
                downloader.DisableInternet();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FlaticonResults.Clear();
                    foreach (var result in results.Take(12)) // Show top 12 results
                    {
                        FlaticonResults.Add(new FlaticonResult
                        {
                            Id = result.Id,
                            Description = result.Name,
                            PreviewUrl = result.Url,
                            DownloadUrl = result.Url
                        });
                    }
                    StatusMessage = $"Found {results.Count} icons on Flaticon";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-PICKER] Flaticon search failed: {ex.Message}");
                await _dialogService.ShowErrorAsync("Search Failed", $"Failed to search Flaticon: {ex.Message}", _ownerWindow);
                StatusMessage = "Flaticon search failed";
            }
            finally
            {
                IsSearchingFlaticon = false;
                downloader?.DisableInternet();
            }
        }

        private async Task SelectFlaticonResultAsync(FlaticonResult result)
        {
            var downloader = EnsureIconDownloader();

            if (downloader == null || string.IsNullOrEmpty(result.DownloadUrl))
            {
                return;
            }

            if (!downloader.HasApiKeyConfigured)
            {
                await _dialogService.ShowWarningAsync("Download Icon",
                    "Flaticon API key is required. Open the Icon Downloader to save your API key first.",
                    _ownerWindow);
                return;
            }

            try
            {
                StatusMessage = $"Downloading icon from Flaticon...";

                // Convert to IconSearchResult
                var iconResult = new IconSearchResult
                {
                    Id = result.Id,
                    Name = result.Description,
                    Url = result.DownloadUrl
                };

                downloader.EnableInternet();
                var downloadedPath = await downloader.DownloadIconAsync(iconResult);
                downloader.DisableInternet();

                if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
                {
                    SelectedIcon = downloadedPath;
                    StatusMessage = $"Downloaded: {result.Description}";
                    Confirm();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ICON-PICKER] Download failed: {ex.Message}");
                await _dialogService.ShowErrorAsync("Download Failed", $"Failed to download icon: {ex.Message}", _ownerWindow);
            }
            finally
            {
                downloader?.DisableInternet();
            }
        }

        private async Task BrowseIconLibraryAsync()
        {
            try
            {
                await IconLibraryLauncher.ShowAsync(_ownerWindow, "Choose from Icon Library");
                // Note: Icon library returns selected icon via its own mechanism
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Icon Library", $"Failed to open icon library: {ex.Message}", _ownerWindow);
            }
        }

        private async Task UploadCustomIconAsync()
        {
            if (_ownerWindow?.StorageProvider == null)
            {
                await _dialogService.ShowWarningAsync("Upload Icon", "File picker unavailable", _ownerWindow);
                return;
            }

            try
            {
                var fileType = new Avalonia.Platform.Storage.FilePickerFileType("Image files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.ico", "*.svg" }
                };

                var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select custom icon",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { fileType }
                });

                if (files != null && files.Count > 0)
                {
                    var file = files[0];
                    if (file.Path.IsFile)
                    {
                        var localPath = file.Path.LocalPath;
                        if (!string.IsNullOrEmpty(localPath))
                        {
                            SelectedIcon = localPath;
                            StatusMessage = $"Selected custom icon: {Path.GetFileName(localPath)}";
                            Confirm();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Upload Failed", $"Failed to upload icon: {ex.Message}", _ownerWindow);
            }
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }
    }

    public sealed class FlaticonResult : ReactiveObject
    {
        private string _id = string.Empty;
        private string _description = string.Empty;
        private string _previewUrl = string.Empty;
        private string _downloadUrl = string.Empty;

        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        public string PreviewUrl
        {
            get => _previewUrl;
            set => this.RaiseAndSetIfChanged(ref _previewUrl, value);
        }

        public string DownloadUrl
        {
            get => _downloadUrl;
            set => this.RaiseAndSetIfChanged(ref _downloadUrl, value);
        }
    }
}
