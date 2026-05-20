using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Android port of the desktop ImportExportDialog (Phase 3g). The desktop
/// dialog branches on file extension (KeePass kdbx, Bitwarden json, generic
/// csv) and runs a full deduplicating import against the unlocked vault. The
/// mobile equivalent uses Avalonia's <see cref="IStorageProvider"/> to obtain
/// files through the Android Storage Access Framework, then defers parsing
/// to the (forthcoming) shared importer service.
///
/// The view supplies an <see cref="IStorageProvider"/> through
/// <see cref="ConfigureStorageProvider"/> from code-behind because mobile
/// pickers must originate from the TopLevel.
/// </summary>
public sealed partial class ImportExportViewModel : ObservableObject
{
    private IStorageProvider? _storageProvider;

    [ObservableProperty] private string _selectedFileName = string.Empty;
    [ObservableProperty] private string _status = "Pick a KeePass (.kdbx), Bitwarden (.json) or CSV file to import.";
    [ObservableProperty] private bool _isBusy;

    public void ConfigureStorageProvider(IStorageProvider provider)
    {
        _storageProvider = provider;
    }

    [RelayCommand]
    private async Task PickImportFileAsync()
    {
        if (_storageProvider is null)
        {
            Status = "Storage provider unavailable on this platform.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Opening file picker…";

            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a credential export",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Credential exports")
                    {
                        Patterns = new[] { "*.kdbx", "*.json", "*.csv" }
                    },
                    FilePickerFileTypes.All
                }
            });

            var file = files?.FirstOrDefault();
            if (file is null)
            {
                Status = "Import cancelled.";
                SelectedFileName = string.Empty;
                return;
            }

            SelectedFileName = file.Name;

            var ext = Path.GetExtension(file.Name).ToLowerInvariant();
            var kind = ext switch
            {
                ".kdbx" => "KeePass",
                ".json" => "Bitwarden",
                ".csv"  => "CSV",
                _ => "Unknown"
            };

            // Parsing is intentionally deferred to the shared importer service
            // that will be wired in once the Android Core data path is live.
            Status = $"Selected {kind} export ({file.Name}). Import will run once vault binding is wired.";
        }
        catch (Exception ex)
        {
            Status = $"Import picker failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PickExportTargetAsync()
    {
        if (_storageProvider is null)
        {
            Status = "Storage provider unavailable on this platform.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Opening save picker…";

            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Choose an encrypted backup target",
                SuggestedFileName = "phantom-vault-backup",
                DefaultExtension = "pvb",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Phantom Vault backup")
                    {
                        Patterns = new[] { "*.pvb" }
                    }
                }
            });

            if (file is null)
            {
                Status = "Export cancelled.";
                return;
            }

            SelectedFileName = file.Name;
            Status = $"Export target chosen: {file.Name}. Encrypted backup will run once vault binding is wired.";
        }
        catch (Exception ex)
        {
            Status = $"Export picker failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back() => ShellViewModel.Current?.GoBackCommand.Execute(null);
}
