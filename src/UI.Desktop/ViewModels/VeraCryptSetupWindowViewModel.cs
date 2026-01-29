using System;
using System.Collections.Generic;
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
    /// <summary>
    /// ViewModel for VeraCrypt container creation wizard.
    /// </summary>
    public class VeraCryptSetupWindowViewModel : ReactiveObject, PhantomVault.UI.Services.IResettableOnError
    {
        private int _containerSizeMB = 100;
        private string _selectedEncryption = "AES";
        private string _selectedHash = "SHA-512";
        private string _selectedFilesystem = "FAT";
        private bool _isCreating;
        private string _statusMessage = string.Empty;
        private int _creationProgress;
        private string _usbPath = string.Empty;
        private string _containerPath = string.Empty;
        private readonly IVeraCryptService _veraCryptService;
        private readonly DialogService _dialogService;
        private Window? _ownerWindow;
        private string _containerPassword = string.Empty;
        private string _confirmContainerPassword = string.Empty;
        private string _passwordError = string.Empty;
        private string _confirmPasswordError = string.Empty;
        private bool _showContainerPassword;
        private bool _showConfirmContainerPassword;

        // VeraCrypt Settings properties
        private bool _isVeraCryptEnabled = true;
        private bool _autoMountEnabled = true;
        private bool _autoDismountEnabled = true;
        private string _veraCryptPath = @"C:\Program Files\VeraCrypt\VeraCrypt.exe";
        private bool _mountAsRemovable = false;
        private bool _mountReadOnly = false;
        private string _preferredDriveLetter = "Auto";
        private bool _cachePassword = false;
        private bool _usePIM = false;
        private bool _useKeyfiles = false;

        public event EventHandler<string>? NavigateToProvision;

        // Parameterless constructor for designer support
        public VeraCryptSetupWindowViewModel() : this(new VeraCryptService(), string.Empty)
        {
        }

        public VeraCryptSetupWindowViewModel(IVeraCryptService veraCryptService, string usbPath = "")
        {
            _veraCryptService = veraCryptService ?? throw new ArgumentNullException(nameof(veraCryptService));
            _usbPath = usbPath;
            _dialogService = new DialogService();

            // Set default container path on USB drive if provided
            if (!string.IsNullOrEmpty(usbPath) && Directory.Exists(usbPath))
            {
                _containerPath = Path.Combine(usbPath, "PhantomVault.vc");
            }

            CreateContainerCommand = ReactiveCommand.CreateFromTask(CreateContainerAsync);
            SkipCommand = ReactiveCommand.Create(OnSkip);
            BrowseLocationCommand = ReactiveCommand.CreateFromTask(BrowseLocationAsync);
            BrowseVeraCryptCommand = ReactiveCommand.CreateFromTask(BrowseVeraCryptAsync);
            SetSizeCommand = ReactiveCommand.Create<string>(OnSetSize);

            // Check if VeraCrypt is installed
            if (!_veraCryptService.IsVeraCryptInstalled())
            {
                StatusMessage = "VeraCrypt not detected. Please install VeraCrypt to create containers.";
            }
        }

        /// <summary>
        /// Reset transient state after an error dialog is dismissed so the user can retry VeraCrypt actions.
        /// Clears status messages and re-checks VeraCrypt availability.
        /// </summary>
        public async Task ResetAfterErrorAsync()
        {
            try
            {
                StatusMessage = string.Empty;
                await Task.Run(() =>
                {
                    try
                    {
                        // Re-evaluate VeraCrypt installation state
                        // Use the injected service instead of creating a new instance
                        // Force property update
                        this.RaisePropertyChanged(nameof(IsVeraCryptInstalled));
                    }
#pragma warning disable CA1031
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VeraCrypt] Failed to refresh state: {ex.Message}");
                    }
#pragma warning restore CA1031
                });
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VeraCrypt] RefreshVeraCryptStateAsync failed: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        public int ContainerSizeMB
        {
            get => _containerSizeMB;
            set => this.RaiseAndSetIfChanged(ref _containerSizeMB, value);
        }

        public string ContainerSizeGB => $"{ContainerSizeMB / 1024.0:F2} GB";

        public List<string> EncryptionAlgorithms => new()
        {
            "AES",
            "Serpent",
            "Twofish",
            "AES-Twofish",
            "AES-Twofish-Serpent",
            "Serpent-AES",
            "Serpent-Twofish-AES",
            "Twofish-Serpent"
        };

        public List<string> HashAlgorithms => new()
        {
            "SHA-512",
            "Whirlpool",
            "SHA-256",
            "Streebog"
        };

        public List<string> FilesystemTypes => new()
        {
            "FAT",
            "exFAT",
            "NTFS",
            "ext4",
            "None (format later)"
        };

        public string SelectedEncryption
        {
            get => _selectedEncryption;
            set => this.RaiseAndSetIfChanged(ref _selectedEncryption, value);
        }

        public string SelectedHash
        {
            get => _selectedHash;
            set => this.RaiseAndSetIfChanged(ref _selectedHash, value);
        }

        public string SelectedFilesystem
        {
            get => _selectedFilesystem;
            set => this.RaiseAndSetIfChanged(ref _selectedFilesystem, value);
        }

        public bool IsCreating
        {
            get => _isCreating;
            set => this.RaiseAndSetIfChanged(ref _isCreating, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public int CreationProgress
        {
            get => _creationProgress;
            set => this.RaiseAndSetIfChanged(ref _creationProgress, value);
        }

        public string ContainerPath
        {
            get => _containerPath;
            set => this.RaiseAndSetIfChanged(ref _containerPath, value);
        }

        public string ContainerPassword
        {
            get => _containerPassword;
            set => this.RaiseAndSetIfChanged(ref _containerPassword, value);
        }

        public string ConfirmContainerPassword
        {
            get => _confirmContainerPassword;
            set => this.RaiseAndSetIfChanged(ref _confirmContainerPassword, value);
        }

        public string PasswordError
        {
            get => _passwordError;
            set => this.RaiseAndSetIfChanged(ref _passwordError, value);
        }

        public string ConfirmPasswordError
        {
            get => _confirmPasswordError;
            set => this.RaiseAndSetIfChanged(ref _confirmPasswordError, value);
        }

        public bool ShowContainerPassword
        {
            get => _showContainerPassword;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showContainerPassword, value))
                {
                    this.RaisePropertyChanged(nameof(ContainerPasswordMaskChar));
                }
            }
        }

        public bool ShowConfirmContainerPassword
        {
            get => _showConfirmContainerPassword;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showConfirmContainerPassword, value))
                {
                    this.RaisePropertyChanged(nameof(ConfirmPasswordMaskChar));
                }
            }
        }

        public char ContainerPasswordMaskChar => ShowContainerPassword ? '\0' : '*';

        public char ConfirmPasswordMaskChar => ShowConfirmContainerPassword ? '\0' : '*';

        public bool IsVeraCryptInstalled => _veraCryptService.IsVeraCryptInstalled();

        public ReactiveCommand<Unit, Unit> CreateContainerCommand { get; }
        public ReactiveCommand<Unit, Unit> SkipCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseLocationCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseVeraCryptCommand { get; }
        public ReactiveCommand<string, Unit> SetSizeCommand { get; }

        // VeraCrypt Settings Properties
        public bool IsVeraCryptEnabled
        {
            get => _isVeraCryptEnabled;
            set => this.RaiseAndSetIfChanged(ref _isVeraCryptEnabled, value);
        }

        public bool AutoMountEnabled
        {
            get => _autoMountEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoMountEnabled, value);
        }

        public bool AutoDismountEnabled
        {
            get => _autoDismountEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoDismountEnabled, value);
        }

        public string VeraCryptPath
        {
            get => _veraCryptPath;
            set => this.RaiseAndSetIfChanged(ref _veraCryptPath, value);
        }

        public bool MountAsRemovable
        {
            get => _mountAsRemovable;
            set => this.RaiseAndSetIfChanged(ref _mountAsRemovable, value);
        }

        public bool MountReadOnly
        {
            get => _mountReadOnly;
            set => this.RaiseAndSetIfChanged(ref _mountReadOnly, value);
        }

        public string PreferredDriveLetter
        {
            get => _preferredDriveLetter;
            set => this.RaiseAndSetIfChanged(ref _preferredDriveLetter, value);
        }

        public bool CachePassword
        {
            get => _cachePassword;
            set => this.RaiseAndSetIfChanged(ref _cachePassword, value);
        }

        public bool UsePIM
        {
            get => _usePIM;
            set => this.RaiseAndSetIfChanged(ref _usePIM, value);
        }

        public bool UseKeyfiles
        {
            get => _useKeyfiles;
            set => this.RaiseAndSetIfChanged(ref _useKeyfiles, value);
        }

        private async Task CreateContainerAsync()
        {
            // Validate VeraCrypt installation
            if (!_veraCryptService.IsVeraCryptInstalled())
            {
                await _dialogService.ShowWarningAsync(
                    "VeraCrypt Not Found",
                    "VeraCrypt is not installed. Please install VeraCrypt from https://www.veracrypt.fr to create encrypted containers.",
                    _ownerWindow);
                StatusMessage = "VeraCrypt is not installed. Please install VeraCrypt from https://www.veracrypt.fr";
                return;
            }

            // Validate container path
            if (string.IsNullOrWhiteSpace(ContainerPath))
            {
                await _dialogService.ShowWarningAsync(
                    "Location Required",
                    "Please select a location where the container will be created.",
                    _ownerWindow);
                StatusMessage = "Please select a location for the container";
                return;
            }

            // Validate inputs
            if (ContainerSizeMB < 10)
            {
                await _dialogService.ShowWarningAsync(
                    "Invalid Size",
                    "Container size must be at least 10 MB.",
                    _ownerWindow);
                StatusMessage = "Container size must be at least 10 MB";
                return;
            }

            if (ContainerSizeMB > 1024 * 1024) // 1 TB
            {
                await _dialogService.ShowWarningAsync(
                    "Size Too Large",
                    "Container size cannot exceed 1 TB (1,048,576 MB).",
                    _ownerWindow);
                StatusMessage = "Container size cannot exceed 1 TB";
                return;
            }

            if (!TryValidatePassword(out var verifiedPassword))
            {
                if (_ownerWindow != null)
                {
                    var passwordIssues = new[] { PasswordError, ConfirmPasswordError }
                        .Where(static s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();

                    await _dialogService.ShowWarningAsync(
                        "Password Entry Needed",
                        passwordIssues.Length == 0
                            ? "Please review the highlighted password fields before continuing."
                            : string.Join("\n", passwordIssues),
                        _ownerWindow);
                }
                StatusMessage = "Container password validation failed.";
                return;
            }

            bool isPasswordless = string.IsNullOrEmpty(verifiedPassword);

            if (isPasswordless)
            {
                bool proceedWithoutPassword = true;
                if (_ownerWindow != null)
                {
                    proceedWithoutPassword = await _dialogService.ShowConfirmationAsync(
                        "Proceed Without Password?",
                        "No password will be required to mount this container. Anyone with the file will be able to open it. Are you sure you want to continue without a password?",
                        _ownerWindow);
                }

                if (!proceedWithoutPassword)
                {
                    StatusMessage = "Container creation cancelled.";
                    return;
                }
            }

            IsCreating = true;
            StatusMessage = isPasswordless
                ? "Proceeding without VeraCrypt password..."
                : "Initializing container creation...";
            CreationProgress = 0;

            try
            {
                // Create a progress reporter
                var progress = new Progress<int>(percent =>
                {
                    CreationProgress = percent;
                    StatusMessage = $"Creating VeraCrypt container... {percent}%";
                });

                // Call VeraCrypt service to create container
                var (success, message) = await _veraCryptService.CreateContainerAsync(
                    ContainerPath,
                    ContainerSizeMB,
                    verifiedPassword,
                    SelectedEncryption,
                    SelectedHash,
                    SelectedFilesystem,
                    progress);

                if (success)
                {
                    await _dialogService.ShowSuccessAsync(
                        "Container Created",
                        $"VeraCrypt container created successfully at:\n{Path.GetFileName(ContainerPath)}",
                        _ownerWindow);
                    string successMessage = string.IsNullOrWhiteSpace(message)
                        ? "Container created successfully!"
                        : message;

                    StatusMessage = isPasswordless
                        ? $"⚠ {successMessage} (container has no password)."
                        : $"✅ {successMessage}";
                    CreationProgress = 100;
                    await Task.Delay(1500);

                    // Navigate to provision window
                    NavigateToProvision?.Invoke(this, _usbPath);
                }
                else
                {
                    await _dialogService.ShowErrorAsync(
                        "Creation Failed",
                        $"Failed to create VeraCrypt container:\n\n{message}",
                        _ownerWindow);
                    string errorMessage = string.IsNullOrWhiteSpace(message)
                        ? "Failed to create VeraCrypt container."
                        : message;
                    StatusMessage = $"{errorMessage}";
                    CreationProgress = 0;
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Unexpected Error",
                    $"An error occurred while creating the container:\n\n{ex.Message}",
                    _ownerWindow);
                StatusMessage = $"Error creating container: {ex.Message}";
                CreationProgress = 0;
            }
            finally
            {
                IsCreating = false;
                ContainerPassword = string.Empty;
                ConfirmContainerPassword = string.Empty;
                ShowContainerPassword = false;
                ShowConfirmContainerPassword = false;
                PasswordError = string.Empty;
                ConfirmPasswordError = string.Empty;
            }
        }

        private async Task BrowseLocationAsync()
        {
            try
            {
                if (_ownerWindow == null)
                {
                    StatusMessage = "Window not initialized";
                    return;
                }

                var storageProvider = _ownerWindow.StorageProvider;
                if (storageProvider == null) return;

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Select Container Location",
                    DefaultExtension = "vc",
                    SuggestedFileName = "PhantomVault.vc",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("VeraCrypt Container")
                        {
                            Patterns = new[] { "*.vc", "*.hc" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (file != null)
                {
                    ContainerPath = file.Path.LocalPath;
                    StatusMessage = $"Container will be created at: {Path.GetFileName(file.Name)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting location: {ex.Message}";
            }
        }

        private void OnSkip()
        {
            StatusMessage = "Skipping VeraCrypt setup...";
            NavigateToProvision?.Invoke(this, _usbPath);
        }

        private void OnSetSize(string sizeMB)
        {
            if (int.TryParse(sizeMB, out int size))
            {
                ContainerSizeMB = size;
            }
        }

        private bool TryValidatePassword(out string password)
        {
            password = ContainerPassword ?? string.Empty;
            string confirm = ConfirmContainerPassword ?? string.Empty;
            PasswordError = string.Empty;
            ConfirmPasswordError = string.Empty;

            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(confirm))
            {
                password = string.Empty;
                return true;
            }

            if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(confirm))
            {
                PasswordError = "Password cannot be empty when confirmation is provided.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(confirm))
            {
                ConfirmPasswordError = "Please confirm the container password.";
                return false;
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ConfirmPasswordError = "Passwords do not match.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(password) && !IsStrongPassword(password))
            {
                PasswordError = "Password must be at least 12 characters and include upper, lower, number, and symbol.";
                return false;
            }

            return true;
        }

        private static bool IsStrongPassword(string password)
        {
            if (password.Length < 12)
            {
                return false;
            }

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;
            bool hasSymbol = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsWhiteSpace(c)) hasSymbol = true;

                if (hasUpper && hasLower && hasDigit && hasSymbol)
                {
                    return true;
                }
            }

            return hasUpper && hasLower && hasDigit && hasSymbol;
        }

        private async Task BrowseVeraCryptAsync()
        {
            try
            {
                if (_ownerWindow == null)
                {
                    StatusMessage = "Window not initialized";
                    return;
                }

                var storageProvider = _ownerWindow.StorageProvider;
                if (storageProvider == null) return;

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
                        new FilePickerFileType("Executable Files")
                        {
                            Patterns = new[] { "*.exe" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    VeraCryptPath = files[0].Path.LocalPath;
                    StatusMessage = $"VeraCrypt path set to: {Path.GetFileName(files[0].Name)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting VeraCrypt executable: {ex.Message}";
            }
        }
    }
}
