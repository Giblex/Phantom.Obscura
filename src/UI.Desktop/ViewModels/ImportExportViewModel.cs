using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public class ImportExportViewModel : ReactiveObject
    {
        private string? _importStatusMessage;
        private string? _exportStatusMessage;
        private string? _selectedExportFormat;
        private string? _filterGroup;
        private string? _destinationFile;
        private bool _includePasswords = true;
        private int _exportCredentialCount;

        public ImportExportViewModel()
        {

            ExportFormats = new ObservableCollection<string>
            {
                "KeePass (.kdbx)",
                "CSV (.csv)",
                "JSON (.json)",
                "LastPass (.csv)",
                "1Password (.1pif)",
                "Bitwarden (.json)"
            };

            ExportGroups = new ObservableCollection<string>
            {
                "All Groups",
                "Logins",
                "Credit Cards",
                "Secure Notes",
                "Identities"
            };

            ImportKeePassCommand = ReactiveCommand.Create(ImportKeePass);
            ImportLastPassCommand = ReactiveCommand.Create(ImportLastPass);
            Import1PasswordCommand = ReactiveCommand.Create(Import1Password);
            ImportBitwardenCommand = ReactiveCommand.Create(ImportBitwarden);
            ImportDashlaneCommand = ReactiveCommand.Create(ImportDashlane);
            ImportCsvCommand = ReactiveCommand.Create(ImportCsv);
            BrowseExportCommand = ReactiveCommand.Create(BrowseExportDestination);
            ExportCommand = ReactiveCommand.Create(Export);
            CloseCommand = ReactiveCommand.Create(() => {  });

            SelectedExportFormat = ExportFormats[0];
            FilterGroup = ExportGroups[0];
        }

        public string? ImportStatusMessage
        {
            get => _importStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _importStatusMessage, value);
        }

        public ObservableCollection<string> ExportFormats { get; }
        public ObservableCollection<string> ExportGroups { get; }

        public string? SelectedExportFormat
        {
            get => _selectedExportFormat;
            set => this.RaiseAndSetIfChanged(ref _selectedExportFormat, value);
        }

        public string? FilterGroup
        {
            get => _filterGroup;
            set => this.RaiseAndSetIfChanged(ref _filterGroup, value);
        }

        public string? DestinationFile
        {
            get => _destinationFile;
            set => this.RaiseAndSetIfChanged(ref _destinationFile, value);
        }

        public bool IncludePasswords
        {
            get => _includePasswords;
            set => this.RaiseAndSetIfChanged(ref _includePasswords, value);
        }

        public int ExportCredentialCount
        {
            get => _exportCredentialCount;
            set => this.RaiseAndSetIfChanged(ref _exportCredentialCount, value);
        }

        public string? ExportStatusMessage
        {
            get => _exportStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _exportStatusMessage, value);
        }

        public ReactiveCommand<Unit, Unit> ImportKeePassCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportLastPassCommand { get; }
        public ReactiveCommand<Unit, Unit> Import1PasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportBitwardenCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportDashlaneCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportCsvCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseExportCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void ImportKeePass()
        {
            ImportStatusMessage = "KeePass import selected. Opening file picker...";

        }

        private void ImportLastPass()
        {
            ImportStatusMessage = "LastPass import selected. Opening file picker...";

        }

        private void Import1Password()
        {
            ImportStatusMessage = "1Password import selected. Opening file picker...";

        }

        private void ImportBitwarden()
        {
            ImportStatusMessage = "Bitwarden import selected. Opening file picker...";

        }

        private void ImportDashlane()
        {
            ImportStatusMessage = "Dashlane import selected. Opening file picker...";

        }

        private void ImportCsv()
        {
            ImportStatusMessage = "CSV import selected. Opening file picker...";

        }

        private void BrowseExportDestination()
        {
            ExportStatusMessage = "Opening file picker for export destination...";

        }

        private void Export()
        {
            ExportStatusMessage = $"Exporting {ExportCredentialCount} credentials...";

        }
    }
}

