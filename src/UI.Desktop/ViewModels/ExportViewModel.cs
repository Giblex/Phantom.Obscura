using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for exporting credentials to external formats (CSV, KeePass XML, JSON).
    /// </summary>
    public sealed class ExportViewModel : ReactiveObject
    {
        private readonly ImportExportService _importExportService;
        private readonly DialogService _dialogService;
        private readonly List<Credential> _credentials;
        private readonly IExportGuard? _exportGuard;
        private Window? _ownerWindow;

        private string _selectedFormat = "CSV";
        private string _destinationFile = string.Empty;
        private string _statusMessage = "Select export format and destination.";
        private bool _isExporting = false;
        private bool _includePasswords = true;
        private string _filterGroup = "All Groups";

        public event EventHandler? CloseRequested;

        public ExportViewModel(List<Credential> credentials, IExportGuard? exportGuard = null)
        {
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _exportGuard = exportGuard;
            _importExportService = new ImportExportService();
            _dialogService = new DialogService();

            Formats = new ObservableCollection<string> { "CSV", "KeePass XML", "JSON" };

            // Get unique groups
            var groups = new List<string> { "All Groups" };
            groups.AddRange(_credentials.Select(c => c.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g));
            Groups = new ObservableCollection<string>(groups);

            BrowseCommand = ReactiveCommand.CreateFromTask(BrowseForDestinationAsync);
            ExportCommand = ReactiveCommand.CreateFromTask(ExecuteExportAsync,
                this.WhenAnyValue(x => x.DestinationFile, x => x.IsExporting,
                    (file, exporting) => !string.IsNullOrEmpty(file) && !exporting));
            CloseCommand = ReactiveCommand.Create(Close);

            UpdateCredentialCount();
        }

        public void SetOwner(Window owner)
        {
            _ownerWindow = owner;
        }

        public ObservableCollection<string> Formats { get; }
        public ObservableCollection<string> Groups { get; }

        public string SelectedFormat
        {
            get => _selectedFormat;
            set => this.RaiseAndSetIfChanged(ref _selectedFormat, value);
        }

        public string FilterGroup
        {
            get => _filterGroup;
            set
            {
                this.RaiseAndSetIfChanged(ref _filterGroup, value);
                UpdateCredentialCount();
            }
        }

        public string DestinationFile
        {
            get => _destinationFile;
            set => this.RaiseAndSetIfChanged(ref _destinationFile, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsExporting
        {
            get => _isExporting;
            set => this.RaiseAndSetIfChanged(ref _isExporting, value);
        }

        public bool IncludePasswords
        {
            get => _includePasswords;
            set => this.RaiseAndSetIfChanged(ref _includePasswords, value);
        }

        public int CredentialCount => GetFilteredCredentials().Count;

        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void UpdateCredentialCount()
        {
            this.RaisePropertyChanged(nameof(CredentialCount));
            StatusMessage = $"Ready to export {CredentialCount} credential(s) to {SelectedFormat}.";
        }

        private List<Credential> GetFilteredCredentials()
        {
            var filtered = FilterGroup == "All Groups"
                ? _credentials
                : _credentials.Where(c => c.Group == FilterGroup).ToList();

            if (!IncludePasswords)
            {
                // Create copies without passwords
                return filtered.Select(c => new Credential
                {
                    Title = c.Title,
                    Username = c.Username,
                    Password = "[REDACTED]",
                    Url = c.Url,
                    Notes = c.Notes,
                    Group = c.Group,
                    Icon = c.Icon,
                    IconColor = c.IconColor,
                    Tags = c.Tags,
                    CreatedUtc = c.CreatedUtc,
                    LastUpdatedUtc = c.LastUpdatedUtc,
                    ExpiryUtc = c.ExpiryUtc
                }).ToList();
            }

            return filtered;
        }

        private async Task BrowseForDestinationAsync()
        {
            if (_ownerWindow == null) return;

            var extension = SelectedFormat switch
            {
                "CSV" => ".csv",
                "KeePass XML" => ".xml",
                "JSON" => ".json",
                _ => ".txt"
            };

            var options = new FilePickerSaveOptions
            {
                Title = "Select Export Destination",
                SuggestedFileName = $"PhantomVault_Export_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
                DefaultExtension = extension,
                FileTypeChoices = SelectedFormat switch
                {
                    "CSV" => new[] { new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } },
                    "KeePass XML" => new[] { new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } } },
                    "JSON" => new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } },
                    _ => null
                }
            };

            var file = await _ownerWindow.StorageProvider.SaveFilePickerAsync(options);

            if (file != null)
            {
                DestinationFile = file.Path.LocalPath;
                StatusMessage = $"Export to: {System.IO.Path.GetFileName(DestinationFile)}";
            }
        }

        private async Task ExecuteExportAsync()
        {
            if (string.IsNullOrEmpty(DestinationFile)) return;

            // Check export guard
            if (_exportGuard != null && !_exportGuard.CanExport(SelectedFormat))
            {
                await _dialogService.ShowWarningAsync(
                    "Export Blocked",
                    "Too many export operations detected. Please wait before exporting again.",
                    _ownerWindow
                );
                StatusMessage = "Export temporarily blocked";
                return;
            }

            // Security confirmation for password export
            if (IncludePasswords)
            {
                var confirm = await _dialogService.ShowConfirmationAsync(
                    "Security Warning",
                    $"You are about to export {CredentialCount} credential(s) including passwords to an unencrypted file.\n\n" +
                    "This file will contain sensitive information in plain text. " +
                    "Ensure you store it securely and delete it when no longer needed.\n\n" +
                    "Do you want to continue?",
                    _ownerWindow
                );

                if (!confirm) return;
            }

            IsExporting = true;
            StatusMessage = "Exporting credentials...";

            try
            {
                var credentialsToExport = GetFilteredCredentials();

                switch (SelectedFormat)
                {
                    case "CSV":
                        await _importExportService.ExportToCsvAsync(credentialsToExport, DestinationFile);
                        break;
                    case "KeePass XML":
                        await _importExportService.ExportToKeePassXmlAsync(credentialsToExport, DestinationFile, "PhantomVault Export");
                        break;
                    case "JSON":
                        await _importExportService.ExportToJsonAsync(credentialsToExport, DestinationFile);
                        break;
                    default:
                        throw new NotSupportedException($"Format '{SelectedFormat}' is not supported");
                }

                // Register export with guard
                _exportGuard?.RegisterExport(SelectedFormat);

                await _dialogService.ShowSuccessAsync(
                    "Export Successful",
                    $"Successfully exported {credentialsToExport.Count} credential(s) to {SelectedFormat} file.\n\n" +
                    $"File: {System.IO.Path.GetFileName(DestinationFile)}\n\n" +
                    (IncludePasswords ? "⚠️ Remember to secure or delete this file after use!" : "Passwords were redacted for security."),
                    _ownerWindow
                );

                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Export Failed",
                    $"Failed to export credentials:\n{ex.Message}\n\nPlease check the destination path and permissions.",
                    _ownerWindow
                );
                StatusMessage = "Export failed. Please check the error message.";
            }
            finally
            {
                IsExporting = false;
            }
        }

        private void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
