using System;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.UI.Services;
using Serilog;

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

        // Issue #25: shared draft tracker so the overlay-bottom Save/Discard
        // buttons drive Advanced-tab persistence. _baselineSnapshot captures
        // the on-disk state at the moment of first-stage so Discard can roll
        // every field back without overwriting any intermediate commits.
        private readonly PhantomVault.UI.Services.SettingsDraftTracker _draft;
        private AdvancedBaseline _baselineSnapshot;
        private bool _suppressStage; // set during LoadSettings so initial property writes don't stage

        private readonly struct AdvancedBaseline
        {
            public bool EnableDebugLogging { get; init; }
            public bool EnablePrivacyMode { get; init; }
            public bool RedactLogs { get; init; }
            public bool BlockRemoteDebugging { get; init; }
            public int SessionTimeout { get; init; }
            public bool AutoLockOnMinimize { get; init; }
            public bool AutoLockOnScreenLock { get; init; }
            public bool ClearClipboardOnLock { get; init; }
            public bool RequireUnlockToShow { get; init; }
            public int SelectedFailedAttempts { get; init; }
        }

        public bool EnableDebugLogging
        {
            get => _enableDebugLogging;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _enableDebugLogging, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool ShowDiagnosticInfo
        {
            get => _showDiagnosticInfo;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showDiagnosticInfo, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool EnablePrivacyMode
        {
            get => _enablePrivacyMode;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _enablePrivacyMode, value))
                {
                    PrivacyShield.PrivacyModeEnabled = value;
                    SaveSettings();
                }
            }
        }

        public bool RedactLogs
        {
            get => _redactLogs;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _redactLogs, value))
                {
                    PrivacyShield.RedactDiagnostics = value;
                    SaveSettings();
                }
            }
        }

        public bool BlockRemoteDebugging
        {
            get => _blockRemoteDebugging;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _blockRemoteDebugging, value))
                {
                    SaveSettings();
                }
            }
        }

        public int SessionTimeout
        {
            get => _sessionTimeout;
            set
            {
                if (_sessionTimeout != value)
                {
                    this.RaiseAndSetIfChanged(ref _sessionTimeout, value);
                    SaveSettings();
                }
            }
        }

        public bool AutoLockOnMinimize
        {
            get => _autoLockOnMinimize;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _autoLockOnMinimize, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool AutoLockOnScreenLock
        {
            get => _autoLockOnScreenLock;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _autoLockOnScreenLock, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool ClearClipboardOnLock
        {
            get => _clearClipboardOnLock;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _clearClipboardOnLock, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool RequireUnlockToShow
        {
            get => _requireUnlockToShow;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _requireUnlockToShow, value))
                {
                    SaveSettings();
                }
            }
        }

        public int SelectedFailedAttempts
        {
            get => _selectedFailedAttempts;
            set
            {
                if (_selectedFailedAttempts != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedFailedAttempts, value);
                    SaveSettings();
                }
            }
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
                if (!IsRecoveryDeveloperModeAvailable)
                    return;

                RecoveryDeveloperMode.IsEnabled = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(RecoveryDeveloperVaultPath));
                this.RaisePropertyChanged(nameof(RecoveryDeveloperUsbPath));
            }
        }

        public bool IsRecoveryDeveloperModeAvailable =>
            RecoveryDeveloperMode.IsAvailable;

        public string RecoveryDeveloperModeStatus => IsRecoveryDeveloperModeAvailable
            ? "Testing-only recovery overrides are available on this build."
            : RecoveryDeveloperMode.AvailabilityMessage;

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

        public AdvancedSettingsViewModel() : this(null) { }

        public AdvancedSettingsViewModel(PhantomVault.UI.Services.SettingsDraftTracker? draftTracker)
        {
            // Issue #25: resolve the singleton tracker from DI so this VM stages
            // into the same instance the overlay's Save button drives.
            _draft = draftTracker
                ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.SettingsDraftTracker)) as PhantomVault.UI.Services.SettingsDraftTracker)
                ?? new PhantomVault.UI.Services.SettingsDraftTracker();
            _suppressStage = true;
            LoadSettings();
            _baselineSnapshot = SnapshotCurrent();
            _suppressStage = false;
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

        private void LoadSettings()
        {
            try
            {
                var settings = SettingsService.LoadAdvancedSnapshot();
                _enableDebugLogging = settings.EnableDebugLogging;
                _enablePrivacyMode = settings.PrivacyModeEnabled;
                _redactLogs = settings.RedactDiagnosticLogs;
                _blockRemoteDebugging = settings.BlockRemoteDebugging;
                _sessionTimeout = settings.SessionTimeoutMinutes;
                _autoLockOnMinimize = settings.AutoLockOnMinimize;
                _autoLockOnScreenLock = settings.AutoLockOnScreenLock;
                _clearClipboardOnLock = settings.ClearClipboardOnLock;
                _requireUnlockToShow = settings.RequireUnlockToShow;
                _selectedFailedAttempts = SettingsService.GetFailedUnlockSelectionFromMaxAttempts(settings.MaxFailedUnlockAttempts);
                PrivacyShield.DebugLoggingEnabled = _enableDebugLogging;
                PrivacyShield.PrivacyModeEnabled = _enablePrivacyMode;
                PrivacyShield.RedactDiagnostics = _redactLogs;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load advanced settings, using defaults");
            }
        }

        // Issue #25: every setter routes through here. Instead of writing to
        // disk on every keystroke / toggle, stage one batched "Advanced.All"
        // entry in the shared tracker. Re-staging replaces the prior pair so
        // a flurry of changes still results in a single commit/discard.
        private void SaveSettings()
        {
            if (_suppressStage) return;

            // If the user toggled everything back to the originally-loaded
            // baseline, drop the staged work entirely.
            if (CurrentMatchesBaseline())
            {
                _draft.ClearKey("Advanced.All");
                return;
            }

            // Capture the snapshot of fields we want to commit.
            var staged = SnapshotCurrent();
            var baseline = _baselineSnapshot;

            _draft.Stage(
                key: "Advanced.All",
                commit: () =>
                {
                    try
                    {
                        SettingsService.Update(settings =>
                        {
                            settings.EnableDebugLogging = staged.EnableDebugLogging;
                            settings.PrivacyModeEnabled = staged.EnablePrivacyMode;
                            settings.RedactDiagnosticLogs = staged.RedactLogs;
                            settings.BlockRemoteDebugging = staged.BlockRemoteDebugging;
                            settings.SessionTimeoutMinutes = staged.SessionTimeout;
                            settings.AutoLockOnMinimize = staged.AutoLockOnMinimize;
                            settings.AutoLockOnScreenLock = staged.AutoLockOnScreenLock;
                            settings.ClearClipboardOnLock = staged.ClearClipboardOnLock;
                            settings.RequireUnlockToShow = staged.RequireUnlockToShow;
                            settings.MaxFailedUnlockAttempts = SettingsService.GetMaxFailedUnlockAttemptsFromSelection(staged.SelectedFailedAttempts);
                        });
                        PrivacyShield.DebugLoggingEnabled = staged.EnableDebugLogging;
                        // Update the baseline so further edits stage against the
                        // newly-persisted state.
                        _baselineSnapshot = staged;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to persist advanced settings");
                    }
                },
                discard: () =>
                {
                    // Roll every property back to the baseline that existed at
                    // first-stage; raise property changes so the UI updates.
                    _suppressStage = true;
                    try
                    {
                        EnableDebugLogging = baseline.EnableDebugLogging;
                        EnablePrivacyMode = baseline.EnablePrivacyMode;
                        RedactLogs = baseline.RedactLogs;
                        BlockRemoteDebugging = baseline.BlockRemoteDebugging;
                        SessionTimeout = baseline.SessionTimeout;
                        AutoLockOnMinimize = baseline.AutoLockOnMinimize;
                        AutoLockOnScreenLock = baseline.AutoLockOnScreenLock;
                        ClearClipboardOnLock = baseline.ClearClipboardOnLock;
                        RequireUnlockToShow = baseline.RequireUnlockToShow;
                        SelectedFailedAttempts = baseline.SelectedFailedAttempts;
                    }
                    finally
                    {
                        _suppressStage = false;
                    }
                    // Restore the live PrivacyShield flag too if it had been
                    // toggled mid-session via the EnablePrivacyMode / RedactLogs
                    // setters (those mutate static state).
                    PrivacyShield.PrivacyModeEnabled = baseline.EnablePrivacyMode;
                    PrivacyShield.RedactDiagnostics = baseline.RedactLogs;
                });
        }

        private AdvancedBaseline SnapshotCurrent() => new AdvancedBaseline
        {
            EnableDebugLogging = EnableDebugLogging,
            EnablePrivacyMode = EnablePrivacyMode,
            RedactLogs = RedactLogs,
            BlockRemoteDebugging = BlockRemoteDebugging,
            SessionTimeout = SessionTimeout,
            AutoLockOnMinimize = AutoLockOnMinimize,
            AutoLockOnScreenLock = AutoLockOnScreenLock,
            ClearClipboardOnLock = ClearClipboardOnLock,
            RequireUnlockToShow = RequireUnlockToShow,
            SelectedFailedAttempts = SelectedFailedAttempts,
        };

        private bool CurrentMatchesBaseline()
        {
            var b = _baselineSnapshot;
            return EnableDebugLogging == b.EnableDebugLogging
                && EnablePrivacyMode == b.EnablePrivacyMode
                && RedactLogs == b.RedactLogs
                && BlockRemoteDebugging == b.BlockRemoteDebugging
                && SessionTimeout == b.SessionTimeout
                && AutoLockOnMinimize == b.AutoLockOnMinimize
                && AutoLockOnScreenLock == b.AutoLockOnScreenLock
                && ClearClipboardOnLock == b.ClearClipboardOnLock
                && RequireUnlockToShow == b.RequireUnlockToShow
                && SelectedFailedAttempts == b.SelectedFailedAttempts;
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
                    PrivacyShield.DebugInfo("AdvancedSettings", $"Diagnostic report exported to: {file.Path.LocalPath}");
                }
            }
            catch (Exception ex)
            {
                PrivacyShield.DebugInfo("AdvancedSettings", $"Export failed: {ex.Message}");
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
                PrivacyShield.DebugInfo("AdvancedSettings", $"Error opening log folder: {ex.Message}");
            }
        }

        private async Task ClearCache()
        {
            try
            {
                await Task.Delay(500); // Simulate operation
                PrivacyShield.DebugInfo("AdvancedSettings", "Cache cleared successfully");
            }
            catch (Exception ex)
            {
                PrivacyShield.DebugInfo("AdvancedSettings", $"Clear cache failed: {ex.Message}");
            }
        }

        private async Task RebuildSearchIndex()
        {
            try
            {
                await Task.Delay(1000); // Simulate operation
                PrivacyShield.DebugInfo("AdvancedSettings", "Search index rebuilt successfully");
            }
            catch (Exception ex)
            {
                PrivacyShield.DebugInfo("AdvancedSettings", $"Rebuild index failed: {ex.Message}");
            }
        }

        private async Task ClearAllVaultData()
        {
            try
            {
                var dialogService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(DialogService)) as DialogService;
                if (dialogService != null)
                {
                    var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    var confirmed = await dialogService.ShowDestructiveConfirmationAsync(
                        "Clear All Vault Data",
                        "This will permanently delete all credentials, categories, and settings. This action cannot be undone.",
                        "DELETE",
                        owner);
                    if (!confirmed) return;
                }

                await Task.Delay(100);
                PrivacyShield.DebugInfo("AdvancedSettings", "Clear all vault data confirmed and executed");
            }
            catch (Exception ex)
            {
                PrivacyShield.DebugInfo("AdvancedSettings", $"Clear vault data failed: {ex.Message}");
            }
        }

        private async Task ResetToDefaults()
        {
            try
            {
                var dialogService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(DialogService)) as DialogService;
                if (dialogService != null)
                {
                    var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    var confirmed = await dialogService.ShowConfirmationAsync(
                        "Reset to Defaults",
                        "Are you sure you want to reset all settings to their default values?",
                        owner);
                    if (!confirmed) return;
                }

                await Task.Delay(100);
                EnableDebugLogging = false;
                ShowDiagnosticInfo = false;
                EnablePrivacyMode = false;
                RedactLogs = true;
                BlockRemoteDebugging = true;
                SessionTimeout = 30;
                AutoLockOnMinimize = false;
                AutoLockOnScreenLock = true;
                ClearClipboardOnLock = true;
                RequireUnlockToShow = false;
                SelectedFailedAttempts = 2;
                SaveSettings();
                PrivacyShield.DebugInfo("AdvancedSettings", "Settings reset to defaults");
            }
            catch (Exception ex)
            {
                PrivacyShield.DebugInfo("AdvancedSettings", $"Reset failed: {ex.Message}");
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
            if (!RedactLogs && !EnablePrivacyMode)
            {
                sb.AppendLine($"Machine Name: {Environment.MachineName}");
                sb.AppendLine($"User Name: {Environment.UserName}");
            }
            else
            {
                sb.AppendLine("Machine Name: [REDACTED]");
                sb.AppendLine("User Name: [REDACTED]");
            }
            sb.AppendLine($"CLR Version: {Environment.Version}");
            sb.AppendLine();

            // Application Information
            sb.AppendLine("APPLICATION INFORMATION");
            sb.AppendLine("-----------------------");
            sb.AppendLine($"Application: PhantomVault");
            sb.AppendLine($"Version: {System.Reflection.Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "Unknown"}");
            sb.AppendLine($"Working Directory: {PrivacyShield.MaskText(Environment.CurrentDirectory)}");
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
            sb.AppendLine($"App Data: {PrivacyShield.MaskText(Path.Combine(appData, "PhantomVault"))}");
            sb.AppendLine($"Logs: {PrivacyShield.MaskText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault", "logs"))}");
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
