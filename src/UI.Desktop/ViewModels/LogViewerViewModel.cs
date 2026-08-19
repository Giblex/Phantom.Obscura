using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    public class LogViewerViewModel : ReactiveObject
    {
        private string _logContent = string.Empty;
        private string _statusMessage = "Ready";
        private string _searchText = string.Empty;
        private int _selectedLogLevel = 0;
        private bool _autoScroll = true;
        private readonly string _logPath;

        public LogViewerViewModel()
        {
            _logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomVault", "Logs");

            RefreshCommand = ReactiveCommand.CreateFromTask(LoadLogs);
            ClearLogsCommand = ReactiveCommand.CreateFromTask(ClearLogs);
            ExportLogsCommand = ReactiveCommand.CreateFromTask(ExportLogs);
            OpenLogFolderCommand = ReactiveCommand.Create(OpenLogFolder);

            this.WhenAnyValue(x => x.SearchText, x => x.SelectedLogLevel)
                .Subscribe(async _ => await FilterLogs());

            _ = LoadLogs();
        }

        public string LogContent
        {
            get => _logContent;
            set => this.RaiseAndSetIfChanged(ref _logContent, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => this.RaiseAndSetIfChanged(ref _searchText, value);
        }

        public int SelectedLogLevel
        {
            get => _selectedLogLevel;
            set => this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => this.RaiseAndSetIfChanged(ref _autoScroll, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ExportLogsCommand { get; }
        public ICommand OpenLogFolderCommand { get; }

        private string? _allLogsCache;

        private async Task LoadLogs()
        {
            try
            {
                StatusMessage = "Loading logs...";

                await Task.Run(() =>
                {
                    if (!Directory.Exists(_logPath))
                    {
                        LogContent = "No log files found. Log directory does not exist.";
                        _allLogsCache = LogContent;
                        StatusMessage = "No logs found";
                        return;
                    }

                    var logFiles = Directory.GetFiles(_logPath, "*.log")
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .Take(5);

                    if (!logFiles.Any())
                    {
                        LogContent = "No log files found in the log directory.";
                        _allLogsCache = LogContent;
                        StatusMessage = "No logs found";
                        return;
                    }

                    var sb = new StringBuilder();
                    int totalLines = 0;

                    foreach (var logFile in logFiles)
                    {
                        sb.AppendLine($"=== {Path.GetFileName(logFile)} ===");
                        sb.AppendLine($"=== Last Modified: {File.GetLastWriteTime(logFile):yyyy-MM-dd HH:mm:ss} ===");
                        sb.AppendLine();

                        try
                        {
                            var lines = File.ReadAllLines(logFile);
                            totalLines += lines.Length;

                            var recentLines = lines.TakeLast(500);
                            foreach (var line in recentLines)
                            {
                                sb.AppendLine(line);
                            }

                            if (lines.Length > 500)
                            {
                                sb.AppendLine($"... ({lines.Length - 500} older lines not shown)");
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"Error reading file: {ex.Message}");
                        }

                        sb.AppendLine();
                        sb.AppendLine();
                    }

                    _allLogsCache = sb.ToString();
                    LogContent = _allLogsCache;
                    StatusMessage = $"Loaded {totalLines} log entries from {logFiles.Count()} file(s)";
                });
            }
            catch (Exception ex)
            {
                LogContent = $"Error loading logs: {ex.Message}";
                StatusMessage = "Error loading logs";
            }
        }

        private async Task FilterLogs()
        {
            if (string.IsNullOrWhiteSpace(_allLogsCache))
            {
                return;
            }

            await Task.Run(() =>
            {
                var lines = _allLogsCache.Split('\n');
                var filtered = new List<string>();

                string? levelFilter = SelectedLogLevel switch
                {
                    1 => "[ERR]",
                    2 => "[WRN]",
                    3 => "[INF]",
                    4 => "[DBG]",
                    _ => null
                };

                foreach (var line in lines)
                {

                    if (levelFilter != null && !line.Contains(levelFilter))
                    {

                        if (!line.StartsWith("==="))
                            continue;
                    }

                    if (!string.IsNullOrWhiteSpace(SearchText))
                    {
                        if (!line.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                        {

                            if (!line.StartsWith("==="))
                                continue;
                        }
                    }

                    filtered.Add(line);
                }

                LogContent = string.Join('\n', filtered);
                StatusMessage = $"Showing {filtered.Count} filtered entries";
            });
        }

        private async Task ClearLogs()
        {
            try
            {
                if (!Directory.Exists(_logPath))
                {
                    StatusMessage = "No logs to clear";
                    return;
                }

                var logFiles = Directory.GetFiles(_logPath, "*.log");

                foreach (var file in logFiles)
                {
                    File.Delete(file);
                }

                LogContent = "All log files have been cleared.";
                _allLogsCache = LogContent;
                StatusMessage = $"Cleared {logFiles.Length} log file(s)";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[LogViewer] Failed to clear logs.");
                StatusMessage = "Logs could not be cleared. Close other log viewers and try again.";
            }

            await Task.CompletedTask;
        }

        private async Task ExportLogs()
        {
            try
            {
                var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Logs",
                    DefaultExtension = "txt",
                    SuggestedFileName = $"PhantomVault_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("Log File") { Patterns = new[] { "*.log" } }
                    }
                });

                if (file != null)
                {
                    await File.WriteAllTextAsync(file.Path.LocalPath, LogContent);
                    StatusMessage = $"Logs exported to {Path.GetFileName(file.Path.LocalPath)}";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[LogViewer] Log export failed.");
                StatusMessage = "Logs could not be exported. Verify the destination is writable and try again.";
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                if (Directory.Exists(_logPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _logPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                else
                {
                    StatusMessage = "Log directory does not exist";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[LogViewer] Failed to open the log folder.");
                StatusMessage = "The log folder could not be opened. Verify it is accessible and try again.";
            }
        }
    }
}

