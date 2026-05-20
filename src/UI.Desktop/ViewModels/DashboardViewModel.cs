using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;
using ReactiveUI;
using Serilog;

namespace PhantomVault.UI.ViewModels
{
    public class DashboardViewModel : ReactiveObject
    {
        // Note: These services would be injected in a real implementation
        // For now, making the ViewModel simpler without constructor dependencies

        private string _welcomeMessage = string.Empty;
        private int _totalCredentials;
        private int _totalCategories;
        private int _twoFactorCoverage;
        private bool _hasRecentActivity;
        private bool _hasFavorites;
        private bool _isQuickAccessOptionsOpen;
        private bool _showQuickAddPassword = true;
        private bool _showQuickGenerator = true;
        private bool _showQuickImport = true;
        private bool _showQuickExport = true;
        private bool _showQuickHealth = true;
        private bool _showQuickPasswords = true;
        private bool _showQuickFavorites = true;
        private bool _showQuickRecovery = true;
        private bool _showQuickTotp = true;
        private bool _showQuickSecurity = true;
        private bool _showQuickSettings = true;
        private bool _showQuickBackup = true;
        private bool _hasQuickAccessCredentials;
        private string _copiedFeedback = string.Empty;
        private bool _isRecoveryAvailable;
        private string _recoveryStatus = "Recovery is not available from this build entry point.";
        private readonly IntegratedAttestorService _integratedAttestorService = new();
        private bool _isAttestorAvailable;
        private string _attestorStatus = "PhantomAttestor is not available from this suite build.";
        private bool _isCategoryPinOptionsOpen;

        public DashboardViewModel()
        {
            RecentActivities = new ObservableCollection<ActivityItem>();
            FavoriteCredentials = new ObservableCollection<Credential>();
            QuickAccessCredentials = new ObservableCollection<CredentialViewModel>();
            Categories = new ObservableCollection<CategoryViewModel>();

            AddPasswordCommand = ReactiveCommand.CreateFromTask(AddPasswordAsync);
            OpenGeneratorCommand = ReactiveCommand.CreateFromTask(OpenGeneratorAsync);
            OpenImportCommand = ReactiveCommand.CreateFromTask(OpenImportAsync);
            OpenExportCommand = ReactiveCommand.Create(OpenExport);
            OpenPasswordHealthCommand = ReactiveCommand.CreateFromTask(OpenPasswordHealthAsync);
            ViewAllActivityCommand = ReactiveCommand.Create(ViewAllActivity);
            OpenCredentialCommand = ReactiveCommand.CreateFromTask<Credential>(OpenCredentialAsync);
            OpenPasswordsCommand = ReactiveCommand.Create(OpenPasswords);
            OpenFavoritesCommand = ReactiveCommand.Create(OpenFavorites);
            OpenRecoveryCommand = ReactiveCommand.Create(OpenRecovery);
            OpenPasswordHealthDashboardCommand = ReactiveCommand.Create(OpenPasswordHealthDashboard);
            OpenTotpVaultCommand = ReactiveCommand.Create(OpenTotpVault);
            LaunchPhantomAttestorCommand = ReactiveCommand.CreateFromTask(LaunchPhantomAttestorAsync);
            OpenSecurityOptionsCommand = ReactiveCommand.Create(OpenSecurityOptions);
            OpenSettingsCommand = ReactiveCommand.Create(OpenSettings);
            OpenBackupCommand = ReactiveCommand.Create(OpenBackup);
            CopyQuickAccessPasswordCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(CopyQuickAccessPasswordAsync);
            NavigateToCategoryCommand = ReactiveCommand.Create<CategoryViewModel>(NavigateToCategory);
            ToggleCategoryPinCommand = ReactiveCommand.Create<CategoryViewModel>(ToggleCategoryPin);

            _ = LoadDashboardDataAsync();
        }

        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set => this.RaiseAndSetIfChanged(ref _welcomeMessage, value);
        }

        public int TotalCredentials
        {
            get => _totalCredentials;
            set => this.RaiseAndSetIfChanged(ref _totalCredentials, value);
        }

        public int TotalCategories
        {
            get => _totalCategories;
            set => this.RaiseAndSetIfChanged(ref _totalCategories, value);
        }

        public int TwoFactorCoverage
        {
            get => _twoFactorCoverage;
            set => this.RaiseAndSetIfChanged(ref _twoFactorCoverage, value);
        }

        public bool HasRecentActivity
        {
            get => _hasRecentActivity;
            set => this.RaiseAndSetIfChanged(ref _hasRecentActivity, value);
        }

        public bool HasFavorites
        {
            get => _hasFavorites;
            set => this.RaiseAndSetIfChanged(ref _hasFavorites, value);
        }

        public bool IsQuickAccessOptionsOpen
        {
            get => _isQuickAccessOptionsOpen;
            set => this.RaiseAndSetIfChanged(ref _isQuickAccessOptionsOpen, value);
        }

        public bool ShowQuickAddPassword
        {
            get => _showQuickAddPassword;
            set => this.RaiseAndSetIfChanged(ref _showQuickAddPassword, value);
        }

        public bool ShowQuickGenerator
        {
            get => _showQuickGenerator;
            set => this.RaiseAndSetIfChanged(ref _showQuickGenerator, value);
        }

        public bool ShowQuickImport
        {
            get => _showQuickImport;
            set => this.RaiseAndSetIfChanged(ref _showQuickImport, value);
        }

        public bool ShowQuickHealth
        {
            get => _showQuickHealth;
            set => this.RaiseAndSetIfChanged(ref _showQuickHealth, value);
        }

        public bool ShowQuickPasswords
        {
            get => _showQuickPasswords;
            set => this.RaiseAndSetIfChanged(ref _showQuickPasswords, value);
        }

        public bool ShowQuickRecovery
        {
            get => _showQuickRecovery;
            set => this.RaiseAndSetIfChanged(ref _showQuickRecovery, value);
        }

        public bool ShowQuickTotp
        {
            get => _showQuickTotp;
            set => this.RaiseAndSetIfChanged(ref _showQuickTotp, value);
        }

        public bool ShowQuickSecurity
        {
            get => _showQuickSecurity;
            set => this.RaiseAndSetIfChanged(ref _showQuickSecurity, value);
        }

        public bool ShowQuickExport
        {
            get => _showQuickExport;
            set => this.RaiseAndSetIfChanged(ref _showQuickExport, value);
        }

        public bool ShowQuickFavorites
        {
            get => _showQuickFavorites;
            set => this.RaiseAndSetIfChanged(ref _showQuickFavorites, value);
        }

        public bool ShowQuickSettings
        {
            get => _showQuickSettings;
            set => this.RaiseAndSetIfChanged(ref _showQuickSettings, value);
        }

        public bool ShowQuickBackup
        {
            get => _showQuickBackup;
            set => this.RaiseAndSetIfChanged(ref _showQuickBackup, value);
        }

        public void ClearDashboard()
        {
            WelcomeMessage = string.Empty;
            TotalCredentials = 0;
            TotalCategories = 0;
            TwoFactorCoverage = 0;
            HasRecentActivity = false;
            HasFavorites = false;
            HasQuickAccessCredentials = false;
            RecentActivities.Clear();
            FavoriteCredentials.Clear();
            QuickAccessCredentials.Clear();
        }

        public ObservableCollection<ActivityItem> RecentActivities { get; }
        public ObservableCollection<Credential> FavoriteCredentials { get; }
        public ObservableCollection<CredentialViewModel> QuickAccessCredentials { get; }
        public ObservableCollection<CategoryViewModel> Categories { get; }

        /// <summary>Whether the category pin/unpin options panel is open.</summary>
        public bool IsCategoryPinOptionsOpen
        {
            get => _isCategoryPinOptionsOpen;
            set => this.RaiseAndSetIfChanged(ref _isCategoryPinOptionsOpen, value);
        }

        public bool HasQuickAccessCredentials
        {
            get => _hasQuickAccessCredentials;
            set => this.RaiseAndSetIfChanged(ref _hasQuickAccessCredentials, value);
        }

        public bool IsRecoveryAvailable
        {
            get => _isRecoveryAvailable;
            set => this.RaiseAndSetIfChanged(ref _isRecoveryAvailable, value);
        }

        public string RecoveryStatus
        {
            get => _recoveryStatus;
            set => this.RaiseAndSetIfChanged(ref _recoveryStatus, value);
        }

        public string CopiedFeedback
        {
            get => _copiedFeedback;
            set => this.RaiseAndSetIfChanged(ref _copiedFeedback, value);
        }

        public bool IsAttestorAvailable
        {
            get => _isAttestorAvailable;
            set => this.RaiseAndSetIfChanged(ref _isAttestorAvailable, value);
        }

        public string AttestorStatus
        {
            get => _attestorStatus;
            set => this.RaiseAndSetIfChanged(ref _attestorStatus, value);
        }

        public ICommand AddPasswordCommand { get; }
        public ICommand OpenGeneratorCommand { get; }
        public ICommand OpenImportCommand { get; }
        public ICommand OpenExportCommand { get; }
        public ICommand OpenPasswordHealthCommand { get; }
        public ICommand ViewAllActivityCommand { get; }
        public ICommand OpenCredentialCommand { get; }
        public ICommand OpenPasswordsCommand { get; }
        public ICommand OpenFavoritesCommand { get; }
        public ICommand OpenRecoveryCommand { get; }
        public ICommand OpenPasswordHealthDashboardCommand { get; }
        public ICommand OpenTotpVaultCommand { get; }
        public ICommand LaunchPhantomAttestorCommand { get; }
        public ICommand OpenSecurityOptionsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenBackupCommand { get; }
        public ICommand CopyQuickAccessPasswordCommand { get; }
        public ReactiveCommand<CategoryViewModel, Unit> NavigateToCategoryCommand { get; }
        public ReactiveCommand<CategoryViewModel, Unit> ToggleCategoryPinCommand { get; }

        /// <summary>
        /// Reference to the owner window for clipboard access.
        /// Set by VaultViewModel when creating this instance.
        /// </summary>
        public Window? OwnerWindow { get; set; }

        /// <summary>
        /// Reference to the clipboard guard for rate limiting.
        /// Set by VaultViewModel when creating this instance.
        /// </summary>
        public IClipboardGuard? ClipboardGuard { get; set; }

        public Action<string>? NavigateToFilter { get; set; }

        public SecurityDashboardViewModel? SecurityDashboardViewModel { get; private set; }

        public void SetRecoveryAvailability(bool isAvailable, string status)
        {
            IsRecoveryAvailable = isAvailable;
            RecoveryStatus = status;
        }

        public void SetAttestorAvailability(bool isAvailable, string status)
        {
            IsAttestorAvailable = isAvailable;
            AttestorStatus = status;
        }

        public async Task LoadDashboardDataAsync()
        {
            try
            {
                // Load welcome message
                var timeOfDay = DateTime.Now.Hour switch
                {
                    < 12 => "Good morning",
                    < 18 => "Good afternoon",
                    _ => "Good evening"
                };
                WelcomeMessage = $"{timeOfDay}! Your vault is secure.";

                // Statistics (TotalCredentials, TotalCategories, TwoFactorCoverage)
                // are computed in LoadQuickAccessCredentials when VaultViewModel
                // passes the real credential and category collections after unlock.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading dashboard data: {ex.Message}");
            }
        }

        private void LoadRecentActivityAsync(IEnumerable<Credential> credentials)
        {
            RecentActivities.Clear();

            var recent = credentials
                .OrderByDescending(c => c.LastUpdatedUtc)
                .Take(10)
                .ToList();

            foreach (var cred in recent)
            {
                var age = DateTimeOffset.UtcNow - cred.LastUpdatedUtc;
                var timeAgo = age.TotalMinutes < 1 ? "just now"
                    : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago"
                    : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago"
                    : age.TotalDays < 30 ? $"{(int)age.TotalDays}d ago"
                    : $"{cred.LastUpdatedUtc:MMM dd}";

                RecentActivities.Add(new ActivityItem
                {
                    IconPreset = cred.IsFavorite ? "Star" : "Edit",
                    Title = cred.Title,
                    Description = $"Updated — {cred.Category}",
                    TimeAgo = timeAgo
                });
            }

            HasRecentActivity = RecentActivities.Any();
        }

        private void LoadFavoritesAsync(IEnumerable<Credential> credentials)
        {
            FavoriteCredentials.Clear();

            var favorites = credentials
                .Where(c => c.IsFavorite)
                .OrderByDescending(c => c.LastUpdatedUtc)
                .ToList();

            foreach (var cred in favorites)
                FavoriteCredentials.Add(cred);

            HasFavorites = FavoriteCredentials.Any();
        }

        /// <summary>
        /// Loads the quick access password tiles from the vault's credential view models.
        /// Picks the most recently used credentials, falling back to most recently updated.
        /// </summary>
        public void LoadQuickAccessCredentials(
            IEnumerable<CredentialViewModel> credentials,
            IEnumerable<CategoryViewModel> categories)
        {
            try
            {
                QuickAccessCredentials.Clear();
                Categories.Clear();

                foreach (var cat in categories.Where(c =>
                    !c.Name.Equals("Secure Rubbish Bin", StringComparison.OrdinalIgnoreCase)))
                    Categories.Add(cat);

                var credList = credentials.ToList();

                // Pick up to 8 most-used credentials, prioritizing:
                // 1. Recently used (LastUsedUtc descending)
                // 2. Favorites first as tiebreaker
                // 3. Most recently updated as final fallback
                var topCredentials = credList
                    .Where(c => c.EntryType == EntryType.Password || c.EntryType == EntryType.WiFi || c.EntryType == EntryType.ApiKey)
                    .OrderByDescending(c => c.GetCredential().LastUsedUtc ?? DateTime.MinValue)
                    .ThenByDescending(c => c.IsFavorite)
                    .ThenByDescending(c => c.LastUpdatedUtc)
                    .Take(8)
                    .ToList();

                foreach (var cred in topCredentials)
                    QuickAccessCredentials.Add(cred);

                HasQuickAccessCredentials = QuickAccessCredentials.Count > 0 || Categories.Any(c => c.IsPinned);

                // Compute dashboard statistics from the real data
                TotalCredentials = credList.Count;
                TotalCategories = Categories.Count;
                var passwordCreds = credList.Where(c => c.EntryType == EntryType.Password).ToList();
                TwoFactorCoverage = passwordCreds.Count > 0
                    ? (int)Math.Round(100.0 * passwordCreds.Count(c => c.HasTotpSecret) / passwordCreds.Count)
                    : 0;

                // Load recent activity and favorites from underlying credential models
                var allCredentials = credList.Select(c => c.GetCredential()).ToList();
                LoadRecentActivityAsync(allCredentials);
                LoadFavoritesAsync(allCredentials);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load quick access credentials for dashboard");
                HasQuickAccessCredentials = false;
            }
        }

        private void NavigateToCategory(CategoryViewModel cat)
        {
            if (cat == null) return;
            RequestNavigation("Category:" + cat.Name);
        }

        private void ToggleCategoryPin(CategoryViewModel cat)
        {
            if (cat == null) return;
            cat.IsPinned = !cat.IsPinned;
        }

        private System.Threading.CancellationTokenSource? _feedbackCts;

        private async Task CopyQuickAccessPasswordAsync(CredentialViewModel credential)
        {
            try
            {
                if (credential == null) return;

                // Check clipboard guard
                if (ClipboardGuard != null && !ClipboardGuard.CanCopy())
                {
                    CopiedFeedback = "Too many clipboard ops - wait";
                    return;
                }

                IClipboard? clipboard = null;
                if (OwnerWindow != null)
                    clipboard = TopLevel.GetTopLevel(OwnerWindow)?.Clipboard;

                if (clipboard == null) return;

                // Get the secret based on entry type
                var secret = credential.EntryType switch
                {
                    EntryType.WiFi => credential.WiFiPassword,
                    EntryType.ApiKey => credential.ApiKeyValue,
                    _ => credential.Password
                };

                if (string.IsNullOrWhiteSpace(secret))
                {
                    CopiedFeedback = "Nothing to copy";
                    return;
                }

                await clipboard.SetTextAsync(secret);
                ClipboardGuard?.RegisterCopy(credential.Title);

                CopiedFeedback = $"Copied: {credential.Title}";

                // Clear feedback after 2 seconds
                _feedbackCts?.Cancel();
                _feedbackCts = new System.Threading.CancellationTokenSource();
                var token = _feedbackCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000, token);
                        if (!token.IsCancellationRequested)
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => CopiedFeedback = string.Empty);
                    }
                    catch (OperationCanceledException) { }
                });

                // Schedule clipboard clearing based on user settings
                var settings = SettingsService.Load();
                var clearDelay = settings.GetClipboardClearDelay();
                if (clearDelay.HasValue)
                {
                    var copiedSecret = secret;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(clearDelay.Value);
                            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                            {
                                try
                                {
                                    if (OwnerWindow != null)
                                    {
                                        var cb = TopLevel.GetTopLevel(OwnerWindow)?.Clipboard;
                                        if (cb != null)
                                        {
                                            var current = await cb.GetTextAsync();
                                            if (current == copiedSecret)
                                                await cb.SetTextAsync(string.Empty);
                                        }
                                    }
                                }
                                catch { }
                            });
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to copy quick access password");
            }
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} min ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hr ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays > 1 ? "s" : "")} ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) > 1 ? "s" : "")} ago";

            return dateTime.ToString("MMM d, yyyy");
        }

        private async Task AddPasswordAsync()
        {
            // Navigate to all passwords view
            RequestNavigation("Password");
            await Task.CompletedTask;
        }

        private async Task OpenGeneratorAsync()
        {
            // Navigate to password generator - for now go to all passwords
            RequestNavigation("All");
            await Task.CompletedTask;
        }

        private async Task OpenImportAsync()
        {
            // Navigate to all items view where import functionality is available
            RequestNavigation("All");
            await Task.CompletedTask;
        }

        private async Task OpenPasswordHealthAsync()
        {
            // Navigate to all passwords for health check
            RequestNavigation("Password");
            await Task.CompletedTask;
        }

        private void ViewAllActivity()
        {
            // Navigate to all items view
            RequestNavigation("All");
        }

        private void OpenPasswords()
        {
            RequestNavigation("Password");
        }

        private void OpenRecovery()
        {
            if (!IsRecoveryAvailable)
            {
                CopiedFeedback = RecoveryStatus;
                return;
            }

            RequestNavigation("Recovery");
        }

        private void OpenPasswordHealthDashboard()
        {
            RequestNavigation("PasswordHealth");
        }

        private void OpenTotpVault()
        {
            RequestNavigation("TOTP");
        }

        private async Task LaunchPhantomAttestorAsync()
        {
            SetAttestorAvailability(_integratedAttestorService.IsAvailable, _integratedAttestorService.AvailabilityMessage);

            if (_integratedAttestorService.TryLaunch(out var errorMessage))
            {
                Log.Information("PhantomAttestor launched successfully");
            }
            else
            {
                Log.Warning("PhantomAttestor launch unavailable: {Reason}", errorMessage);
                AttestorStatus = errorMessage ?? _integratedAttestorService.AvailabilityMessage;
            }

            await Task.CompletedTask;
        }

        private void OpenSecurityOptions()
        {
            RequestNavigation("SecurityOptions");
        }

        private void OpenExport()
        {
            RequestNavigation("Export");
        }

        private void OpenFavorites()
        {
            RequestNavigation("Favorites");
        }

        private void OpenSettings()
        {
            RequestNavigation("Settings");
        }

        private void OpenBackup()
        {
            RequestNavigation("Backup");
        }

        private async Task OpenCredentialAsync(Credential credential)
        {
            // Navigate to all items and select the credential
            RequestNavigation("All");
            await Task.CompletedTask;
        }

        private void RequestNavigation(string filterType)
        {
            MessageBus.Current.SendMessage(new NavigateToVaultWithFilterMessage(filterType));
            NavigateToFilter?.Invoke(filterType);
        }

    }

    public class ActivityItem
    {
        public string IconPreset { get; set; } = "Info";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TimeAgo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bindable scope for the dashboard's <c>controls:SecurityDashboard</c>
    /// surface. Live security metrics (score, breach counts, weak passwords,
    /// 2FA coverage, etc.) are exposed directly on <see cref="DashboardViewModel"/>
    /// and the parent <c>VaultViewModel</c>; this type exists so XAML can bind
    /// a dedicated DataContext without coupling to those VMs. It is intentionally
    /// empty — extend it only when the security panel needs state that is not
    /// already published by the parent view-models.
    /// </summary>
    public class SecurityDashboardViewModel
    {
    }
}
