using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ReactiveUI;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the installer/setup screen that handles VeraCrypt installation
    /// and other component configuration.
    /// </summary>
    public class InstallerViewModel : ReactiveObject
    {
        // VeraCrypt download URLs (official sources)
        private const string VeraCryptDownloadUrl64 = "https://launchpad.net/veracrypt/trunk/1.26.15/+download/VeraCrypt%20Setup%201.26.15.exe";
        private const string VeraCryptDownloadUrl32 = "https://launchpad.net/veracrypt/trunk/1.26.15/+download/VeraCrypt%20Setup%201.26.15.exe";
        private const string VeraCryptWebsite = "https://www.veracrypt.fr/en/Downloads.html";
        private const string VeraCryptVersion = "1.26.15";
        private const long ExpectedDownloadSize = 35_000_000; // ~35 MB

        private readonly IVeraCryptService _veraCryptService;
        private readonly DialogService _dialogService;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _installationCts;
        private Window? _ownerWindow;

        // State properties
        private bool _isVeraCryptInstalled;
        private string _veraCryptPath = string.Empty;
        private string _veraCryptVersionText = string.Empty;
        private bool _isInstalling;
        private bool _isProgressIndeterminate;
        private int _installationProgress;
        private string _installationStatus = string.Empty;
        private string _installationDetails = string.Empty;
        private bool _canCancelInstallation;
        private int _stepProgress = 25;
        private string _currentStepTitle = "Step 1: Security Components";
        private string _stepDescription = "Configure encryption and authentication components for your vault.";

        // Windows Hello / YubiKey status
        private bool _isWindowsHelloAvailable;
        private bool _isYubiKeyDetected;

        public event EventHandler? NavigateToWelcome;
        public event EventHandler? CloseRequested;

        public InstallerViewModel() : this(new VeraCryptService())
        {
        }

        public InstallerViewModel(IVeraCryptService veraCryptService)
        {
            _veraCryptService = veraCryptService ?? throw new ArgumentNullException(nameof(veraCryptService));
            _dialogService = new DialogService();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PhantomVault/1.0");

            // Initialize commands
            DownloadAndInstallVeraCryptCommand = ReactiveCommand.CreateFromTask(DownloadAndInstallVeraCryptAsync);
            BrowseVeraCryptCommand = ReactiveCommand.CreateFromTask(BrowseVeraCryptAsync);
            OpenVeraCryptWebsiteCommand = ReactiveCommand.Create(OpenVeraCryptWebsite);
            CancelInstallationCommand = ReactiveCommand.Create(CancelInstallation);
            SkipSetupCommand = ReactiveCommand.Create(SkipSetup);
            RefreshStatusCommand = ReactiveCommand.CreateFromTask(RefreshStatusAsync);
            ContinueCommand = ReactiveCommand.Create(Continue);

            // Check initial status
            _ = RefreshStatusAsync();
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        #region Properties

        public bool IsVeraCryptInstalled
        {
            get => _isVeraCryptInstalled;
            private set => this.RaiseAndSetIfChanged(ref _isVeraCryptInstalled, value);
        }

        public string VeraCryptPath
        {
            get => _veraCryptPath;
            private set => this.RaiseAndSetIfChanged(ref _veraCryptPath, value);
        }

        public string InstalledVeraCryptVersion
        {
            get => _veraCryptVersionText;
            private set => this.RaiseAndSetIfChanged(ref _veraCryptVersionText, value);
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isInstalling, value);
                this.RaisePropertyChanged(nameof(CanContinue));
            }
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            private set => this.RaiseAndSetIfChanged(ref _isProgressIndeterminate, value);
        }

        public int InstallationProgress
        {
            get => _installationProgress;
            private set => this.RaiseAndSetIfChanged(ref _installationProgress, value);
        }

        public string InstallationStatus
        {
            get => _installationStatus;
            private set => this.RaiseAndSetIfChanged(ref _installationStatus, value);
        }

        public string InstallationDetails
        {
            get => _installationDetails;
            private set => this.RaiseAndSetIfChanged(ref _installationDetails, value);
        }

        public bool CanCancelInstallation
        {
            get => _canCancelInstallation;
            private set => this.RaiseAndSetIfChanged(ref _canCancelInstallation, value);
        }

        public int StepProgress
        {
            get => _stepProgress;
            private set => this.RaiseAndSetIfChanged(ref _stepProgress, value);
        }

        public string CurrentStepTitle
        {
            get => _currentStepTitle;
            private set => this.RaiseAndSetIfChanged(ref _currentStepTitle, value);
        }

        public string StepDescription
        {
            get => _stepDescription;
            private set => this.RaiseAndSetIfChanged(ref _stepDescription, value);
        }

        public bool CanContinue => !IsInstalling;

        public string DownloadSizeText => $"Download size: ~{ExpectedDownloadSize / 1_000_000} MB";

        // Status display properties
        public string VeraCryptStatusText => IsVeraCryptInstalled ? "Installed" : "Not Installed";
        public string VeraCryptStatusIcon => IsVeraCryptInstalled ? "✓" : "⚠";
        public IBrush VeraCryptStatusBackground => IsVeraCryptInstalled
            ? new SolidColorBrush(Color.FromRgb(34, 139, 34), 0.2)   // Green
            : new SolidColorBrush(Color.FromRgb(255, 165, 0), 0.2);  // Orange

        public string WindowsHelloStatusText => _isWindowsHelloAvailable ? "Available" : "Not Available";
        public string WindowsHelloStatusIcon => _isWindowsHelloAvailable ? "✓" : "○";
        public IBrush WindowsHelloStatusBackground => _isWindowsHelloAvailable
            ? new SolidColorBrush(Color.FromRgb(34, 139, 34), 0.2)
            : new SolidColorBrush(Color.FromRgb(128, 128, 128), 0.2);

        public string YubiKeyStatusText => _isYubiKeyDetected ? "Detected" : "Not Detected";
        public string YubiKeyStatusIcon => _isYubiKeyDetected ? "✓" : "○";
        public IBrush YubiKeyStatusBackground => _isYubiKeyDetected
            ? new SolidColorBrush(Color.FromRgb(34, 139, 34), 0.2)
            : new SolidColorBrush(Color.FromRgb(128, 128, 128), 0.2);

        #endregion

        #region Commands

        public ReactiveCommand<Unit, Unit> DownloadAndInstallVeraCryptCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseVeraCryptCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenVeraCryptWebsiteCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelInstallationCommand { get; }
        public ReactiveCommand<Unit, Unit> SkipSetupCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshStatusCommand { get; }
        public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

        #endregion

        #region Methods

        private async Task RefreshStatusAsync()
        {
            try
            {
                // Check VeraCrypt
                IsVeraCryptInstalled = _veraCryptService.IsVeraCryptInstalled();
                if (IsVeraCryptInstalled)
                {
                    VeraCryptPath = _veraCryptService.VeraCryptPath;
                    InstalledVeraCryptVersion = await GetVeraCryptVersionAsync();
                }
                else
                {
                    VeraCryptPath = string.Empty;
                    InstalledVeraCryptVersion = string.Empty;
                }

                // Check Windows Hello availability (Windows only)
                _isWindowsHelloAvailable = await CheckWindowsHelloAvailabilityAsync();

                // Check YubiKey (simplified check)
                _isYubiKeyDetected = CheckYubiKeyDetected();

                // Update UI
                this.RaisePropertyChanged(nameof(VeraCryptStatusText));
                this.RaisePropertyChanged(nameof(VeraCryptStatusIcon));
                this.RaisePropertyChanged(nameof(VeraCryptStatusBackground));
                this.RaisePropertyChanged(nameof(WindowsHelloStatusText));
                this.RaisePropertyChanged(nameof(WindowsHelloStatusIcon));
                this.RaisePropertyChanged(nameof(WindowsHelloStatusBackground));
                this.RaisePropertyChanged(nameof(YubiKeyStatusText));
                this.RaisePropertyChanged(nameof(YubiKeyStatusIcon));
                this.RaisePropertyChanged(nameof(YubiKeyStatusBackground));

                // Update progress based on what's installed
                UpdateStepProgress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Installer] Error refreshing status: {ex.Message}");
            }
        }

        private void UpdateStepProgress()
        {
            int progress = 25; // Base progress
            if (IsVeraCryptInstalled) progress += 50;
            if (_isWindowsHelloAvailable) progress += 12;
            if (_isYubiKeyDetected) progress += 13;
            StepProgress = Math.Min(progress, 100);
        }

        private Task<string> GetVeraCryptVersionAsync()
        {
            try
            {
                var path = _veraCryptService.VeraCryptPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    return Task.FromResult(versionInfo.FileVersion ?? "Unknown");
                }
            }
            catch
            {
                // Ignore version detection errors
            }
            return Task.FromResult(string.Empty);
        }

        private async Task<bool> CheckWindowsHelloAvailabilityAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                // Check for Windows Hello support via registry or API
                // This is a simplified check - real implementation would use Windows.Security.Credentials
                return await Task.Run(() =>
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                            @"SOFTWARE\Policies\Microsoft\PassportForWork");
                        return key != null || Environment.OSVersion.Version.Major >= 10;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private bool CheckYubiKeyDetected()
        {
            // Simplified YubiKey detection - would need USB device enumeration
            // For now, just return false (user can configure later)
            return false;
        }

        private async Task DownloadAndInstallVeraCryptAsync()
        {
            if (IsInstalling)
                return;

            try
            {
                IsInstalling = true;
                CanCancelInstallation = true;
                IsProgressIndeterminate = false;
                InstallationProgress = 0;
                InstallationStatus = "Preparing download...";
                InstallationDetails = "Connecting to VeraCrypt servers...";

                _installationCts = new CancellationTokenSource();
                var token = _installationCts.Token;

                // Determine download URL based on architecture
                var downloadUrl = Environment.Is64BitOperatingSystem ? VeraCryptDownloadUrl64 : VeraCryptDownloadUrl32;
                var tempPath = Path.Combine(Path.GetTempPath(), "VeraCrypt_Setup.exe");

                // Download the installer
                InstallationStatus = "Downloading VeraCrypt...";
                InstallationDetails = $"Source: {downloadUrl}";

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? ExpectedDownloadSize;
                    var buffer = new byte[8192];
                    long bytesDownloaded = 0;

                    using var contentStream = await response.Content.ReadAsStreamAsync(token);
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                        bytesDownloaded += bytesRead;

                        var progressPercent = (int)((double)bytesDownloaded / totalBytes * 50);
                        InstallationProgress = progressPercent;
                        InstallationDetails = $"Downloaded {bytesDownloaded / 1_000_000:F1} MB of {totalBytes / 1_000_000:F1} MB";
                    }
                }

                token.ThrowIfCancellationRequested();

                // Verify downloaded file
                InstallationStatus = "Verifying download...";
                InstallationProgress = 55;
                InstallationDetails = "Checking file integrity...";

                var fileInfo = new FileInfo(tempPath);
                if (fileInfo.Length < 1_000_000) // Sanity check - should be at least 1 MB
                {
                    throw new InvalidOperationException("Downloaded file appears to be corrupted or incomplete.");
                }

                // Run the installer
                InstallationStatus = "Running VeraCrypt installer...";
                InstallationProgress = 60;
                InstallationDetails = "Please complete the VeraCrypt setup wizard. The installer will run with administrator privileges.";
                IsProgressIndeterminate = true;
                CanCancelInstallation = false;

                // Run installer with elevated privileges
                var psi = new ProcessStartInfo
                {
                    FileName = tempPath,
                    Arguments = "/S", // Silent install (VeraCrypt supports this)
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    InstallationDetails = "Waiting for VeraCrypt installation to complete...";
                    await process.WaitForExitAsync(token);

                    if (process.ExitCode == 0)
                    {
                        InstallationStatus = "Installation successful!";
                        InstallationProgress = 100;
                        InstallationDetails = "VeraCrypt has been installed successfully.";

                        // Refresh status
                        await Task.Delay(1000, token);
                        await RefreshStatusAsync();

                        await _dialogService.ShowInfoAsync(
                            "Installation Complete",
                            "VeraCrypt has been installed successfully. You can now create encrypted containers for your vault.",
                            _ownerWindow);
                    }
                    else
                    {
                        throw new InvalidOperationException($"VeraCrypt installer exited with code {process.ExitCode}. Installation may have been cancelled or failed.");
                    }
                }

                // Clean up temp file
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            catch (OperationCanceledException)
            {
                InstallationStatus = "Installation cancelled";
                InstallationDetails = "The installation was cancelled by the user.";
                InstallationProgress = 0;
            }
            catch (HttpRequestException ex)
            {
                InstallationStatus = "Download failed";
                InstallationDetails = $"Could not download VeraCrypt: {ex.Message}. Check your internet connection and try again.";
                InstallationProgress = 0;

                await _dialogService.ShowErrorAsync(
                    "Download Failed",
                    $"Could not download VeraCrypt from the official servers.\n\n{ex.Message}\n\nYou can try downloading manually from the VeraCrypt website.",
                    _ownerWindow);
            }
            catch (Exception ex)
            {
                InstallationStatus = "Installation failed";
                InstallationDetails = ex.Message;
                InstallationProgress = 0;

                await _dialogService.ShowErrorAsync(
                    "Installation Failed",
                    $"An error occurred during installation:\n\n{ex.Message}\n\nYou can try installing VeraCrypt manually from https://www.veracrypt.fr/",
                    _ownerWindow);
            }
            finally
            {
                IsInstalling = false;
                IsProgressIndeterminate = false;
                CanCancelInstallation = false;
                _installationCts?.Dispose();
                _installationCts = null;
            }
        }

        private async Task BrowseVeraCryptAsync()
        {
            try
            {
                if (_ownerWindow == null)
                    return;

                var storageProvider = _ownerWindow.StorageProvider;
                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select VeraCrypt Executable",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("VeraCrypt Executable")
                        {
                            Patterns = new[] { "VeraCrypt.exe", "veracrypt" }
                        },
                        new FilePickerFileType("All Executables")
                        {
                            Patterns = new[] { "*.exe", "*" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    var path = files[0].Path.LocalPath;
                    if (File.Exists(path))
                    {
                        // Verify it's actually VeraCrypt
                        var fileName = Path.GetFileName(path).ToLowerInvariant();
                        if (fileName.Contains("veracrypt"))
                        {
                            // Update settings to use this path
                            // (Would need to integrate with SettingsService)
                            VeraCryptPath = path;
                            IsVeraCryptInstalled = true;
                            InstalledVeraCryptVersion = await GetVeraCryptVersionAsync();

                            this.RaisePropertyChanged(nameof(VeraCryptStatusText));
                            this.RaisePropertyChanged(nameof(VeraCryptStatusIcon));
                            this.RaisePropertyChanged(nameof(VeraCryptStatusBackground));
                            UpdateStepProgress();

                            await _dialogService.ShowInfoAsync(
                                "VeraCrypt Found",
                                $"VeraCrypt located at:\n{path}",
                                _ownerWindow);
                        }
                        else
                        {
                            await _dialogService.ShowWarningAsync(
                                "Invalid Selection",
                                "The selected file does not appear to be VeraCrypt. Please select the VeraCrypt.exe file.",
                                _ownerWindow);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Installer] Error browsing for VeraCrypt: {ex.Message}");
            }
        }

        private void OpenVeraCryptWebsite()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VeraCryptWebsite,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Installer] Error opening website: {ex.Message}");
            }
        }

        private void CancelInstallation()
        {
            try
            {
                _installationCts?.Cancel();
            }
            catch
            {
                // Ignore cancellation errors
            }
        }

        private void SkipSetup()
        {
            NavigateToWelcome?.Invoke(this, EventArgs.Empty);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Continue()
        {
            NavigateToWelcome?.Invoke(this, EventArgs.Empty);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
