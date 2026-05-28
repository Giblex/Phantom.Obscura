using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views.Dialogs;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for importing credentials from external formats (CSV, KeePass XML, JSON).
    /// </summary>
    public sealed class ImportViewModel : ReactiveObject
    {
        private readonly ImportExportService _importExportService;
        private readonly DialogService _dialogService;
        private readonly List<Credential> _existingCredentials;
        private Window? _ownerWindow;

        private string _selectedFormat = "CSV";
        private string _selectedFile = string.Empty;
        private string _statusMessage = "Select a file to import credentials.";
        private bool _isImporting = false;
        private int _previewCount = 0;
        private ObservableCollection<Credential> _previewCredentials = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap?> _iconCache = new();

        public event EventHandler<List<Credential>>? ImportCompleted;
        public event EventHandler? CloseRequested;

        public ImportViewModel(List<Credential>? existingCredentials = null)
        {
            _importExportService = new ImportExportService();
            _dialogService = new DialogService();
            _existingCredentials = existingCredentials ?? new List<Credential>();

            // Place the most common import formats first: JSON and CSV
            Formats = new ObservableCollection<string>
            {
                "JSON",
                "CSV",
                "KeePass XML",
                "KeePass KDBX",
                "1Password CSV",
                "Bitwarden JSON",
                "Bitwarden CSV",
                "Proton Pass JSON",
                "LastPass CSV",
                "Chrome CSV",
                "Edge CSV",
                "Firefox CSV"
            };

            // Build a lightweight representation for tile UI (name + icon)
            ImportMethods = new ObservableCollection<ImportMethod>(
                Formats.Select(f => new ImportMethod { Name = f, IconSource = GetIconForFormat(f) })
            );

            BrowseCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await BrowseForFileAsync();
            });
            ImportCommand = ReactiveCommand.CreateFromTask(ExecuteImportAsync,
                this.WhenAnyValue(x => x.SelectedFile, x => x.IsImporting,
                    (file, importing) => !string.IsNullOrEmpty(file) && !importing));
            StartImportCommand = ReactiveCommand.CreateFromTask<string>(async format =>
            {
                // Set selected format and immediately open file picker; start import automatically after file selection
                SelectedFormat = format;
                var fileSelected = await BrowseForFileAsync();

                // If a file was selected, start the import immediately
                if (fileSelected && !string.IsNullOrEmpty(SelectedFile))
                {
                    // Small delay to allow UI to update status/preview
                    await Task.Delay(120);
                    await ExecuteImportAsync();
                }
            });
            CloseCommand = ReactiveCommand.Create(Close);
        }

        public void SetOwner(Window owner)
        {
            _ownerWindow = owner;
        }

        public ObservableCollection<string> Formats { get; }

        public ObservableCollection<ImportMethod> ImportMethods { get; }

        public string SelectedFormat
        {
            get => _selectedFormat;
            set => this.RaiseAndSetIfChanged(ref _selectedFormat, value);
        }

        public string SelectedFile
        {
            get => _selectedFile;
            set => this.RaiseAndSetIfChanged(ref _selectedFile, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsImporting
        {
            get => _isImporting;
            set => this.RaiseAndSetIfChanged(ref _isImporting, value);
        }

        public int PreviewCount
        {
            get => _previewCount;
            set => this.RaiseAndSetIfChanged(ref _previewCount, value);
        }

        public ObservableCollection<Credential> PreviewCredentials
        {
            get => _previewCredentials;
            set => this.RaiseAndSetIfChanged(ref _previewCredentials, value);
        }

        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportCommand { get; }
        public ReactiveCommand<string, Unit> StartImportCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        public sealed class ImportMethod
        {
            public string Name { get; set; } = string.Empty;
            // Use a Bitmap so the Image control can bind directly to an IImage instance
            public Bitmap? IconSource { get; set; }
        }

        private Bitmap? GetIconForFormat(string format)
        {
            // Map format names to the embedded provider logos that ship as
            // AvaloniaResource under "Entry Logos/Import logos". These are
            // loaded via avares:// (the previous file-system path under
            // Assets/Icons/Logos/import never existed, so all icons were blank).
            var fileName = format switch
            {
                "1Password CSV" => "Password.png",
                "Bitwarden JSON" => "Bitwarden.png",
                "Bitwarden CSV" => "Bitwarden.png",
                "Proton Pass JSON" => "ProtonPass.png",
                "KeePass XML" => "Keepassxc.png",
                "KeePass KDBX" => "Keepassxc.png",
                "LastPass CSV" => "Lastpass.png",
                "Chrome CSV" => "Chrome.png",
                "Edge CSV" => "Edge.png",
                "Firefox CSV" => "Firefox.png",
                "JSON" => "Json.png",
                "CSV" => "Cvs.png",
                _ => "Json.png",
            };

            if (_iconCache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            Bitmap? loaded = null;
            try
            {
                const string baseUri = "avares://PhantomVault.UI/Assets/Visuals/Entry Logos/Entry Logos/Import logos/";
                var uri = new Uri(baseUri + fileName);
                if (!AssetLoader.Exists(uri))
                {
                    uri = new Uri(baseUri + "Json.png");
                }
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    loaded = new Bitmap(stream);
                }
                else
                {
                    Console.WriteLine($"[ImportViewModel] Embedded icon not found for '{format}' ({fileName}).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImportViewModel] Failed to load icon for '{format}': {ex}");
            }

            _iconCache[fileName] = loaded;
            return loaded;
        }

        private async Task<bool> BrowseForFileAsync()
        {
            if (_ownerWindow == null)
                return false;

            var options = new FilePickerOpenOptions
            {
                Title = "Select Import File",
                AllowMultiple = false,
                FileTypeFilter = SelectedFormat switch
                {
                    "CSV" or "1Password CSV" or "Bitwarden CSV" or "LastPass CSV" or "Chrome CSV" or "Edge CSV" or "Firefox CSV"
                        => new[] { new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } },
                    "KeePass XML" => new[] { new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } } },
                    "KeePass KDBX" => new[] { new FilePickerFileType("KeePass Database") { Patterns = new[] { "*.kdbx" } } },
                    "JSON" or "Bitwarden JSON" or "Proton Pass JSON" => new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } },
                    _ => null
                }
            };

            var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(options);

            if (files.Count == 0)
            {
                SelectedFile = string.Empty;
                PreviewCount = 0;
                PreviewCredentials.Clear();
                StatusMessage = "Import selection cancelled.";
                return false;
            }

            if (files.Count > 0)
            {
                SelectedFile = files[0].Path.LocalPath;
                StatusMessage = $"Selected: {System.IO.Path.GetFileName(SelectedFile)}";

                // Auto-detect format
                await AutoDetectFormatAsync();

                // Load preview
                await LoadPreviewAsync();
                return true;
            }

            return false;
        }

        private async Task AutoDetectFormatAsync()
        {
            try
            {
                StatusMessage = "Detecting file format...";

                var detectedFormat = await _importExportService.DetectFormatAsync(SelectedFile);

                if (!string.IsNullOrEmpty(detectedFormat))
                {
                    // Check if detected format is in our supported list
                    if (Formats.Contains(detectedFormat))
                    {
                        SelectedFormat = detectedFormat;
                        StatusMessage = $"Detected: {detectedFormat} - {System.IO.Path.GetFileName(SelectedFile)}";
                    }
                    else
                    {
                        StatusMessage = $"Detected format '{detectedFormat}' not directly supported. Please select manually.";
                    }
                }
                else
                {
                    StatusMessage = $"❓ Unable to auto-detect format. Please select the format manually.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Format detection failed: {ex.Message}. Please select manually.";
            }
        }

        private async Task LoadPreviewAsync()
        {
            try
            {
                List<Credential> credentials;

                switch (SelectedFormat)
                {
                    case "CSV":
                        credentials = await _importExportService.ImportFromCsvAsync(SelectedFile);
                        break;
                    case "KeePass XML":
                        credentials = await _importExportService.ImportFromKeePassXmlAsync(SelectedFile);
                        break;
                    case "KeePass KDBX":
                        credentials = await ImportFromKdbxAsync();
                        break;
                    case "JSON":
                        credentials = await _importExportService.ImportFromJsonAsync(SelectedFile);
                        break;
                    case "1Password CSV":
                        credentials = await _importExportService.ImportFrom1PasswordCsvAsync(SelectedFile);
                        break;
                    case "Bitwarden JSON":
                        credentials = await _importExportService.ImportFromBitwardenJsonAsync(SelectedFile);
                        break;
                    case "Bitwarden CSV":
                        credentials = await _importExportService.ImportFromBitwardenCsvAsync(SelectedFile);
                        break;
                    case "Proton Pass JSON":
                        credentials = await _importExportService.ImportFromProtonPassJsonAsync(SelectedFile);
                        break;
                    case "LastPass CSV":
                        credentials = await _importExportService.ImportFromLastPassCsvAsync(SelectedFile);
                        break;
                    case "Chrome CSV":
                    case "Edge CSV":
                        credentials = await _importExportService.ImportFromChromeCsvAsync(SelectedFile);
                        break;
                    case "Firefox CSV":
                        credentials = await _importExportService.ImportFromFirefoxCsvAsync(SelectedFile);
                        break;
                    default:
                        credentials = new List<Credential>();
                        break;
                }

                PreviewCount = credentials.Count;
                PreviewCredentials = new ObservableCollection<Credential>(credentials.Take(5)); // Show first 5
                StatusMessage = $"Found {PreviewCount} credential(s). Preview showing first {Math.Min(5, PreviewCount)}.";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Import Preview Error",
                    $"Failed to preview file:\n{ex.Message}",
                    _ownerWindow
                );
                PreviewCount = 0;
                PreviewCredentials.Clear();
                StatusMessage = "Failed to load preview. Please check the file format.";
            }
        }

        private async Task ExecuteImportAsync()
        {
            if (string.IsNullOrEmpty(SelectedFile)) return;

            IsImporting = true;
            StatusMessage = "Importing credentials...";

            try
            {
                ImportResult importResult;

                if (string.Equals(SelectedFormat, "KeePass KDBX", StringComparison.OrdinalIgnoreCase))
                {
                    var importedCredentials = await ImportFromKdbxAsync();
                    if (importedCredentials.Count == 0)
                    {
                        StatusMessage = "No credentials were imported.";
                        return;
                    }

                    importResult = _importExportService.BuildImportResult(importedCredentials, _existingCredentials);
                }
                else
                {
                    var progress = new Progress<ImportProgress>(p =>
                    {
                        StatusMessage = $"Importing... {p.PercentComplete}% ({p.ProcessedItems}/{p.TotalItems}) - {p.CurrentItem}";
                    });

                    importResult = await _importExportService.ImportWithDuplicateDetectionAsync(
                        SelectedFile,
                        SelectedFormat,
                        _existingCredentials,
                        progress
                    );
                }

                // Handle duplicates if any
                var credentialsToImport = importResult.SuccessfulCredentials;
                if (importResult.Duplicates.Any())
                {
                    var reviewChoice = await _dialogService.ShowConfirmationAsync(
                        "Duplicates Detected",
                        $"Found {importResult.DuplicateCount} duplicate credential(s).\n\n" +
                        $"Do you want to manually review each duplicate?\n\n" +
                        $"• Yes - Review side-by-side and choose which to keep\n" +
                        $"• No - Automatically keep most recently created",
                        _ownerWindow
                    );

                    if (reviewChoice)
                    {
                        // Show merge window for manual selection
                        var mergeViewModel = new MergeCredentialsViewModel(importResult.Duplicates);
                        var mergeWindow = new Views.MergeCredentialsWindow
                        {
                            DataContext = mergeViewModel
                        };

                        await mergeWindow.ShowDialog(_ownerWindow!);

                        if (mergeViewModel.IsMerged)
                        {
                            // Apply user choices from the merge window
                            credentialsToImport = ApplyMergeChoices(
                                importResult.SuccessfulCredentials,
                                mergeViewModel.ResolvedDuplicates
                            );
                        }
                        else
                        {
                            // User cancelled - abort import
                            StatusMessage = "Import cancelled by user.";
                            IsImporting = false;
                            return;
                        }
                    }
                    else
                    {
                        // Auto-apply smart default resolution (keep most recent)
                        credentialsToImport = _importExportService.ApplyDuplicateResolution(
                            importResult.SuccessfulCredentials,
                            importResult.Duplicates
                        );
                    }
                }

                // Show import summary
                await _dialogService.ShowImportSummaryAsync(
                    "Import Complete",
                    importResult,
                    _ownerWindow
                );

                // Return credentials to caller
                if (credentialsToImport.Any())
                {
                    ImportCompleted?.Invoke(this, credentialsToImport);
                    StatusMessage = $"Imported {credentialsToImport.Count} credential(s).";
                }
                else
                {
                    StatusMessage = "Import completed with no new credentials.";
                }

                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Import Failed",
                    $"Failed to import credentials:\n{ex.Message}\n\nPlease verify the file format and try again.",
                    _ownerWindow
                );
                StatusMessage = "Import failed. Please check the error message.";
            }
            finally
            {
                IsImporting = false;
            }
        }

        private List<Credential> ApplyMergeChoices(
            List<Credential> allCredentials,
            List<DuplicateInfo> resolvedDuplicates)
        {
            var result = new List<Credential>();
            var duplicateNewCredentials = new HashSet<Credential>(resolvedDuplicates.Select(d => d.NewCredential));

            // Add all non-duplicate credentials
            result.AddRange(allCredentials.Where(c => !duplicateNewCredentials.Contains(c)));

            // Process resolved duplicates based on user choices
            foreach (var duplicate in resolvedDuplicates)
            {
                if (duplicate.KeepNew)
                {
                    // Keep new credential (will replace existing)
                    result.Add(duplicate.NewCredential);
                }
                // If KeepNew is false, we keep existing (don't add new credential)
            }

            return result;
        }

        private List<Credential> ApplyUserDuplicateChoices(
            List<Credential> allCredentials,
            List<DuplicateInfo> duplicates,
            Dictionary<DuplicateInfo, DuplicateChoice> userChoices)
        {
            var result = new List<Credential>();
            var duplicateNewCredentials = new HashSet<Credential>(duplicates.Select(d => d.NewCredential));
            var duplicateExistingCredentials = new HashSet<Credential>(
                duplicates.Select(d => d.ExistingCredential).Where(c => c != null)!
            );

            // Add all non-duplicate credentials
            result.AddRange(allCredentials.Where(c => !duplicateNewCredentials.Contains(c)));

            // Process user choices for duplicates
            foreach (var duplicate in duplicates)
            {
                var choice = userChoices.ContainsKey(duplicate)
                    ? userChoices[duplicate]
                    : DuplicateChoice.KeepNew; // Default to keep new

                switch (choice)
                {
                    case DuplicateChoice.KeepExisting:
                        // Don't add the new credential (existing is already in vault)
                        break;

                    case DuplicateChoice.KeepNew:
                        // Add new credential (will replace existing)
                        result.Add(duplicate.NewCredential);
                        break;

                    case DuplicateChoice.KeepBoth:
                        // Add new credential with modified title to avoid confusion
                        var newCred = duplicate.NewCredential;
                        if (!string.IsNullOrEmpty(newCred.Title) && !newCred.Title.EndsWith(" (imported)"))
                        {
                            newCred.Title += " (imported)";
                        }
                        result.Add(newCred);
                        break;
                }
            }

            return result;
        }

        private async Task<List<Credential>> ImportFromKdbxAsync()
        {
            // Prompt for password and keyfile
            var credentials = await KeePassPasswordDialog.ShowAsync(_ownerWindow);
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.Value.password))
            {
                StatusMessage = "KDBX import cancelled - password required";
                return new List<Credential>();
            }

            var (password, keyfilePath) = credentials.Value;

            // Use KeePassImportService to import
            var keePassService = new KeePassImportService();
            var progressReporter = new Progress<int>(percent =>
            {
                StatusMessage = $"Importing KeePass database... {percent}%";
            });

            var result = await keePassService.ImportAsync(SelectedFile, password!, keyfilePath, progressReporter);

            if (!result.IsSuccess)
            {
                await _dialogService.ShowErrorAsync(
                    "KDBX Import Failed",
                    result.Message,
                    _ownerWindow
                );
                StatusMessage = "KDBX import failed";
                return new List<Credential>();
            }

            StatusMessage = $"Successfully imported {result.TotalEntries} credentials from {result.TotalGroups} groups";
            return result.Credentials;
        }

        private void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
