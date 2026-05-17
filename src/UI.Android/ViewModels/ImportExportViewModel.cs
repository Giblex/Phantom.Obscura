using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class ImportExportViewModel : BaseViewModel
    {
        private readonly ImportExportService _importExportService;
        private readonly VaultViewModel _vaultViewModel;

        [ObservableProperty]
        private string _importFilePath = string.Empty;

        [ObservableProperty]
        private string _exportFilePath = string.Empty;

        [ObservableProperty]
        private int _importedCount;

        [ObservableProperty]
        private int _exportedCount;

        [ObservableProperty]
        private string _lastOperationResult = string.Empty;

        [ObservableProperty]
        private bool _hasResult;

        [ObservableProperty]
        private bool _resultIsSuccess;

        public ImportExportViewModel(ImportExportService importExportService, VaultViewModel vaultViewModel)
        {
            _importExportService = importExportService;
            _vaultViewModel = vaultViewModel;
        }

        [RelayCommand]
        private async Task PickAndImportAsync()
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Select vault file to import",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "*/*" } }
                    })
                });

                if (result is null) return;

                IsBusy = true;
                StatusMessage = "Importing…";
                ImportFilePath = result.FullPath;

                var format = await _importExportService.DetectFormatAsync(result.FullPath);

                List<Credential> credentials;
                if (format == "csv")
                    credentials = await _importExportService.ImportFromCsvAsync(result.FullPath);
                else
                    credentials = await _importExportService.ImportFromCsvAsync(result.FullPath); // fallback

                ImportedCount = credentials.Count;
                foreach (var cred in credentials)
                    _vaultViewModel.AddCredentialFromImport(cred);

                SetResult(true, $"Imported {credentials.Count} credential{(credentials.Count == 1 ? "" : "s")} successfully.");
            }
            catch (Exception ex)
            {
                SetResult(false, $"Import failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ExportAsCsvAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exporting…";

                var credentials = _vaultViewModel.Credentials.ToList();
                if (credentials.Count == 0)
                {
                    SetResult(false, "No credentials to export.");
                    return;
                }

                var folder = FileSystem.Current.AppDataDirectory;
                var fileName = $"phantom_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = Path.Combine(folder, fileName);

                await _importExportService.ExportToCsvAsync(credentials, path);
                ExportedCount = credentials.Count;
                ExportFilePath = path;

                // Share the file so user can save it externally
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Save Phantom Obscura export",
                    File = new ShareFile(path)
                });

                SetResult(true, $"Exported {credentials.Count} credential{(credentials.Count == 1 ? "" : "s")}.");
            }
            catch (Exception ex)
            {
                SetResult(false, $"Export failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SetResult(bool success, string message)
        {
            ResultIsSuccess = success;
            LastOperationResult = message;
            StatusMessage = message;
            HasResult = true;
        }
    }
}
