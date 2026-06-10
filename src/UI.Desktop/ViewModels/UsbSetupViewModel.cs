using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{

    public sealed class UsbSetupViewModel : ReactiveObject, PhantomVault.UI.Services.IResettableOnError
    {
        private readonly UsbDetector _usbDetector;
        private readonly ObservableCollection<string> _drives = new();
        private string? _selectedDrive;
        private string _statusMessage = string.Empty;
        private string _selectedDriveTotalSize = string.Empty;
        private string _selectedDriveFreeSpace = string.Empty;
        private string _selectedDriveFormat = string.Empty;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;

        public event EventHandler? NavigateBack;
        public event EventHandler<string>? NavigateToContinue;

        public UsbSetupViewModel(UsbDetector usbDetector)
        {
            _usbDetector = usbDetector;
            _dialogService = new DialogService();
            Refresh();
            _usbDetector.RemovableDriveInserted += d => _drives.Add(d);
            _usbDetector.RemovableDriveRemoved += d => _drives.Remove(d);

            RefreshCommand = ReactiveCommand.Create(Refresh);
            BrowseCommand = ReactiveCommand.CreateFromTask(BrowseForDriveAsync);

            var canContinue = this.WhenAnyValue(
                x => x.SelectedDrive,
                x => x.StatusMessage,
                (drive, status) => !string.IsNullOrWhiteSpace(drive) &&
                                   !status.StartsWith("⚠") &&
                                   !status.StartsWith("Error"));

            ContinueCommand = ReactiveCommand.CreateFromTask(ContinueAsync, canContinue);
            GoBackCommand = ReactiveCommand.Create(GoBack);

            this.WhenAnyValue(x => x.SelectedDrive)
                .Subscribe(_ => UpdateSelectedDriveInfo());
        }

        private void Refresh()
        {
            _drives.Clear();
            foreach (var drive in _usbDetector.GetRemovableDrives())
            {
                _drives.Add(drive);
            }

            if (_selectedDrive == null && _drives.Count > 0)
            {
                SelectedDrive = _drives[0];
            }
        }

        private void UpdateSelectedDriveInfo()
        {
            if (string.IsNullOrWhiteSpace(_selectedDrive))
            {
                SelectedDriveTotalSize = string.Empty;
                SelectedDriveFreeSpace = string.Empty;
                SelectedDriveFormat = string.Empty;
                return;
            }

            try
            {
                var driveInfo = new DriveInfo(_selectedDrive);

                if (driveInfo.IsReady)
                {
                    SelectedDriveTotalSize = FormatBytes(driveInfo.TotalSize);
                    SelectedDriveFreeSpace = FormatBytes(driveInfo.AvailableFreeSpace);
                    SelectedDriveFormat = driveInfo.DriveFormat;

                    if (driveInfo.AvailableFreeSpace < 100 * 1024 * 1024)
                    {
                        StatusMessage = "Warning: Less than 100 MB free space available";
                    }
                    else
                    {
                        StatusMessage = string.Empty;
                    }
                }
                else
                {
                    StatusMessage = "Drive is not ready. Please check the drive.";
                    SelectedDriveTotalSize = "N/A";
                    SelectedDriveFreeSpace = "N/A";
                    SelectedDriveFormat = "N/A";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading drive info: {ex.Message}";
                SelectedDriveTotalSize = "N/A";
                SelectedDriveFreeSpace = "N/A";
                SelectedDriveFormat = "N/A";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private async Task BrowseForDriveAsync()
        {
            try
            {

                var topLevel = _ownerWindow;

                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null) return;

                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select USB Drive or Folder",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    var selectedPath = folders[0].Path.LocalPath;

                    if (Directory.Exists(selectedPath))
                    {

                        var matchingDrive = _drives.FirstOrDefault(d =>
                            selectedPath.StartsWith(d, StringComparison.OrdinalIgnoreCase));

                        if (matchingDrive != null)
                        {

                            SelectedDrive = matchingDrive;
                            var driveInfo = new DriveInfo(matchingDrive);
                            StatusMessage = $"Selected: {driveInfo.VolumeLabel} ({matchingDrive})";
                        }
                        else
                        {

                            SelectedDrive = selectedPath;
                            StatusMessage = $"Selected folder: {selectedPath}";
                        }

                        await Task.Delay(3000);
                        StatusMessage = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting folder: {ex.Message}";
            }
        }

        private async Task ContinueAsync()
        {
            if (string.IsNullOrWhiteSpace(_selectedDrive))
            {
                await _dialogService.ShowWarningAsync(
                    "USB Drive Required",
                    "Please select a USB drive from the dropdown list before continuing.",
                    _ownerWindow);
                StatusMessage = "Please select a USB drive first";
                return;
            }

            try
            {
                var driveInfo = new DriveInfo(_selectedDrive);

                if (!driveInfo.IsReady)
                {
                    await _dialogService.ShowWarningAsync(
                        "Drive Not Ready",
                        "The selected drive is not ready. Please check the drive connection and try again.",
                        _ownerWindow);
                    StatusMessage = "Selected drive is not ready";
                    return;
                }

                var pvaultPath = Path.Combine(_selectedDrive, "vault.pvault");
                var manifestPath = Path.Combine(_selectedDrive, "vault.manifest");
                if (File.Exists(pvaultPath) || File.Exists(manifestPath))
                {
                    bool proceed = await _dialogService.ShowConfirmationAsync(
                        "Vault Already Exists",
                        "A vault already exists on this drive. Do you want to overwrite it? This action cannot be undone.",
                        _ownerWindow);

                    if (!proceed)
                    {
                        StatusMessage = "A vault already exists on this drive. Please choose a different drive or delete the existing vault.";
                        return;
                    }
                }

                StatusMessage = "Drive validated. Proceeding to vault setup...";
                await Task.Delay(1000);

                NavigateToContinue?.Invoke(this, _selectedDrive);
            }
            catch (Exception ex)
            {

                try
                {
                    Console.WriteLine(ex.ToString());
                    var logDir = Path.Combine(Path.GetTempPath(), "PhantomVaultLogs");
                    Directory.CreateDirectory(logDir);
                    var logPath = Path.Combine(logDir, "usb-setup-errors.log");
                    File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] {ex}\n\n");
                }
                catch {  }

                await _dialogService.ShowErrorAsync(
                    "Drive Validation Error",
                    $"An error occurred while validating the drive:\n\n{ex.Message}",
                    _ownerWindow);
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void GoBack()
        {
            StatusMessage = "Going back to welcome page...";
            NavigateBack?.Invoke(this, EventArgs.Empty);
        }

        public ObservableCollection<string> Drives => _drives;

        public string? SelectedDrive
        {
            get => _selectedDrive;
            set => this.RaiseAndSetIfChanged(ref _selectedDrive, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string SelectedDriveTotalSize
        {
            get => _selectedDriveTotalSize;
            set => this.RaiseAndSetIfChanged(ref _selectedDriveTotalSize, value);
        }

        public string SelectedDriveFreeSpace
        {
            get => _selectedDriveFreeSpace;
            set => this.RaiseAndSetIfChanged(ref _selectedDriveFreeSpace, value);
        }

        public string SelectedDriveFormat
        {
            get => _selectedDriveFormat;
            set => this.RaiseAndSetIfChanged(ref _selectedDriveFormat, value);
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        public async Task ResetAfterErrorAsync()
        {
            try
            {

                StatusMessage = string.Empty;
                await Task.Run(() => Refresh());

                if (_selectedDrive == null && _drives.Count > 0)
                {
                    SelectedDrive = _drives[0];
                }
            }
            catch
            {

            }
        }

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> ContinueCommand { get; }
        public ReactiveCommand<Unit, Unit> GoBackCommand { get; }
    }
}

