using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
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

        public DashboardViewModel()
        {
            RecentActivities = new ObservableCollection<ActivityItem>();
            FavoriteCredentials = new ObservableCollection<Credential>();

            AddPasswordCommand = ReactiveCommand.CreateFromTask(AddPasswordAsync);
            OpenGeneratorCommand = ReactiveCommand.CreateFromTask(OpenGeneratorAsync);
            OpenImportCommand = ReactiveCommand.CreateFromTask(OpenImportAsync);
            OpenPasswordHealthCommand = ReactiveCommand.CreateFromTask(OpenPasswordHealthAsync);
            ViewAllActivityCommand = ReactiveCommand.Create(ViewAllActivity);
            OpenCredentialCommand = ReactiveCommand.CreateFromTask<Credential>(OpenCredentialAsync);
            OpenPasswordsCommand = ReactiveCommand.Create(OpenPasswords);
            OpenRecoveryCommand = ReactiveCommand.Create(OpenRecovery);
            OpenPasswordHealthDashboardCommand = ReactiveCommand.Create(OpenPasswordHealthDashboard);
            OpenTotpVaultCommand = ReactiveCommand.Create(OpenTotpVault);
            LaunchPhantomAttestorCommand = ReactiveCommand.CreateFromTask(LaunchPhantomAttestorAsync);
            OpenSecurityOptionsCommand = ReactiveCommand.Create(OpenSecurityOptions);

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

        public void ClearDashboard()
        {
            WelcomeMessage = string.Empty;
            TotalCredentials = 0;
            TotalCategories = 0;
            TwoFactorCoverage = 0;
            HasRecentActivity = false;
            HasFavorites = false;
            RecentActivities.Clear();
            FavoriteCredentials.Clear();
        }

        public ObservableCollection<ActivityItem> RecentActivities { get; }
        public ObservableCollection<Credential> FavoriteCredentials { get; }

        public ICommand AddPasswordCommand { get; }
        public ICommand OpenGeneratorCommand { get; }
        public ICommand OpenImportCommand { get; }
        public ICommand OpenPasswordHealthCommand { get; }
        public ICommand ViewAllActivityCommand { get; }
        public ICommand OpenCredentialCommand { get; }
        public ICommand OpenPasswordsCommand { get; }
        public ICommand OpenRecoveryCommand { get; }
        public ICommand OpenPasswordHealthDashboardCommand { get; }
        public ICommand OpenTotpVaultCommand { get; }
        public ICommand LaunchPhantomAttestorCommand { get; }
        public ICommand OpenSecurityOptionsCommand { get; }

        public Action<string>? NavigateToFilter { get; set; }

        public SecurityDashboardViewModel? SecurityDashboardViewModel { get; private set; }

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

                // TODO: Load actual statistics from vault service
                // For now, using placeholder data
                TotalCredentials = 0;
                TotalCategories = 0;
                TwoFactorCoverage = 0;

                HasRecentActivity = false;
                HasFavorites = false;

                // Initialize security dashboard (placeholder - integrate with actual security service)
                // SecurityDashboardViewModel = new SecurityDashboardViewModel(...);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading dashboard data: {ex.Message}");
            }
        }

        private async Task LoadRecentActivityAsync(IEnumerable<Credential> credentials)
        {
            await Task.Run(() =>
            {
                RecentActivities.Clear();

                // TODO: Load actual recent activity from credentials
                // Placeholder implementation

                HasRecentActivity = RecentActivities.Any();
            });
        }

        private async Task LoadFavoritesAsync(IEnumerable<Credential> credentials)
        {
            await Task.Run(() =>
            {
                FavoriteCredentials.Clear();

                // TODO: Load actual favorites from credentials
                // Placeholder implementation

                HasFavorites = FavoriteCredentials.Any();
            });
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
            try
            {
                // Try to find PhantomAttestor executable
                var possiblePaths = new[]
                {
                    // Development path (relative to PhantomObscuraV6)
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PhantomAttestor", "App", "bin", "Debug", "net8.0", "PhantomAttestor.App.exe"),
                    // Installed path (same directory)
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PhantomAttestor.App.exe"),
                    // Sibling build path
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "PhantomAttestor", "PhantomAttestor.App.exe")
                };

                string? attestorPath = null;
                foreach (var path in possiblePaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        attestorPath = fullPath;
                        break;
                    }
                }

                if (attestorPath == null)
                {
                    // Try using dotnet run for development
                    var projectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PhantomAttestor", "App", "PhantomAttestor.App.csproj");
                    var fullProjectPath = Path.GetFullPath(projectPath);
                    
                    if (File.Exists(fullProjectPath))
                    {
                        Log.Information("Launching PhantomAttestor via dotnet run: {ProjectPath}", fullProjectPath);
                        var psi = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"run --project \"{fullProjectPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = false,
                            WorkingDirectory = Path.GetDirectoryName(fullProjectPath)
                        };
                        psi.EnvironmentVariables["PHANTOMATTESTOR_DEV_BYPASS"] = "1";
                        Process.Start(psi);
                        Log.Information("PhantomAttestor launched via dotnet run");
                        return;
                    }
                    
                    Log.Warning("PhantomAttestor not found. Please build or install PhantomAttestor first.");
                    return;
                }

                Log.Information("Launching PhantomAttestor: {Path}", attestorPath);
                var process = new ProcessStartInfo
                {
                    FileName = attestorPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(attestorPath)
                };
                Process.Start(process);
                Log.Information("PhantomAttestor launched successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch PhantomAttestor");
            }
            
            await Task.CompletedTask;
        }

        private void OpenSecurityOptions()
        {
            RequestNavigation("SecurityOptions");
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

    public class SecurityDashboardViewModel
    {
        // Placeholder for security dashboard data
        // This should be populated from actual security analysis services
    }
}
