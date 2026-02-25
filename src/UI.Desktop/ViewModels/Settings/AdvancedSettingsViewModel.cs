using System;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class AdvancedSettingsViewModel : ReactiveObject
    {
        private bool _enableDebugLogging = false;
        private bool _showDiagnosticInfo = false;
        private bool _enablePrivacyMode = false;
        private bool _redactLogs = true;
        private bool _blockRemoteDebugging = true;
        private int _sessionTimeout = 30;
        private bool _autoLockOnMinimize = false;
        private bool _autoLockOnScreenLock = true;
        private bool _clearClipboardOnLock = true;
        private bool _requireUnlockToShow = false;
        private int _selectedFailedAttempts = 2; // 10 attempts

        public bool EnableDebugLogging
        {
            get => _enableDebugLogging;
            set => this.RaiseAndSetIfChanged(ref _enableDebugLogging, value);
        }

        public bool ShowDiagnosticInfo
        {
            get => _showDiagnosticInfo;
            set => this.RaiseAndSetIfChanged(ref _showDiagnosticInfo, value);
        }

        public bool EnablePrivacyMode
        {
            get => _enablePrivacyMode;
            set => this.RaiseAndSetIfChanged(ref _enablePrivacyMode, value);
        }

        public bool RedactLogs
        {
            get => _redactLogs;
            set => this.RaiseAndSetIfChanged(ref _redactLogs, value);
        }

        public bool BlockRemoteDebugging
        {
            get => _blockRemoteDebugging;
            set => this.RaiseAndSetIfChanged(ref _blockRemoteDebugging, value);
        }

        public int SessionTimeout
        {
            get => _sessionTimeout;
            set => this.RaiseAndSetIfChanged(ref _sessionTimeout, value);
        }

        public bool AutoLockOnMinimize
        {
            get => _autoLockOnMinimize;
            set => this.RaiseAndSetIfChanged(ref _autoLockOnMinimize, value);
        }

        public bool AutoLockOnScreenLock
        {
            get => _autoLockOnScreenLock;
            set => this.RaiseAndSetIfChanged(ref _autoLockOnScreenLock, value);
        }

        public bool ClearClipboardOnLock
        {
            get => _clearClipboardOnLock;
            set => this.RaiseAndSetIfChanged(ref _clearClipboardOnLock, value);
        }

        public bool RequireUnlockToShow
        {
            get => _requireUnlockToShow;
            set => this.RaiseAndSetIfChanged(ref _requireUnlockToShow, value);
        }

        public int SelectedFailedAttempts
        {
            get => _selectedFailedAttempts;
            set => this.RaiseAndSetIfChanged(ref _selectedFailedAttempts, value);
        }

        public ICommand ViewLogsCommand { get; }
        public ICommand ExportDiagnosticReportCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand RebuildSearchIndexCommand { get; }
        public ICommand ClearAllVaultDataCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }

        // PhantomRecovery Developer Mode
        public bool IsRecoveryDeveloperModeEnabled
        {
            get => RecoveryDeveloperMode.IsEnabled;
            set
            {
                RecoveryDeveloperMode.IsEnabled = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(RecoveryDeveloperVaultPath));
                this.RaisePropertyChanged(nameof(RecoveryDeveloperUsbPath));
            }
        }

        public string RecoveryDeveloperVaultPath => RecoveryDeveloperMode.DeveloperVaultPath;
        public string RecoveryDeveloperUsbPath => RecoveryDeveloperMode.DeveloperUsbPath;

        public bool SkipUsbBindingCheck
        {
            get => RecoveryDeveloperMode.SkipUsbBindingCheck;
            set
            {
                RecoveryDeveloperMode.SkipUsbBindingCheck = value;
                this.RaisePropertyChanged();
            }
        }

        public bool RecoveryVerboseLogging
        {
            get => RecoveryDeveloperMode.VerboseLogging;
            set
            {
                RecoveryDeveloperMode.VerboseLogging = value;
                this.RaisePropertyChanged();
            }
        }

        public AdvancedSettingsViewModel()
        {
            ViewLogsCommand = ReactiveCommand.Create(ViewLogs);
            ExportDiagnosticReportCommand = ReactiveCommand.CreateFromTask(ExportDiagnosticReport);
            OpenLogFolderCommand = ReactiveCommand.Create(OpenLogFolder);
            ClearCacheCommand = ReactiveCommand.CreateFromTask(ClearCache);
            RebuildSearchIndexCommand = ReactiveCommand.CreateFromTask(RebuildSearchIndex);
            ClearAllVaultDataCommand = ReactiveCommand.CreateFromTask(ClearAllVaultData);
            ResetToDefaultsCommand = ReactiveCommand.CreateFromTask(ResetToDefaults);
        }

        private void ViewLogs()
        {
            var logViewer = new Views.LogViewerWindow();
            logViewer.Show();
        }

        private async Task ExportDiagnosticReport()
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Diagnostic Report",
                    DefaultExtension = "txt",
                    SuggestedFileName = $"PhantomVault_Diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } }
                    }
                });

                if (file != null)
                {
                    var report = GenerateDiagnosticReport();
                    await System.IO.File.WriteAllTextAsync(file.Path.LocalPath, report);
                    System.Diagnostics.Debug.WriteLine($"Diagnostic report exported to: {file.Path.LocalPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomVault", "Logs");

                if (System.IO.Directory.Exists(logPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening log folder: {ex.Message}");
            }
        }

        private async Task ClearCache()
        {
            try
            {
                await Task.Delay(500); // Simulate operation
                System.Diagnostics.Debug.WriteLine("Cache cleared successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clear cache failed: {ex.Message}");
            }
        }

        private async Task RebuildSearchIndex()
        {
            try
            {
                await Task.Delay(1000); // Simulate operation
                System.Diagnostics.Debug.WriteLine("Search index rebuilt successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Rebuild index failed: {ex.Message}");
            }
        }

        private async Task ClearAllVaultData()
        {
            // TODO: Show confirmation dialog before clearing
            try
            {
                await Task.Delay(100);
                System.Diagnostics.Debug.WriteLine("⚠ WARNING: Clear all vault data requested");
                // This should show a confirmation dialog
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clear vault data failed: {ex.Message}");
            }
        }

        private async Task ResetToDefaults()
        {
            // TODO: Show confirmation dialog before reset
            try
            {
                await Task.Delay(100);
                System.Diagnostics.Debug.WriteLine("⚠ WARNING: Reset to defaults requested");
                // Reset all settings to default values
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reset failed: {ex.Message}");
            }
        }

        private string GenerateDiagnosticReport()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("PHANTOMVAULT DIAGNOSTIC REPORT");
            sb.AppendLine("================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // System Information
            sb.AppendLine("SYSTEM INFORMATION");
            sb.AppendLine("------------------");
            sb.AppendLine($"Operating System: {Environment.OSVersion}");
            sb.AppendLine($"OS Version: {Environment.OSVersion.Version}");
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"System Directory: {Environment.SystemDirectory}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"User Name: {Environment.UserName}");
            sb.AppendLine($"CLR Version: {Environment.Version}");
            sb.AppendLine();

            // Application Information
            sb.AppendLine("APPLICATION INFORMATION");
            sb.AppendLine("-----------------------");
            sb.AppendLine($"Application: PhantomVault");
            sb.AppendLine($"Version: 6.0.0"); // TODO: Get from assembly
            sb.AppendLine($"Working Directory: {Environment.CurrentDirectory}");
            sb.AppendLine($"Base Directory: {AppContext.BaseDirectory}");
            sb.AppendLine();

            // Memory Information
            sb.AppendLine("MEMORY INFORMATION");
            sb.AppendLine("------------------");
            var process = System.Diagnostics.Process.GetCurrentProcess();
            sb.AppendLine($"Working Set: {process.WorkingSet64 / 1024 / 1024} MB");
            sb.AppendLine($"Private Memory: {process.PrivateMemorySize64 / 1024 / 1024} MB");
            sb.AppendLine($"Virtual Memory: {process.VirtualMemorySize64 / 1024 / 1024} MB");
            sb.AppendLine();

            // Settings
            sb.AppendLine("CURRENT SETTINGS");
            sb.AppendLine("----------------");
            sb.AppendLine($"Debug Logging: {EnableDebugLogging}");
            sb.AppendLine($"Diagnostic Info: {ShowDiagnosticInfo}");
            sb.AppendLine($"Privacy Mode: {EnablePrivacyMode}");
            sb.AppendLine($"Redact Logs: {RedactLogs}");
            sb.AppendLine($"Block Remote Debugging: {BlockRemoteDebugging}");
            sb.AppendLine($"Session Timeout: {SessionTimeout} minutes");
            sb.AppendLine($"Auto-lock on Minimize: {AutoLockOnMinimize}");
            sb.AppendLine($"Auto-lock on Screen Lock: {AutoLockOnScreenLock}");
            sb.AppendLine($"Clear Clipboard on Lock: {ClearClipboardOnLock}");
            sb.AppendLine($"Require Unlock to Show: {RequireUnlockToShow}");
            sb.AppendLine($"Failed Attempts Threshold: {GetFailedAttemptsValue()}");
            sb.AppendLine();

            // Paths
            sb.AppendLine("APPLICATION PATHS");
            sb.AppendLine("-----------------");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            sb.AppendLine($"App Data: {Path.Combine(appData, "PhantomVault")}");
            sb.AppendLine($"Logs: {Path.Combine(appData, "PhantomVault", "Logs")}");
            sb.AppendLine();

            // Disk Space
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                sb.AppendLine("DISK SPACE");
                sb.AppendLine("----------");
                sb.AppendLine($"Drive: {drive.Name}");
                sb.AppendLine($"Total Size: {drive.TotalSize / 1024 / 1024 / 1024} GB");
                sb.AppendLine($"Free Space: {drive.AvailableFreeSpace / 1024 / 1024 / 1024} GB");
                sb.AppendLine();
            }
            catch
            {
                // Ignore disk space errors
            }

            sb.AppendLine("END OF REPORT");
            sb.AppendLine("================================");

            return sb.ToString();
        }

        private string GetFailedAttemptsValue()
        {
            return SelectedFailedAttempts switch
            {
                0 => "3",
                1 => "5",
                2 => "10",
                3 => "Unlimited",
                _ => "Unknown"
            };
        }
    }
}
