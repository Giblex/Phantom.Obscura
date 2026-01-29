using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for secure Flaticon icon downloader window.
    /// </summary>
    public sealed class IconDownloaderViewModel : ReactiveObject
    {
        private readonly SecureIconDownloaderService _iconService;
        private Window? _ownerWindow;

        private bool _isInternetEnabled;
        private string _internetStatusText = "🔴 Internet Disconnected - API Key Required";
        private string _internetStatusColor = "#DC3545"; // Red
        private string _searchQuery = "";
        private string _statusMessage = "Flaticon API key required. Get your free key at: https://www.flaticon.com/api";
        private string _cacheInfo = "";
        private string _apiKey = "";
        private bool _isApiKeyConfigured;

        public IconDownloaderViewModel()
        {
            _iconService = new SecureIconDownloaderService();

            // Initialize commands
            EnableInternetCommand = ReactiveCommand.Create(EnableInternet);
            DisableInternetCommand = ReactiveCommand.Create(DisableInternet);
            SearchIconsCommand = ReactiveCommand.CreateFromTask(SearchIconsAsync);
            DownloadIconCommand = ReactiveCommand.CreateFromTask<IconSearchResult>(DownloadIconAsync);
            ClearCacheCommand = ReactiveCommand.CreateFromTask(ClearCacheAsync);
            SaveApiKeyCommand = ReactiveCommand.Create(SaveApiKey);
            CloseCommand = ReactiveCommand.Create(Close);

            UpdateCacheInfo();
            CheckApiKeyStatus();
        }

        // Properties
        public bool IsInternetEnabled
        {
            get => _isInternetEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isInternetEnabled, value);
        }

        public string InternetStatusText
        {
            get => _internetStatusText;
            private set => this.RaiseAndSetIfChanged(ref _internetStatusText, value);
        }

        public string InternetStatusColor
        {
            get => _internetStatusColor;
            private set => this.RaiseAndSetIfChanged(ref _internetStatusColor, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string CacheInfo
        {
            get => _cacheInfo;
            private set => this.RaiseAndSetIfChanged(ref _cacheInfo, value);
        }

        public string ApiKey
        {
            get => _apiKey;
            set => this.RaiseAndSetIfChanged(ref _apiKey, value);
        }

        public bool IsApiKeyConfigured
        {
            get => _isApiKeyConfigured;
            private set => this.RaiseAndSetIfChanged(ref _isApiKeyConfigured, value);
        }

        public ObservableCollection<IconSearchResult> SearchResults { get; } = new();

        // Commands
        public ReactiveCommand<Unit, Unit> EnableInternetCommand { get; }
        public ReactiveCommand<Unit, Unit> DisableInternetCommand { get; }
        public ReactiveCommand<Unit, Unit> SearchIconsCommand { get; }
        public ReactiveCommand<IconSearchResult, Unit> DownloadIconCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearCacheCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveApiKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        // Methods
        private void CheckApiKeyStatus()
        {
            // Check if API key is already configured
            if (_iconService.HasApiKeyConfigured)
            {
                IsApiKeyConfigured = true;
                InternetStatusText = "🟡 API Key Configured - Ready to Connect";
                InternetStatusColor = "#FFC107"; // Yellow
                StatusMessage = "API key configured. Click 'Enable Internet' to start searching.";
            }
            else
            {
                IsApiKeyConfigured = false;
                InternetStatusText = "🔴 API Key Required";
                InternetStatusColor = "#DC3545"; // Red
                StatusMessage = "Flaticon API key required. Enter your API key below and click 'Save API Key'.";
            }
        }

        private void SaveApiKey()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                StatusMessage = "Please enter a valid API key.";
                return;
            }

            try
            {
                _iconService.SetApiKey(ApiKey);
                IsApiKeyConfigured = true;
                InternetStatusText = "🟡 API Key Saved - Ready to Connect";
                InternetStatusColor = "#FFC107"; // Yellow
                StatusMessage = "API key saved successfully! You can now enable internet and search for icons.";
                ApiKey = ""; // Clear the input for security
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save API key: {ex.Message}";
            }
        }

        private void EnableInternet()
        {
            if (!IsApiKeyConfigured)
            {
                StatusMessage = "Please configure your Flaticon API key first.";
                return;
            }

            try
            {
                _iconService.EnableInternet();
                IsInternetEnabled = true;
                InternetStatusText = "🟢 Connected to Flaticon API (Authenticated HTTPS)";
                InternetStatusColor = "#28A745"; // Green
                StatusMessage = "Secure connection established. You can now search for authentic Flaticon icons.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to enable internet: {ex.Message}";
                IsApiKeyConfigured = false; // May need to reconfigure
            }
        }

        private void DisableInternet()
        {
            try
            {
                _iconService.DisableInternet();
                IsInternetEnabled = false;
                InternetStatusText = "🔴 Internet Disconnected";
                InternetStatusColor = "#DC3545"; // Red
                StatusMessage = "Internet disabled. All connections closed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to disable internet: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task SearchIconsAsync()
        {
            if (!IsInternetEnabled)
            {
                StatusMessage = "Please enable internet first before searching.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                StatusMessage = "Please enter a search query.";
                return;
            }

            try
            {
                StatusMessage = $"🔍 Searching for '{SearchQuery}'...";
                SearchResults.Clear();

                var results = await _iconService.SearchIconsAsync(SearchQuery);

                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }

                StatusMessage = $"Found {results.Count} icons for '{SearchQuery}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
                // Auto-disable internet on error
                DisableInternet();
            }
        }

        private async System.Threading.Tasks.Task DownloadIconAsync(IconSearchResult icon)
        {
            if (!IsInternetEnabled)
            {
                StatusMessage = "Please enable internet first before downloading.";
                return;
            }

            try
            {
                StatusMessage = $"⬇ Downloading '{icon.Name}'...";

                var savedIcon = await _iconService.DownloadIconAsync(icon);

                StatusMessage = $"Downloaded '{icon.Name}' - Icon: {savedIcon}";
                UpdateCacheInfo();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
                // Auto-disable internet on error
                DisableInternet();
            }
        }

        private async System.Threading.Tasks.Task ClearCacheAsync()
        {
            try
            {
                var cachedIcons = _iconService.GetCachedIcons();
                _iconService.ClearCache();
                UpdateCacheInfo();
                StatusMessage = $"Cleared {cachedIcons.Count} cached icons";

                // Show a simple message dialog (simulate for now)
                await System.Threading.Tasks.Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to clear cache: {ex.Message}";
            }
        }

        private void UpdateCacheInfo()
        {
            var cachedIcons = _iconService.GetCachedIcons();
            CacheInfo = cachedIcons.Count == 0
                ? "📦 Cache: Empty"
                : $"📦 Cache: {cachedIcons.Count} icons stored locally";
        }

        /// <summary>
        /// Sets the credential context for auto-generating search queries.
        /// This will automatically populate the search box with relevant terms.
        /// </summary>
        public void SetCredentialContext(PhantomVault.Core.Models.Credential credential)
        {
            if (credential == null)
                return;

            // Use IconManager to generate smart search query
            var iconsDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Icons"
            );

            var iconManager = new PhantomVault.Core.Services.IconManager(iconsDir);
            var suggestedQuery = iconManager.GenerateSearchQuery(credential);

            SearchQuery = suggestedQuery;
            StatusMessage = $"💡 Auto-suggested search for '{credential.Title}': {suggestedQuery}";
        }

        /// <summary>
        /// Downloads an icon with a custom filename based on credential info.
        /// </summary>
        public async System.Threading.Tasks.Task DownloadIconForCredentialAsync(
            IconSearchResult icon,
            PhantomVault.Core.Models.Credential credential)
        {
            if (!IsInternetEnabled)
            {
                StatusMessage = "Please enable internet first before downloading.";
                return;
            }

            try
            {
                StatusMessage = $"⬇ Downloading '{icon.Name}' for {credential.Title}...";

                // Get suggested filename from IconManager
                var iconsDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets", "Icons"
                );

                var iconManager = new PhantomVault.Core.Services.IconManager(iconsDir);
                var suggestedFilename = iconManager.GetSuggestedIconFilename(credential);

                // Download with custom filename to Assets/Icons
                var savedIcon = await _iconService.DownloadIconAsync(icon, suggestedFilename, iconsDir);

                StatusMessage = $"Downloaded '{icon.Name}' as '{suggestedFilename}.png' - Icon: {savedIcon}";
                UpdateCacheInfo();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
                // Auto-disable internet on error
                DisableInternet();
            }
        }

        private void Close()
        {
            // Ensure internet is disabled when closing
            if (IsInternetEnabled)
            {
                DisableInternet();
            }

            _iconService.Dispose();
            _ownerWindow?.Close();
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }
    }
}
