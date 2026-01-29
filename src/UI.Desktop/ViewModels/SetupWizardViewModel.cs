using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the first-run setup wizard that guides new users through
    /// initial configuration of PhantomVault.
    /// </summary>
    public partial class SetupWizardViewModel : ObservableObject
    {
        private readonly VeraCryptDetectionService _veraCryptDetector;
        private readonly VaultService _vaultService;

        [ObservableProperty]
        private int _currentStep = 1;

        [ObservableProperty]
        private int _totalSteps = 6;

        [ObservableProperty]
        private string _currentStepTitle = "Welcome";

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoNext = true;

        [ObservableProperty]
        private string _nextButtonText = "Next";

        // Step 1: Welcome
        [ObservableProperty]
        private bool _acceptedTerms;

        // Step 2: Security Level
        [ObservableProperty]
        private string _selectedSecurityLevel = "Balanced";

        [ObservableProperty]
        private string _securityDescription = "Recommended for most users. Provides strong security with convenient features.";

        // Step 3: Storage Location
        [ObservableProperty]
        private string _selectedStorageLocation = "USB";

        [ObservableProperty]
        private bool _usbDetected;

        [ObservableProperty]
        private string? _selectedUsbPath;

        [ObservableProperty]
        private ObservableCollection<string> _availableUsbDrives = new();

        // Step 4: VeraCrypt
        [ObservableProperty]
        private bool _veraCryptInstalled;

        [ObservableProperty]
        private string? _veraCryptPath;

        [ObservableProperty]
        private bool _downloadingVeraCrypt;

        [ObservableProperty]
        private double _veraCryptDownloadProgress;

        // Step 5: Master Password
        [ObservableProperty]
        private string _masterPassword = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private string _passwordStrength = "Weak";

        [ObservableProperty]
        private bool _passwordsMatch;

        [ObservableProperty]
        private bool _generateKeyfile;

        [ObservableProperty]
        private string? _keyfilePath;

        // Step 6: Summary
        [ObservableProperty]
        private string _setupSummary = string.Empty;

        [ObservableProperty]
        private bool _isCompleting;

        [ObservableProperty]
        private string? _statusMessage;

        public ObservableCollection<SecurityLevelOption> SecurityLevels { get; } = new()
        {
            new SecurityLevelOption
            {
                Name = "Basic",
                Description = "Simple password protection. Quick setup for personal use.",
                Features = new[] { "Password-only authentication", "Local storage", "Basic encryption (AES-256)" },
                RecommendedFor = "Personal use on trusted devices"
            },
            new SecurityLevelOption
            {
                Name = "Balanced",
                Description = "Recommended for most users. Strong security with convenient features.",
                Features = new[] { "Password + optional keyfile", "USB storage recommended", "Post-quantum encryption", "Auto-lock on idle" },
                RecommendedFor = "General users who want strong security"
            },
            new SecurityLevelOption
            {
                Name = "Maximum",
                Description = "Highest security. Required for sensitive data and compliance.",
                Features = new[] { "Multi-factor authentication required", "USB binding enforced", "VeraCrypt container", "Post-quantum encryption", "Aggressive auto-locking" },
                RecommendedFor = "Enterprise users, high-value data"
            }
        };

        public SetupWizardViewModel(VeraCryptDetectionService? veracryptDetectionService = null, VaultService? vaultService = null)
        {
            _veraCryptDetector = veracryptDetectionService ?? new VeraCryptDetectionService();
            // VaultService will need to be properly injected when this ViewModel is actually used
            // For now, we'll allow null and handle vault creation differently
            _vaultService = null!;

            _veraCryptDetector.DownloadProgressChanged += OnVeraCryptDownloadProgress;
            _veraCryptDetector.StatusMessageChanged += (s, msg) => StatusMessage = msg;
        }

        [RelayCommand]
        private async Task NextStepAsync()
        {
            if (!await ValidateCurrentStepAsync())
                return;

            if (CurrentStep < TotalSteps)
            {
                CurrentStep++;
                UpdateStepInfo();
                await LoadStepDataAsync();
            }
            else
            {
                await CompleteSetupAsync();
            }
        }

        [RelayCommand]
        private void PreviousStep()
        {
            if (CurrentStep > 1)
            {
                CurrentStep--;
                UpdateStepInfo();
            }
        }

        [RelayCommand]
        private async Task LoadStepDataAsync()
        {
            switch (CurrentStep)
            {
                case 3: // Storage Location
                    await DetectUsbDrivesAsync();
                    break;

                case 4: // VeraCrypt
                    await DetectVeraCryptAsync();
                    break;

                case 6: // Summary
                    GenerateSummary();
                    break;
            }
        }

        private async Task<bool> ValidateCurrentStepAsync()
        {
            switch (CurrentStep)
            {
                case 1: // Welcome
                    if (!AcceptedTerms)
                    {
                        StatusMessage = "Please accept the terms and conditions to continue.";
                        return false;
                    }
                    break;

                case 2: // Security Level
                    if (string.IsNullOrEmpty(SelectedSecurityLevel))
                    {
                        StatusMessage = "Please select a security level.";
                        return false;
                    }
                    break;

                case 3: // Storage Location
                    if (SelectedStorageLocation == "USB" && string.IsNullOrEmpty(SelectedUsbPath))
                    {
                        StatusMessage = "Please select a USB drive for your vault.";
                        return false;
                    }
                    break;

                case 4: // VeraCrypt
                    if (!VeraCryptInstalled && SelectedSecurityLevel == "Maximum")
                    {
                        StatusMessage = "VeraCrypt is required for Maximum security level. Please install it first.";
                        return false;
                    }
                    break;

                case 5: // Master Password
                    if (string.IsNullOrEmpty(MasterPassword))
                    {
                        StatusMessage = "Please enter a master password.";
                        return false;
                    }
                    if (MasterPassword != ConfirmPassword)
                    {
                        StatusMessage = "Passwords do not match.";
                        return false;
                    }
                    if (PasswordStrength == "Weak")
                    {
                        StatusMessage = "Password is too weak. Please use a stronger password.";
                        return false;
                    }
                    break;
            }

            return true;
        }

        private void UpdateStepInfo()
        {
            CurrentStepTitle = CurrentStep switch
            {
                1 => "Welcome to PhantomVault",
                2 => "Choose Security Level",
                3 => "Select Storage Location",
                4 => "VeraCrypt Configuration",
                5 => "Create Master Password",
                6 => "Review and Complete",
                _ => "Setup"
            };

            CanGoBack = CurrentStep > 1;
            CanGoNext = true;
            NextButtonText = CurrentStep == TotalSteps ? "Create Vault" : "Next";
        }

        private async Task DetectUsbDrivesAsync()
        {
            await Task.Run(() =>
            {
                AvailableUsbDrives.Clear();
                var drives = DriveInfo.GetDrives();
                
                foreach (var drive in drives)
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        var displayName = $"{drive.Name} - {drive.VolumeLabel} ({drive.TotalSize / 1024 / 1024 / 1024} GB)";
                        AvailableUsbDrives.Add(displayName);
                    }
                }

                UsbDetected = AvailableUsbDrives.Count > 0;
                if (UsbDetected)
                {
                    StatusMessage = $"Found {AvailableUsbDrives.Count} USB drive(s).";
                }
                else
                {
                    StatusMessage = "No USB drives detected. You can use local storage instead.";
                }
            });
        }

        private async Task DetectVeraCryptAsync()
        {
            await Task.Run(() =>
            {
                var result = _veraCryptDetector.DetectVeraCrypt();
                VeraCryptInstalled = result.IsInstalled;
                VeraCryptPath = result.InstallPath;
                StatusMessage = result.StatusMessage;

                if (!VeraCryptInstalled)
                {
                    StatusMessage = "VeraCrypt not found. Click 'Download VeraCrypt' to install it.";
                }
            });
        }

        [RelayCommand]
        private async Task DownloadVeraCryptAsync()
        {
            try
            {
                DownloadingVeraCrypt = true;
                StatusMessage = "Preparing to download VeraCrypt...";

                var tempPath = Path.Combine(Path.GetTempPath(), "veracrypt_installer.exe");
                var result = await _veraCryptDetector.DownloadVeraCryptAsync(tempPath);

                if (result.Success)
                {
                    StatusMessage = "VeraCrypt downloaded. Launching installer...";
                    var installResult = _veraCryptDetector.LaunchInstaller(result.FilePath!);
                    
                    if (installResult.Success)
                    {
                        StatusMessage = "Please complete the VeraCrypt installation, then click 'Detect Again'.";
                    }
                    else
                    {
                        StatusMessage = installResult.ErrorMessage ?? "Failed to launch installer.";
                    }
                }
                else
                {
                    StatusMessage = result.ErrorMessage ?? "Download failed.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error downloading VeraCrypt: {ex.Message}";
            }
            finally
            {
                DownloadingVeraCrypt = false;
            }
        }

        [RelayCommand]
        private void OpenVeraCryptDownloadPage()
        {
            _veraCryptDetector.OpenDownloadPage();
            StatusMessage = "Opened VeraCrypt download page in your browser.";
        }

        private void OnVeraCryptDownloadProgress(object? sender, DownloadProgressEventArgs e)
        {
            VeraCryptDownloadProgress = e.PercentageComplete;
        }

        [RelayCommand]
        private void GenerateStrongPassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=";
            var random = new Random();
            var password = new char[20];
            
            for (int i = 0; i < password.Length; i++)
            {
                password[i] = chars[random.Next(chars.Length)];
            }

            MasterPassword = new string(password);
            ConfirmPassword = MasterPassword;
            AnalyzePasswordStrength();
            StatusMessage = "Strong password generated. Please save it in a secure location.";
        }

        [RelayCommand]
        private void AnalyzePasswordStrength()
        {
            if (string.IsNullOrEmpty(MasterPassword))
            {
                PasswordStrength = "None";
                return;
            }

            int score = 0;
            
            // Length
            if (MasterPassword.Length >= 12) score++;
            if (MasterPassword.Length >= 16) score++;
            if (MasterPassword.Length >= 20) score++;

            // Complexity
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[A-Z]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[a-z]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[0-9]")) score++;
            if (System.Text.RegularExpressions.Regex.IsMatch(MasterPassword, "[^A-Za-z0-9]")) score++;

            PasswordStrength = score switch
            {
                <= 2 => "Weak",
                <= 4 => "Fair",
                <= 6 => "Good",
                _ => "Excellent"
            };

            PasswordsMatch = MasterPassword == ConfirmPassword;
        }

        private void GenerateSummary()
        {
            var summary = "Your PhantomVault will be created with the following settings:\n\n";
            summary += $"• Security Level: {SelectedSecurityLevel}\n";
            summary += $"• Storage Location: {(SelectedStorageLocation == "USB" ? $"USB Drive ({SelectedUsbPath})" : "Local Storage")}\n";
            summary += $"• VeraCrypt: {(VeraCryptInstalled ? "Enabled" : "Not installed")}\n";
            summary += $"• Master Password: Set\n";
            summary += $"• Keyfile: {(GenerateKeyfile ? "Will be generated" : "Not used")}\n";
            summary += $"\nEncryption: AES-256-GCM with {(SelectedSecurityLevel == "Maximum" ? "Post-Quantum" : "Standard")} key derivation\n";

            SetupSummary = summary;
        }

        private async Task CompleteSetupAsync()
        {
            try
            {
                IsCompleting = true;
                StatusMessage = "Creating your vault...";

                // Determine vault path
                string vaultPath;
                if (SelectedStorageLocation == "USB" && !string.IsNullOrEmpty(SelectedUsbPath))
                {
                    vaultPath = Path.Combine(SelectedUsbPath, "PhantomVault");
                }
                else
                {
                    vaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "PhantomVault");
                }

                // Generate keyfile if requested
                string? keyfilePath = null;
                if (GenerateKeyfile)
                {
                    keyfilePath = Path.Combine(vaultPath, "vault.key");
                    // Keyfile generation logic here
                }

                // Calculate vault size based on security level (in bytes)
                long vaultSizeBytes = SelectedSecurityLevel switch
                {
                    "Standard" => 100L * 1024 * 1024 * 1024, // 100 GB
                    "High" => 250L * 1024 * 1024 * 1024,     // 250 GB
                    "Maximum" => 500L * 1024 * 1024 * 1024,  // 500 GB
                    _ => 100L * 1024 * 1024 * 1024
                };

                // Create vault with proper parameters
                await _vaultService.CreateVaultAsync(
                    vaultPath, 
                    vaultSizeBytes,      // Size in bytes
                    MasterPassword,       // Passphrase
                    keyfilePath           // Keyfile path
                );

                StatusMessage = "Vault created successfully!";
                
                // TODO: Navigate to main vault view
                // NavigationService.NavigateToVault(vaultPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating vault: {ex.Message}";
            }
            finally
            {
                IsCompleting = false;
            }
        }

        partial void OnSelectedSecurityLevelChanged(string value)
        {
            var selected = SecurityLevels.FirstOrDefault(s => s.Name == value);
            if (selected != null)
            {
                SecurityDescription = selected.Description;
            }
        }

        partial void OnMasterPasswordChanged(string value)
        {
            AnalyzePasswordStrength();
        }

        partial void OnConfirmPasswordChanged(string value)
        {
            PasswordsMatch = MasterPassword == ConfirmPassword;
        }
    }

    public class SecurityLevelOption
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] Features { get; set; } = Array.Empty<string>();
        public string RecommendedFor { get; set; } = string.Empty;
    }
}
