using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using PhantomVault.Core.Services;
using System.Net.Http;
using System.Security.Cryptography;
using Serilog;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for vault settings window.
    /// </summary>
    public sealed class VaultSettingsViewModel : ReactiveObject
    {
        private readonly DialogService _dialogService;
        private readonly VaultViewModel? _vaultViewModel;
        private Window? _ownerWindow;

        private string _currentSectionTitle = "General Settings";
        private string _statusMessage = "Ready";
        private string _vaultName = "My Vault";

        // Section visibility
        private bool _isShowingGeneral = true;
        private bool _isShowingSecurity;
        private bool _isShowingAutoFill;
        private bool _isShowingImportExport;
        private bool _isShowingBackup;
        private bool _isShowingAccessibility;
        private bool _isShowingAdvanced;
        private bool _isShowingSecuredRubbishBin;
        private bool _isShowingAbout;

        // General settings
        private int _themeSelection;
        private int _clipboardClearTime = 1; // Default to 1 minute

        // Security settings
        private bool _autoLockEnabled = true;
        private int _autoLockTime = 1; // Default to 5 minutes
        private int _defaultPasswordLength = 16;
        private bool _includeUppercase = true;
        private bool _includeLowercase = true;
        private bool _includeNumbers = true;
        private bool _includeSymbols = true;

        // Decoy vault settings
        private bool _enableDecoyVault = true;
        private int _decoyCredentialCount = 20; // Number of fake credentials to generate
        private bool _decoyReadOnlyMode = true;
        private bool _decoyLogActivations = true;

        // Auto-fill settings
        private bool _autoFillEnabled = true;
        private bool _autoFillOnPageLoad;
        private bool _showAutoFillIcon = true;

        // Backup settings
        private bool _autoBackupEnabled;

        // Advanced settings
        private bool _enableDebugLogging;
        private bool _allowConcurrentSessions;
        private int _encryptionAlgorithmIndex = 0; // Default to AES-256
        private int _kdfAlgorithmIndex = 0; // Default to Argon2id
        private int _kdfIterations = 600000; // Default to 600k iterations

        // Browser extension and native host status
        private string _extensionStatusColor = "#DC3545"; // Red by default
        private string _extensionStatusText = "Not Installed";
        private string _nativeHostStatusColor = "#DC3545"; // Red by default
        private string _nativeHostStatusText = "Not Configured";

        // Credentials collection for import/export
        private List<Credential> _credentials = new();

        // Flaticon downloader
        private string _internetStatusText = "🔴 Internet Disconnected";
        private string _internetStatusColor = "#DC3545";

        // Passkey & hardware token status state
        private string _passkeyStatusText = "Windows Hello status not checked";
        private string _passkeyStatusDetail = "Open this vault on the current device to link Windows Hello.";
        private string _passkeyStatusAccent = "#6C757D";
        private string _passkeyStatusTimestamp = "Status not checked yet.";
        private string _passkeyActionHint = "Unlock the vault to enable Windows Hello setup.";
        private bool _canManagePasskeys;

        private string _yubiKeyStatusText = "YubiKey status not checked";
        private string _yubiKeyStatusDetail = "Insert a YubiKey and choose Configure to view device details.";
        private string _yubiKeyStatusAccent = "#6C757D";
        private string _yubiKeyStatusTimestamp = "Status not checked yet.";
        private string _yubiKeyActionHint = "Insert a YubiKey before starting the hardware check.";

        // TOTP Authenticator status
        private string _totpStatusText = "TOTP not configured";
        private string _totpStatusDetail = "Set up time-based one-time password (TOTP) for additional security.";
        private string _totpStatusAccent = "#6C757D";
        private string _totpStatusTimestamp = "Never configured.";
        private string _totpActionHint = "Generate QR code or enter secret key to enable TOTP authentication.";
        private bool _hasTotpEnabled = false;

        // Busy indicator state
        private bool _isBusy;
        private string _busyMessage = "Working...";
        private string _busyDetail = string.Empty;
        private bool _isBusyIndeterminate = true;
        private double _busyProgress;

        private bool _isCategoryManagerPanelVisible;
        private CategoryManagerViewModel? _categoryManagerViewModel;

        // UI layout / theme toggles (bound from XAML)
        private bool _useDarkTheme = true;
        private bool _useGridLayout = true;
        private bool _isDashboardEnabled = true;

        // Privacy & diagnostics
        private bool _privacyModeEnabled;
        private bool _redactDiagnosticLogs = true;

        // Secure trash settings
        private bool _secureTrashEnabled = true;
        private bool _secureTrashAutoPurge = true;
        private int _secureTrashRetentionDays = 30;
        private int _secureTrashWipePasses = 3;

        /// <summary>
        /// Provides access to theme settings for runtime theme switching.
        /// </summary>
        public Settings.ThemeSettingsViewModel ThemeSettings { get; }

        public VaultSettingsViewModel(VaultViewModel? vaultViewModel = null)
        {
            _dialogService = new DialogService();
            _vaultViewModel = vaultViewModel;
            
            // Initialize theme settings with runtime theme service
            var app = Application.Current as App;
            var themeManager = app?.Services?.GetService(typeof(ThemeManagerService)) as ThemeManagerService;
            var runtimeThemeService = app?.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;
            ThemeSettings = new Settings.ThemeSettingsViewModel(themeManager, runtimeThemeService);
            
            // Load persisted settings
            var settings = SettingsService.Load();

            // Initialize privacy and secure trash settings from persisted settings
            try
            {
                _privacyModeEnabled = settings.PrivacyModeEnabled;
                _redactDiagnosticLogs = settings.RedactDiagnosticLogs;
                PrivacyShield.PrivacyModeEnabled = _privacyModeEnabled;
                PrivacyShield.RedactDiagnostics = _redactDiagnosticLogs;

                _secureTrashEnabled = settings.SecureTrashEnabled;
                _secureTrashAutoPurge = settings.SecureTrashAutoPurge;
                _secureTrashRetentionDays = settings.SecureTrashRetentionDays;
                _secureTrashWipePasses = settings.SecureTrashWipePasses;

                _clipboardClearTime = settings.ClipboardClearTime;

                _isDashboardEnabled = settings.DashboardEnabled;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load user settings during VaultSettingsViewModel initialization, using defaults");
            }

            // Navigation commands
            ShowGeneralCommand = ReactiveCommand.Create(ShowGeneral);
            ShowSecurityCommand = ReactiveCommand.Create(ShowSecurity);
            ShowAutoFillCommand = ReactiveCommand.Create(ShowAutoFill);
            ShowImportExportCommand = ReactiveCommand.Create(ShowImportExport);
            ShowBackupCommand = ReactiveCommand.Create(ShowBackup);
            ShowAccessibilityCommand = ReactiveCommand.Create(ShowAccessibility);
            ShowAdvancedCommand = ReactiveCommand.Create(ShowAdvanced);
            ShowSecuredRubbishBinCommand = ReactiveCommand.Create(ShowSecuredRubbishBin);
            ShowAboutCommand = ReactiveCommand.Create(ShowAbout);

            // Common quick actions bound from settings XAML
            OpenAddEntryCommand = ReactiveCommand.CreateFromTask(OpenAddEntryAsync);
            OpenPasswordGeneratorCommand = ReactiveCommand.CreateFromTask(OpenPasswordGeneratorAsync);
            OpenCategoryManagerCommand = ReactiveCommand.CreateFromTask(ManageCategoriesAsync);

            // Action commands
            ChangeMasterPasswordCommand = ReactiveCommand.CreateFromTask(ChangeMasterPasswordAsync);
            ConfigureTwoFactorCommand = ReactiveCommand.CreateFromTask(ConfigureTwoFactorAsync);
            ConfigurePasskeyCommand = ReactiveCommand.CreateFromTask(ConfigurePasskeyAsync);
            ConfigureYubiKeyCommand = ReactiveCommand.CreateFromTask(ConfigureYubiKeyAsync);
            ConfigureTotpCommand = ReactiveCommand.CreateFromTask(ConfigureTotpAsync);
            ViewTotpCodesCommand = ReactiveCommand.CreateFromTask(ViewTotpCodesAsync);
            ManageCategoriesCommand = ReactiveCommand.CreateFromTask(ManageCategoriesAsync);
            InstallChromeExtensionCommand = ReactiveCommand.CreateFromTask(InstallChromeExtensionAsync);
            InstallFirefoxExtensionCommand = ReactiveCommand.CreateFromTask(InstallFirefoxExtensionAsync);
            ConfigureNativeHostCommand = ReactiveCommand.CreateFromTask(ConfigureNativeHostAsync);
            CreateBackupCommand = ReactiveCommand.CreateFromTask(CreateBackupAsync);
            RestoreBackupCommand = ReactiveCommand.CreateFromTask(RestoreBackupAsync);
            ExportVaultCommand = ReactiveCommand.CreateFromTask(ExportVaultAsync);
            ExportVaultCsvCommand = ReactiveCommand.CreateFromTask(ExportVaultCsvAsync);
            ExportKeePassCommand = ReactiveCommand.CreateFromTask(ExportKeePassAsync);
            OpenImportWindowCommand = ReactiveCommand.CreateFromTask(OpenImportWindowAsync);
            ImportVaultCommand = ReactiveCommand.CreateFromTask(ImportVaultAsync);
            ImportVaultCsvCommand = ReactiveCommand.CreateFromTask(ImportVaultCsvAsync);
            ImportKeePassCommand = ReactiveCommand.CreateFromTask(ImportKeePassAsync);
            ClearVaultDataCommand = ReactiveCommand.CreateFromTask(ClearVaultDataAsync);
            ResetToDefaultsCommand = ReactiveCommand.CreateFromTask(ResetToDefaultsAsync);
            CloseCommand = ReactiveCommand.Create(Close);
            OpenFlatIconDownloaderCommand = ReactiveCommand.CreateFromTask(OpenFlatIconDownloaderAsync);
            OpenIconManagerCommand = ReactiveCommand.CreateFromTask(OpenIconManagerAsync);
            ApplyThemeCommand = ReactiveCommand.Create(ApplyTheme);

            SetPasskeyStatus(
                "Windows Hello not linked",
                "Register a passkey to unlock without typing your master password.",
                "#6C757D",
                "Unlock the vault to enable Windows Hello setup.",
                updateTimestamp: false);

            SetYubiKeyStatus(
                "YubiKey status not checked",
                "Insert a YubiKey and choose Configure to view device details.",
                "#6C757D",
                "Supports FIDO2 and OTP-capable keys.",
                updateTimestamp: false);

            RefreshPasskeyStatus();

        }

        // Command to open the local Icon Manager window
        public ReactiveCommand<Unit, Unit> OpenIconManagerCommand { get; }

        private async Task OpenIconManagerAsync()
        {
            try
            {
                await IconLibraryLauncher.ShowAsync(_ownerWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Icon Library", $"Failed to open Icon Manager: {ex.Message}", _ownerWindow);
            }
        }

        // Properties
        public string CurrentSectionTitle
        {
            get => _currentSectionTitle;
            set => this.RaiseAndSetIfChanged(ref _currentSectionTitle, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string VaultName
        {
            get => _vaultName;
            set => this.RaiseAndSetIfChanged(ref _vaultName, value);
        }

        // Section visibility
        public bool IsShowingGeneral
        {
            get => _isShowingGeneral;
            set => this.RaiseAndSetIfChanged(ref _isShowingGeneral, value);
        }

        public bool IsShowingSecurity
        {
            get => _isShowingSecurity;
            set => this.RaiseAndSetIfChanged(ref _isShowingSecurity, value);
        }

        public bool IsShowingAutoFill
        {
            get => _isShowingAutoFill;
            set => this.RaiseAndSetIfChanged(ref _isShowingAutoFill, value);
        }

        public bool IsShowingImportExport
        {
            get => _isShowingImportExport;
            set => this.RaiseAndSetIfChanged(ref _isShowingImportExport, value);
        }

        public bool IsShowingBackup
        {
            get => _isShowingBackup;
            set => this.RaiseAndSetIfChanged(ref _isShowingBackup, value);
        }

        public bool IsShowingAccessibility
        {
            get => _isShowingAccessibility;
            set => this.RaiseAndSetIfChanged(ref _isShowingAccessibility, value);
        }

        public bool IsShowingAdvanced
        {
            get => _isShowingAdvanced;
            set => this.RaiseAndSetIfChanged(ref _isShowingAdvanced, value);
        }

        public bool IsShowingSecuredRubbishBin
        {
            get => _isShowingSecuredRubbishBin;
            set => this.RaiseAndSetIfChanged(ref _isShowingSecuredRubbishBin, value);
        }

        public bool IsShowingAbout
        {
            get => _isShowingAbout;
            set => this.RaiseAndSetIfChanged(ref _isShowingAbout, value);
        }

        // General settings properties
        public int ThemeSelection
        {
            get => _themeSelection;
            set => this.RaiseAndSetIfChanged(ref _themeSelection, value);
        }

        public ReactiveCommand<Unit, Unit> ApplyThemeCommand { get; }

        private void ApplyTheme()
        {
            try
            {
                if (Avalonia.Application.Current is App app)
                {
                    if (ThemeSelection == 0) app.SetTheme("dark");
                    else if (ThemeSelection == 1) app.SetTheme("light");
                    else app.SetTheme("dark"); // System default fallback
                    StatusMessage = "Theme applied";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to apply theme";
                System.Diagnostics.Debug.WriteLine($"ApplyTheme error: {ex.Message}");
            }
        }

        public int ClipboardClearTime
        {
            get => _clipboardClearTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _clipboardClearTime, value);
                try
                {
                    var s = SettingsService.Load();
                    s.ClipboardClearTime = value;
                    SettingsService.Save(s);
                }
                catch { /* Ignore persistence failures */ }
            }
        }

        // Security settings properties
        public bool AutoLockEnabled
        {
            get => _autoLockEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoLockEnabled, value);
        }

        public int AutoLockTime
        {
            get => _autoLockTime;
            set => this.RaiseAndSetIfChanged(ref _autoLockTime, value);
        }

        public int DefaultPasswordLength
        {
            get => _defaultPasswordLength;
            set => this.RaiseAndSetIfChanged(ref _defaultPasswordLength, value);
        }

        public bool IncludeUppercase
        {
            get => _includeUppercase;
            set => this.RaiseAndSetIfChanged(ref _includeUppercase, value);
        }

        public bool IncludeLowercase
        {
            get => _includeLowercase;
            set => this.RaiseAndSetIfChanged(ref _includeLowercase, value);
        }

        public bool IncludeNumbers
        {
            get => _includeNumbers;
            set => this.RaiseAndSetIfChanged(ref _includeNumbers, value);
        }

        public bool IncludeSymbols
        {
            get => _includeSymbols;
            set => this.RaiseAndSetIfChanged(ref _includeSymbols, value);
        }

        // Decoy vault settings properties
        public bool EnableDecoyVault
        {
            get => _enableDecoyVault;
            set => this.RaiseAndSetIfChanged(ref _enableDecoyVault, value);
        }

        public int DecoyCredentialCount
        {
            get => _decoyCredentialCount;
            set => this.RaiseAndSetIfChanged(ref _decoyCredentialCount, value);
        }

        public bool DecoyReadOnlyMode
        {
            get => _decoyReadOnlyMode;
            set => this.RaiseAndSetIfChanged(ref _decoyReadOnlyMode, value);
        }

        public bool DecoyLogActivations
        {
            get => _decoyLogActivations;
            set => this.RaiseAndSetIfChanged(ref _decoyLogActivations, value);
        }

        // Auto-fill settings properties
        public bool AutoFillEnabled
        {
            get => _autoFillEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoFillEnabled, value);
        }

        public bool AutoFillOnPageLoad
        {
            get => _autoFillOnPageLoad;
            set => this.RaiseAndSetIfChanged(ref _autoFillOnPageLoad, value);
        }

        public bool ShowAutoFillIcon
        {
            get => _showAutoFillIcon;
            set => this.RaiseAndSetIfChanged(ref _showAutoFillIcon, value);
        }

        // Backup settings properties
        public bool AutoBackupEnabled
        {
            get => _autoBackupEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoBackupEnabled, value);
        }

        // Advanced settings properties
        public bool EnableDebugLogging
        {
            get => _enableDebugLogging;
            set => this.RaiseAndSetIfChanged(ref _enableDebugLogging, value);
        }

        public bool AllowConcurrentSessions
        {
            get => _allowConcurrentSessions;
            set => this.RaiseAndSetIfChanged(ref _allowConcurrentSessions, value);
        }

        public int EncryptionAlgorithmIndex
        {
            get => _encryptionAlgorithmIndex;
            set => this.RaiseAndSetIfChanged(ref _encryptionAlgorithmIndex, value);
        }

        public int KdfAlgorithmIndex
        {
            get => _kdfAlgorithmIndex;
            set => this.RaiseAndSetIfChanged(ref _kdfAlgorithmIndex, value);
        }

        public int KdfIterations
        {
            get => _kdfIterations;
            set => this.RaiseAndSetIfChanged(ref _kdfIterations, value);
        }

        public string ExtensionStatusColor
        {
            get => _extensionStatusColor;
            set => this.RaiseAndSetIfChanged(ref _extensionStatusColor, value);
        }

        public string ExtensionStatusText
        {
            get => _extensionStatusText;
            set => this.RaiseAndSetIfChanged(ref _extensionStatusText, value);
        }

        public string NativeHostStatusColor
        {
            get => _nativeHostStatusColor;
            set => this.RaiseAndSetIfChanged(ref _nativeHostStatusColor, value);
        }

        public string NativeHostStatusText
        {
            get => _nativeHostStatusText;
            set => this.RaiseAndSetIfChanged(ref _nativeHostStatusText, value);
        }

        // Flaticon downloader properties
        public string InternetStatusText
        {
            get => _internetStatusText;
            set => this.RaiseAndSetIfChanged(ref _internetStatusText, value);
        }

        public string InternetStatusColor
        {
            get => _internetStatusColor;
            set => this.RaiseAndSetIfChanged(ref _internetStatusColor, value);
        }

        public string PasskeyStatusText
        {
            get => _passkeyStatusText;
            private set => this.RaiseAndSetIfChanged(ref _passkeyStatusText, value);
        }

        public string PasskeyStatusDetail
        {
            get => _passkeyStatusDetail;
            private set => this.RaiseAndSetIfChanged(ref _passkeyStatusDetail, value);
        }

        public string PasskeyStatusAccent
        {
            get => _passkeyStatusAccent;
            private set => this.RaiseAndSetIfChanged(ref _passkeyStatusAccent, value);
        }

        public string PasskeyStatusTimestamp
        {
            get => _passkeyStatusTimestamp;
            private set => this.RaiseAndSetIfChanged(ref _passkeyStatusTimestamp, value);
        }

        public string PasskeyActionHint
        {
            get => _passkeyActionHint;
            private set => this.RaiseAndSetIfChanged(ref _passkeyActionHint, value);
        }

        public bool CanManagePasskeys
        {
            get => _canManagePasskeys;
            private set => this.RaiseAndSetIfChanged(ref _canManagePasskeys, value);
        }

        public string YubiKeyStatusText
        {
            get => _yubiKeyStatusText;
            private set => this.RaiseAndSetIfChanged(ref _yubiKeyStatusText, value);
        }

        public string YubiKeyStatusDetail
        {
            get => _yubiKeyStatusDetail;
            private set => this.RaiseAndSetIfChanged(ref _yubiKeyStatusDetail, value);
        }

        public string YubiKeyStatusAccent
        {
            get => _yubiKeyStatusAccent;
            private set => this.RaiseAndSetIfChanged(ref _yubiKeyStatusAccent, value);
        }

        public string YubiKeyStatusTimestamp
        {
            get => _yubiKeyStatusTimestamp;
            private set => this.RaiseAndSetIfChanged(ref _yubiKeyStatusTimestamp, value);
        }

        public string YubiKeyActionHint
        {
            get => _yubiKeyActionHint;
            private set => this.RaiseAndSetIfChanged(ref _yubiKeyActionHint, value);
        }

        public string TotpStatusText
        {
            get => _totpStatusText;
            private set => this.RaiseAndSetIfChanged(ref _totpStatusText, value);
        }

        public string TotpStatusDetail
        {
            get => _totpStatusDetail;
            private set => this.RaiseAndSetIfChanged(ref _totpStatusDetail, value);
        }

        public string TotpStatusAccent
        {
            get => _totpStatusAccent;
            private set => this.RaiseAndSetIfChanged(ref _totpStatusAccent, value);
        }

        public string TotpStatusTimestamp
        {
            get => _totpStatusTimestamp;
            private set => this.RaiseAndSetIfChanged(ref _totpStatusTimestamp, value);
        }

        public string TotpActionHint
        {
            get => _totpActionHint;
            private set => this.RaiseAndSetIfChanged(ref _totpActionHint, value);
        }

        public bool HasTotpEnabled
        {
            get => _hasTotpEnabled;
            private set => this.RaiseAndSetIfChanged(ref _hasTotpEnabled, value);
        }

        public bool HasVaultContext => _vaultViewModel != null;

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string BusyMessage
        {
            get => _busyMessage;
            private set => this.RaiseAndSetIfChanged(ref _busyMessage, value);
        }

        public string BusyDetail
        {
            get => _busyDetail;
            private set => this.RaiseAndSetIfChanged(ref _busyDetail, value);
        }

        public bool IsBusyIndeterminate
        {
            get => _isBusyIndeterminate;
            private set => this.RaiseAndSetIfChanged(ref _isBusyIndeterminate, value);
        }

        public double BusyProgress
        {
            get => _busyProgress;
            private set => this.RaiseAndSetIfChanged(ref _busyProgress, value);
        }

        public bool IsCategoryManagerPanelVisible
        {
            get => _isCategoryManagerPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isCategoryManagerPanelVisible, value);
        }

        public CategoryManagerViewModel? CategoryManagerViewModel
        {
            get => _categoryManagerViewModel;
            private set => this.RaiseAndSetIfChanged(ref _categoryManagerViewModel, value);
        }

        // Commands
        public ReactiveCommand<Unit, Unit> ShowGeneralCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSecurityCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAutoFillCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowImportExportCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowBackupCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAccessibilityCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdvancedCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSecuredRubbishBinCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
        public ReactiveCommand<Unit, Unit> ChangeMasterPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfigureTwoFactorCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfigurePasskeyCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfigureYubiKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfigureTotpCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewTotpCodesCommand { get; }
        public ReactiveCommand<Unit, Unit> ManageCategoriesCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallChromeExtensionCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallFirefoxExtensionCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfigureNativeHostCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateBackupCommand { get; }
        public ReactiveCommand<Unit, Unit> RestoreBackupCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCsvCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportKeePassCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenImportWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportVaultCsvCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportKeePassCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearVaultDataCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenFlatIconDownloaderCommand { get; }

        // Quick action commands used by settings UI
        public ReactiveCommand<Unit, Unit> OpenAddEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenPasswordGeneratorCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenCategoryManagerCommand { get; }

        // Theme/layout toggles (bound from XAML)
        public bool UseDarkTheme
        {
            get => _useDarkTheme;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _useDarkTheme, value))
                {
                    try
                    {
                        if (Avalonia.Application.Current is App app)
                        {
                            app.SetTheme(value ? "dark" : "light");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to set theme to {Theme}", value ? "dark" : "light");
                    }
                }
            }
        }

        public bool UseGridLayout
        {
            get => _useGridLayout;
            set => this.RaiseAndSetIfChanged(ref _useGridLayout, value);
        }

        /// <summary>
        /// When true the Dashboard view is available; when false the app skips
        /// straight to the Passwords view and hides the sidebar Dashboard button.
        /// </summary>
        public bool IsDashboardEnabled
        {
            get => _isDashboardEnabled;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isDashboardEnabled, value))
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.DashboardEnabled = value;
                        SettingsService.Save(s);

                        // Propagate to VaultViewModel so it takes effect immediately
                        if (_vaultViewModel != null)
                        {
                            _vaultViewModel.IsDashboardEnabled = value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to persist DashboardEnabled setting");
                    }
                }
            }
        }

        // Privacy & diagnostics (persisted)
        public bool PrivacyModeEnabled
        {
            get => _privacyModeEnabled;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _privacyModeEnabled, value))
                {
                    try
                    {
                        PrivacyShield.PrivacyModeEnabled = value;
                        var s = SettingsService.Load();
                        s.PrivacyModeEnabled = value;
                        SettingsService.Save(s);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to save PrivacyModeEnabled setting");
                    }
                }
            }
        }

        public bool RedactDiagnosticLogs
        {
            get => _redactDiagnosticLogs;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _redactDiagnosticLogs, value))
                {
                    try
                    {
                        PrivacyShield.RedactDiagnostics = value;
                        var s = SettingsService.Load();
                        s.RedactDiagnosticLogs = value;
                        SettingsService.Save(s);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to save RedactDiagnosticLogs setting");
                    }
                }
            }
        }

        // Secure trash settings
        public bool SecureTrashEnabled
        {
            get => _secureTrashEnabled;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _secureTrashEnabled, value))
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.SecureTrashEnabled = value;
                        SettingsService.Save(s);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to save SecureTrashEnabled setting");
                    }
                }
            }
        }

        public bool SecureTrashAutoPurge
        {
            get => _secureTrashAutoPurge;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _secureTrashAutoPurge, value))
                {
                    try
                    {
                        var s = SettingsService.Load();
                        s.SecureTrashAutoPurge = value;
                        SettingsService.Save(s);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to save SecureTrashAutoPurge setting");
                    }
                }
            }
        }

        public int SecureTrashRetentionDays
        {
            get => _secureTrashRetentionDays;
            set
            {
                this.RaiseAndSetIfChanged(ref _secureTrashRetentionDays, value);
                try
                {
                    var s = SettingsService.Load();
                    s.SecureTrashRetentionDays = value;
                    SettingsService.Save(s);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save SecureTrashRetentionDays setting");
                }
            }
        }

        public int SecureTrashWipePasses
        {
            get => _secureTrashWipePasses;
            set
            {
                this.RaiseAndSetIfChanged(ref _secureTrashWipePasses, value);
                try
                {
                    var s = SettingsService.Load();
                    s.SecureTrashWipePasses = value;
                    SettingsService.Save(s);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save SecureTrashWipePasses setting");
                }
            }
        }

        // Methods
        private void ShowGeneral()
        {
            HideAllSections();
            IsShowingGeneral = true;
            CurrentSectionTitle = "General Settings";
        }

        private void ShowSecurity()
        {
            HideAllSections();
            IsShowingSecurity = true;
            CurrentSectionTitle = "Security Settings";
        }

        private void ShowAutoFill()
        {
            HideAllSections();
            IsShowingAutoFill = true;
            CurrentSectionTitle = "Auto-Fill Settings";
        }

        private void ShowImportExport()
        {
            HideAllSections();
            IsShowingImportExport = true;
            CurrentSectionTitle = "Import / Export";
        }

        private void ShowBackup()
        {
            HideAllSections();
            IsShowingBackup = true;
            CurrentSectionTitle = "Backup & Restore";
        }

        private void ShowAccessibility()
        {
            HideAllSections();
            IsShowingAccessibility = true;
            CurrentSectionTitle = "Accessibility";
        }

        private void ShowAdvanced()
        {
            HideAllSections();
            IsShowingAdvanced = true;
            CurrentSectionTitle = "Advanced Settings";
        }

        private void ShowSecuredRubbishBin()
        {
            HideAllSections();
            IsShowingSecuredRubbishBin = true;
            CurrentSectionTitle = "Secured Rubbish Bin";
        }

        private void ShowAbout()
        {
            HideAllSections();
            IsShowingAbout = true;
            CurrentSectionTitle = "About PhantomVault";
        }

        private async System.Threading.Tasks.Task OpenAddEntryAsync()
        {
            try
            {
                if (_vaultViewModel != null)
                {
                    await _vaultViewModel.ShowAddCredentialDialogAsync();
                    return;
                }

                // Fallback: create a new AddEditCredential window
                var viewModel = new AddEditCredentialViewModel(null, null);
                var window = new AddEditCredentialWindow
                {
                    DataContext = viewModel
                };
                viewModel.SetOwnerWindow(window);

                // Determine an owner window if available; otherwise show non-modally
                Avalonia.Controls.Window? owner = _ownerWindow;
                if (owner == null && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    owner = desktop.MainWindow;
                }

                if (owner != null)
                {
                    await window.ShowDialog(owner);
                }
                else
                {
                    // Non-modal show returns void
                    window.Show();
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Open Add Entry", ex.Message, _ownerWindow);
            }
        }

        private async System.Threading.Tasks.Task OpenPasswordGeneratorAsync()
        {
            try
            {
                var viewModel = new PasswordGeneratorViewModel();
                var window = new PasswordGeneratorWindow
                {
                    DataContext = viewModel
                };
                viewModel.SetOwnerWindow(window);

                // Determine an owner window if available; otherwise show non-modally
                Avalonia.Controls.Window? owner = _ownerWindow;
                if (owner == null && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    owner = desktop.MainWindow;
                }

                if (owner != null)
                {
                    await window.ShowDialog(owner);
                }
                else
                {
                    // Non-modal show returns void
                    window.Show();
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Password Generator", ex.Message, _ownerWindow);
            }
        }

        private void HideAllSections()
        {
            IsShowingGeneral = false;
            IsShowingSecurity = false;
            IsShowingAutoFill = false;
            IsShowingImportExport = false;
            IsShowingBackup = false;
            IsShowingAccessibility = false;
            IsShowingAdvanced = false;
            IsShowingSecuredRubbishBin = false;
            IsShowingAbout = false;
        }

        private async System.Threading.Tasks.Task ChangeMasterPasswordAsync()
        {
            await _dialogService.ShowInfoAsync(
                "Change Master Password",
                "Master password change dialog would open here.\n\nThis is a critical security operation that requires your current password and will re-encrypt your entire vault.",
                _ownerWindow);
            StatusMessage = "Master password change initiated";
        }

        private async System.Threading.Tasks.Task ConfigureTwoFactorAsync()
        {
            await _dialogService.ShowInfoAsync(
                "Two-Factor Authentication (Coming Soon)",
                "Full two-factor enrollment is under active development and will ship in an upcoming build.\n\nPlanned options include:\n• YubiKey hardware tokens\n• Windows Hello / Touch ID\n• TOTP authenticator apps\n\nStay tuned—this preview keeps the menu item visible so you know what is on the roadmap.",
                _ownerWindow);
            StatusMessage = "Two-factor authentication coming soon.";
        }

        private void SetPasskeyStatus(string statusText, string detail, string accentHex, string actionHint, bool updateTimestamp = true)
        {
            PasskeyStatusText = statusText;
            PasskeyStatusDetail = detail;
            PasskeyStatusAccent = accentHex;
            PasskeyActionHint = actionHint;

            if (updateTimestamp)
            {
                PasskeyStatusTimestamp = $"Updated {DateTime.Now:t}";
            }
        }

        private void SetYubiKeyStatus(string statusText, string detail, string accentHex, string actionHint, bool updateTimestamp = true)
        {
            YubiKeyStatusText = statusText;
            YubiKeyStatusDetail = detail;
            YubiKeyStatusAccent = accentHex;
            YubiKeyActionHint = actionHint;

            if (updateTimestamp)
            {
                YubiKeyStatusTimestamp = $"Updated {DateTime.Now:t}";
            }
        }

        private void SetTotpStatus(string statusText, string detail, string accentHex, string actionHint, bool updateTimestamp = true)
        {
            TotpStatusText = statusText;
            TotpStatusDetail = detail;
            TotpStatusAccent = accentHex;
            TotpActionHint = actionHint;

            if (updateTimestamp)
            {
                TotpStatusTimestamp = $"Updated {DateTime.Now:t}";
            }
        }

        private void UpdateBusyStatus(string message, string? detail = null, double? progress = null, bool? indeterminate = null)
        {
            BusyMessage = message;

            if (detail != null)
            {
                BusyDetail = detail;
            }

            if (indeterminate.HasValue)
            {
                IsBusyIndeterminate = indeterminate.Value;
                if (indeterminate.Value)
                {
                    BusyProgress = 0;
                }
            }

            if (progress.HasValue)
            {
                BusyProgress = progress.Value;
            }
        }

        private async Task RunBusyOperationAsync(string message, Func<Task> operation, bool indeterminate = true, string? detail = null)
        {
            UpdateBusyStatus(message, detail, indeterminate ? null : 0d, indeterminate);
            IsBusy = true;

            try
            {
                await operation().ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                BusyProgress = 0;
                BusyDetail = string.Empty;
                BusyMessage = "Ready";
                IsBusyIndeterminate = true;
            }
        }

        private void RefreshPasskeyStatus()
        {
            if (_vaultViewModel == null)
            {
                CanManagePasskeys = false;
                SetPasskeyStatus(
                    "Vault not unlocked",
                    "Open settings from an unlocked vault to manage Windows Hello credentials.",
                    "#6C757D",
                    "Unlock the vault and reopen settings to link Windows Hello.",
                    updateTimestamp: false);
                return;
            }

            var services = (Avalonia.Application.Current as App)?.Services;
            if (services == null)
            {
                CanManagePasskeys = false;
                SetPasskeyStatus(
                    "Service unavailable",
                    "Application services were not initialised, so the passkey status cannot be determined right now.",
                    "#DC3545",
                    "Restart PhantomVault and try again.");
                return;
            }

            if (!_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                CanManagePasskeys = false;
                SetPasskeyStatus(
                    "Unlock vault to continue",
                    "We need the manifest credentials cached from an unlocked vault session before we can manage Windows Hello.",
                    "#6C757D",
                    "Unlock the vault and reopen settings to enable passkey management.",
                    updateTimestamp: false);
                return;
            }

            var manifestService = services.GetService(typeof(ManifestService)) as ManifestService;
            if (manifestService == null)
            {
                CanManagePasskeys = false;
                SetPasskeyStatus(
                    "Manifest service unavailable",
                    "Required services for reading the vault manifest were not resolved.",
                    "#DC3545",
                    "Repair or reinstall PhantomVault, then try linking Windows Hello again.");
                return;
            }

            try
            {
                var manifest = manifestService.ReadManifest(
                    manifestPath,
                    string.IsNullOrEmpty(passphrase) ? null : passphrase,
                    string.IsNullOrEmpty(keyfilePath) ? null : keyfilePath);

                CanManagePasskeys = true;

                if (!string.IsNullOrWhiteSpace(manifest.PasskeyId))
                {
                    SetPasskeyStatus(
                        "Windows Hello linked",
                        "Unlocking this vault already requires Windows Hello verification.",
                        "#28A745",
                        "Use Re-link to rotate the credential or recover from device replacement.");
                }
                else
                {
                    SetPasskeyStatus(
                        "Passkey not configured",
                        "Register Windows Hello to unlock the vault without typing the master password.",
                        "#FFA500",
                        "Linking takes about 15 seconds and keeps your passphrase as a fallback.");
                }
            }
            catch
            {
                CanManagePasskeys = false;
                SetPasskeyStatus(
                    "Manifest unavailable",
                    "We could not read the vault manifest to confirm the passkey status.",
                    "#DC3545",
                    "Ensure the vault remains unlocked and retry the operation.");
            }
        }

        private async System.Threading.Tasks.Task ConfigurePasskeyAsync()
        {
            await RunBusyOperationAsync(
                "Preparing Windows Hello…",
                async () =>
                {
                    var services = (Avalonia.Application.Current as App)?.Services;
                    var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                                        ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService;

                    if (passkeyService == null)
                    {
                        SetPasskeyStatus(
                            "Service unavailable",
                            "The Windows Hello integration could not be resolved on this device.",
                            "#DC3545",
                            "Repair PhantomVault or reinstall the Windows Hello components, then try again.");

                        CanManagePasskeys = false;

                        await _dialogService.ShowErrorAsync(
                            "Passkey Service Unavailable",
                            "The Windows Hello integration could not be resolved. Reinstall or repair PhantomVault, then try again.",
                            _ownerWindow);
                        StatusMessage = "Passkey service unavailable.";
                        return;
                    }

                    UpdateBusyStatus("Checking Windows Hello support…", "Making sure Windows Hello is available on this device.");

                    SetPasskeyStatus(
                        "Checking Windows Hello…",
                        "Verifying Windows Hello availability before linking your vault.",
                        "#0D6EFD",
                        "A Windows Hello prompt will appear to confirm the registration.");

                    if (!passkeyService.IsSupported)
                    {
                        SetPasskeyStatus(
                            "Windows Hello not available",
                            $"Status reported by Windows: {passkeyService.AuthenticatorDescription}.",
                            "#FFA500",
                            "Configure Windows Hello in Windows Settings, then retry the passkey setup.");

                        await _dialogService.ShowWarningAsync(
                            "Windows Hello Not Ready",
                            $"This device does not currently expose a compatible authenticator.\n\nReported status: {passkeyService.AuthenticatorDescription}",
                            _ownerWindow);
                        StatusMessage = "Passkeys unsupported on this device.";
                        return;
                    }

                    if (_vaultViewModel == null)
                    {
                        SetPasskeyStatus(
                            "Vault context required",
                            "Open the settings window from an unlocked vault to manage Windows Hello credentials.",
                            "#6C757D",
                            "Unlock the vault and reopen settings to enable passkey management.",
                            updateTimestamp: false);

                        CanManagePasskeys = false;

                        await _dialogService.ShowWarningAsync(
                            "Vault Context Required",
                            "Open the settings window from an unlocked vault to manage passkeys.",
                            _ownerWindow);
                        StatusMessage = "Passkey configuration requires active vault.";
                        return;
                    }

                    if (!_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
                    {
                        SetPasskeyStatus(
                            "Unlock vault to continue",
                            "We need the manifest credentials cached from an unlocked vault session before we can manage Windows Hello.",
                            "#6C757D",
                            "Unlock the vault and reopen settings to enable passkey management.",
                            updateTimestamp: false);

                        CanManagePasskeys = false;

                        await _dialogService.ShowWarningAsync(
                            "Manifest Not Found",
                            "Unable to locate the manifest for the current vault. Reopen the vault from its USB drive and try again.",
                            _ownerWindow);
                        StatusMessage = "Manifest not located.";
                        return;
                    }

                    if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(keyfilePath))
                    {
                        await _dialogService.ShowWarningAsync(
                            "Missing Manifest Secrets",
                            "The session does not have the manifest passphrase or keyfile path. Unlock the vault with its passphrase/keyfile and retry.",
                            _ownerWindow);
                        StatusMessage = "Manifest credentials required.";
                        return;
                    }

                    var manifestService = services?.GetService(typeof(ManifestService)) as ManifestService;
                    if (manifestService == null)
                    {
                        SetPasskeyStatus(
                            "Manifest service unavailable",
                            "Required services for reading the vault manifest were not resolved.",
                            "#DC3545",
                            "Repair or reinstall PhantomVault, then try linking Windows Hello again.");

                        CanManagePasskeys = false;

                        await _dialogService.ShowErrorAsync(
                            "Manifest Service Unavailable",
                            "Cannot update the vault manifest because required services were not initialised.",
                            _ownerWindow);
                        StatusMessage = "Manifest service unavailable.";
                        return;
                    }

                    UpdateBusyStatus("Loading manifest…", "Decrypting the vault manifest to check the current credential state.", progress: 25, indeterminate: false);

                    VaultManifest manifest;
                    try
                    {
                        manifest = manifestService.ReadManifest(manifestPath, string.IsNullOrEmpty(passphrase) ? null : passphrase, string.IsNullOrEmpty(keyfilePath) ? null : keyfilePath);
                    }
                    catch (Exception ex)
                    {
                        SetPasskeyStatus(
                            "Unable to read manifest",
                            "We could not decrypt the manifest to determine passkey status.",
                            "#DC3545",
                            "Unlock the vault again and ensure the passphrase or keyfile are available.");

                        CanManagePasskeys = false;

                        await _dialogService.ShowErrorAsync(
                            "Manifest Unlock Failed",
                            $"Could not decrypt the manifest: {ex.Message}",
                            _ownerWindow);
                        StatusMessage = "Unable to read manifest.";
                        return;
                    }

                    if (manifest.PasskeyId is string existingPasskeyId && !string.IsNullOrWhiteSpace(existingPasskeyId))
                    {
                        var fingerprint = existingPasskeyId.Length > 12
                            ? existingPasskeyId[..12] + "…"
                            : existingPasskeyId;

                        var replace = await _dialogService.ShowConfirmationAsync(
                            "Replace Existing Passkey?",
                            $"A Windows Hello credential is already bound to this vault (fingerprint: {fingerprint}).\n\nDo you want to replace it with a new credential now?",
                            _ownerWindow);

                        if (!replace)
                        {
                            SetPasskeyStatus(
                                "Windows Hello linked",
                                "The existing Windows Hello credential remains active for this vault.",
                                "#28A745",
                                "Use Re-link if you need to rotate or revoke the credential.");

                            await _dialogService.ShowInfoAsync(
                                "Passkey Unchanged",
                                "The existing Windows Hello credential remains active. Vault unlocks will continue to require Windows Hello verification.",
                                _ownerWindow);
                            StatusMessage = "Passkey unchanged.";
                            return;
                        }
                    }

                    var challenge = new byte[32];
                    RandomNumberGenerator.Fill(challenge);

                    byte[] credentialId;
                    try
                    {
                        UpdateBusyStatus("Waiting for Windows Hello…", "Confirm the Windows Hello prompt to finish linking.");

                        SetPasskeyStatus(
                            "Waiting for Windows Hello…",
                            "Complete the Windows Hello prompt to bind this vault to your device.",
                            "#0D6EFD",
                            "Use your PIN or biometric sensor when Windows requests verification.");

                        credentialId = await passkeyService.RegisterAsync(
                            manifest.DeviceId ?? Guid.NewGuid().ToString("N"),
                            manifest.VaultName ?? _vaultViewModel.VaultName,
                            "phantomvault.app",
                            challenge);
                    }
                    catch (PlatformNotSupportedException ex)
                    {
                        SetPasskeyStatus(
                            "Windows Hello unavailable",
                            ex.Message,
                            "#DC3545",
                            "Configure Windows Hello and try again.");

                        await _dialogService.ShowErrorAsync("Windows Hello Unavailable", ex.Message, _ownerWindow);
                        StatusMessage = "Windows Hello unavailable.";
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        SetPasskeyStatus(
                            "Passkey registration failed",
                            ex.Message,
                            "#DC3545",
                            "Retry or reset Windows Hello before attempting again.");

                        await _dialogService.ShowErrorAsync("Passkey Registration Failed", ex.Message, _ownerWindow);
                        StatusMessage = "Passkey registration failed.";
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetPasskeyStatus(
                            "Passkey registration failed",
                            "An unexpected error occurred while invoking Windows Hello.",
                            "#DC3545",
                            "Check Windows Event Viewer for additional details and try again.");

                        await _dialogService.ShowErrorAsync("Passkey Registration Failed", $"Unexpected error: {ex.Message}", _ownerWindow);
                        StatusMessage = "Passkey registration failed.";
                        return;
                    }
                    finally
                    {
                        Array.Clear(challenge, 0, challenge.Length);
                    }

                    manifest.PasskeyId = Convert.ToBase64String(credentialId);
                    Array.Clear(credentialId, 0, credentialId.Length);

                    try
                    {
                        UpdateBusyStatus("Saving vault manifest…", "Encrypting and writing the updated manifest.", progress: 75, indeterminate: false);
                        manifestService.WriteManifest(manifest, manifestPath, string.IsNullOrEmpty(passphrase) ? null : passphrase, string.IsNullOrEmpty(keyfilePath) ? null : keyfilePath);
                    }
                    catch (Exception ex)
                    {
                        SetPasskeyStatus(
                            "Manifest update failed",
                            "Windows Hello was registered but the manifest could not be updated.",
                            "#DC3545",
                            "Retry the operation; the previous passkey remains unchanged until the manifest is updated.");

                        await _dialogService.ShowErrorAsync(
                            "Manifest Update Failed",
                            $"Windows Hello registered but the manifest could not be updated: {ex.Message}",
                            _ownerWindow);
                        StatusMessage = "Manifest update failed.";
                        return;
                    }

                    UpdateBusyStatus("Finishing up…", "Wrapping up the Windows Hello configuration.", progress: 100, indeterminate: false);

                    SetPasskeyStatus(
                        "Windows Hello linked",
                        "Unlocking this vault now requires Windows Hello verification.",
                        "#28A745",
                        "Keep your passphrase or keyfile as a recovery option.");

                    var builder = new StringBuilder();
                    builder.AppendLine("Windows Hello is now bound to this vault.");
                    builder.AppendLine();
                    builder.AppendLine("Unlock attempts will prompt for Windows Hello verification.");
                    builder.AppendLine("Keep your passphrase/keyfile available as a recovery method in case Windows Hello becomes unavailable.");

                    await _dialogService.ShowSuccessAsync("Passkey Registered", builder.ToString(), _ownerWindow);
                    StatusMessage = "Passkey registered.";
                    RefreshPasskeyStatus();
                },
                detail: "Follow the Windows Hello prompts to finish linking this device.");
        }

        private async System.Threading.Tasks.Task ConfigureYubiKeyAsync()
        {
            await RunBusyOperationAsync(
                "Scanning for YubiKey…",
                async () =>
                {
                    YubiKeyService yubiKeyService;

                    try
                    {
                        yubiKeyService = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(YubiKeyService)) as YubiKeyService
                                        ?? new YubiKeyService();
                    }
                    catch (Exception ex)
                    {
                        SetYubiKeyStatus(
                            "YubiKey service unavailable",
                            $"Unable to initialise the YubiKey integration: {ex.Message}",
                            "#DC3545",
                            "Install or repair YubiKey Manager, then try again.");

                        await _dialogService.ShowErrorAsync(
                            "YubiKey Service Error",
                            $"Unable to initialise the YubiKey integration: {ex.Message}",
                            _ownerWindow);
                        StatusMessage = "YubiKey service unavailable.";
                        return;
                    }

                    UpdateBusyStatus("Checking hardware token…", "Detecting connected YubiKey devices.");

                    try
                    {
                        SetYubiKeyStatus(
                            "Checking for YubiKeys…",
                            "Looking for connected hardware tokens.",
                            "#0D6EFD",
                            "Insert your YubiKey and touch the sensor if prompted.");

                        bool detected = yubiKeyService.IsTokenPresent();
                        string? deviceInfo = yubiKeyService.GetDeviceInfo();

                        if (detected)
                        {
                            var detail = string.IsNullOrWhiteSpace(deviceInfo)
                                ? "A YubiKey is connected and ready."
                                : $"Connected device: {deviceInfo}";

                            SetYubiKeyStatus(
                                "YubiKey detected",
                                detail,
                                "#28A745",
                                "Touch the YubiKey when PhantomVault requests confirmation.");
                        }
                        else
                        {
                            SetYubiKeyStatus(
                                "No YubiKey detected",
                                "Insert a YubiKey and try again. USB-A, USB-C, NFC, and Lightning models are supported.",
                                "#FFA500",
                                "If the key is already inserted, tap the gold contact to wake it.");
                        }

                        var builder = new StringBuilder();
                        builder.AppendLine(detected ? "YubiKey detected." : "No YubiKey detected.");

                        if (!string.IsNullOrWhiteSpace(deviceInfo))
                        {
                            builder.AppendLine();
                            builder.AppendLine($"Device info: {deviceInfo}");
                        }

                        builder.AppendLine();
                        builder.AppendLine("Tips:");
                        builder.AppendLine("- Insert the YubiKey and touch the sensor when prompted.");
                        builder.AppendLine("- Keep YubiKey Manager installed to update firmware and configure slots.");
                        builder.AppendLine("- Provision vaults with \"Requires hardware token\" enabled to enforce YubiKey presence during unlock.");

                        await _dialogService.ShowInfoAsync("YubiKey Status", builder.ToString(), _ownerWindow);
                        StatusMessage = detected ? "YubiKey detected." : "No YubiKey detected.";
                    }
                    catch (InvalidOperationException ex)
                    {
                        SetYubiKeyStatus(
                            "YubiKey detection failed",
                            ex.Message,
                            "#DC3545",
                            "Reconnect the device or install YubiKey Manager, then try again.");

                        await _dialogService.ShowErrorAsync("YubiKey Error", ex.Message, _ownerWindow);
                        StatusMessage = "YubiKey detection failed.";
                    }
                },
                detail: "Make sure your YubiKey is inserted before continuing.");
        }

        private async System.Threading.Tasks.Task ConfigureTotpAsync()
        {
            if (_ownerWindow == null)
            {
                return;
            }

            await RunBusyOperationAsync(
                "Opening TOTP Setup…",
                async () =>
                {
                    try
                    {
                        UpdateBusyStatus("Loading TOTP configuration…", "Preparing authenticator setup.");

                        // Get the vault name for the TOTP label
                        string vaultName = "PhantomVault";
                        if (_vaultViewModel != null && _vaultViewModel.TryGetManifestContext(out var manifestPath, out _, out _))
                        {
                            vaultName = System.IO.Path.GetFileNameWithoutExtension(manifestPath) ?? "PhantomVault";
                        }

                        // Create the TOTP settings view model
                        var totpService = new TotpService();
                        var totpViewModel = new TotpSettingsViewModel(totpService)
                        {
                            VaultName = vaultName
                        };

                        // If we have an existing TOTP secret in the manifest, load it
                        if (_vaultViewModel != null && _vaultViewModel.TryGetManifestContext(out manifestPath, out var passphrase, out var keyfilePath))
                        {
                            try
                            {
                                var services = (Avalonia.Application.Current as App)?.Services;
                                var manifestService = services?.GetService(typeof(ManifestService)) as ManifestService;
                                
                                if (manifestService != null && File.Exists(manifestPath))
                                {
                                    var manifest = manifestService.ReadManifest(
                                        manifestPath,
                                        string.IsNullOrWhiteSpace(passphrase) ? null : passphrase,
                                        string.IsNullOrWhiteSpace(keyfilePath) ? null : keyfilePath);
                                    
                                    if (!string.IsNullOrEmpty(manifest?.TotpSecret))
                                    {
                                        // Load existing TOTP configuration
                                        totpViewModel.IsTotpEnabled = manifest.RequiresTotp;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore manifest read errors - just show fresh setup
                            }
                        }

                        // Create and show the TOTP settings window
                        var totpWindow = new PhantomVault.UI.Views.TotpSettingsWindow
                        {
                            DataContext = totpViewModel
                        };
                        
                        totpViewModel.SetOwnerWindow(totpWindow);
                        
                        await totpWindow.ShowDialog(_ownerWindow);

                        // After dialog closes, if TOTP was configured, update the manifest
                        if (totpViewModel.IsTotpEnabled && !string.IsNullOrEmpty(totpViewModel.TotpSecret))
                        {
                            await SaveTotpToManifestAsync(totpViewModel.TotpSecret).ConfigureAwait(false);
                            HasTotpEnabled = true;
                            SetTotpStatus(
                                "TOTP enabled",
                                "Two-factor authentication is active for this vault.",
                                "#28A745",
                                "Use your authenticator app to generate codes for unlock.");
                            StatusMessage = "TOTP configuration saved.";
                        }
                        else if (!totpViewModel.IsTotpEnabled && string.IsNullOrEmpty(totpViewModel.TotpSecret))
                        {
                            // TOTP was removed
                            HasTotpEnabled = false;
                            StatusMessage = "TOTP setup cancelled.";
                        }
                    }
                    catch (Exception ex)
                    {
                        SetTotpStatus(
                            "TOTP setup failed",
                            $"Unable to configure TOTP: {ex.Message}",
                            "#DC3545",
                            "Try again or contact support if the issue persists.");

                        await _dialogService.ShowErrorAsync(
                            "TOTP Configuration Error",
                            $"Unable to configure TOTP: {ex.Message}",
                            _ownerWindow);
                        StatusMessage = "TOTP setup failed.";
                    }
                },
                detail: "Prepare your authenticator app to scan the QR code.");
        }

        /// <summary>
        /// Saves the TOTP secret to the vault manifest.
        /// </summary>
        private async System.Threading.Tasks.Task SaveTotpToManifestAsync(string totpSecret)
        {
            if (_vaultViewModel == null || !_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                throw new InvalidOperationException("Cannot save TOTP: vault context not available.");
            }

            var services = (Avalonia.Application.Current as App)?.Services;
            var manifestService = services?.GetService(typeof(ManifestService)) as ManifestService;
            
            if (manifestService == null || !File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Cannot save TOTP: manifest service unavailable.");
            }

            var manifest = manifestService.ReadManifest(
                manifestPath,
                string.IsNullOrWhiteSpace(passphrase) ? null : passphrase,
                string.IsNullOrWhiteSpace(keyfilePath) ? null : keyfilePath);
            
            if (manifest == null)
            {
                throw new InvalidOperationException("Cannot save TOTP: failed to read manifest.");
            }

            manifest.TotpSecret = totpSecret;
            manifest.RequiresTotp = true;
            
            manifestService.WriteManifest(
                manifest,
                manifestPath,
                string.IsNullOrWhiteSpace(passphrase) ? null : passphrase,
                string.IsNullOrWhiteSpace(keyfilePath) ? null : keyfilePath);

            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task ViewTotpCodesAsync()
        {
            await RunBusyOperationAsync(
                "Loading TOTP codes…",
                async () =>
                {
                    try
                    {
                        // Get manifest context
                        if (_vaultViewModel == null)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Vault Not Available",
                                "Vault view model is not initialized.",
                                _ownerWindow);
                            StatusMessage = "Vault not available.";
                            return;
                        }

                        if (!_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
                        {
                            await _dialogService.ShowErrorAsync(
                                "Manifest Not Found",
                                "Unable to locate vault manifest for TOTP retrieval.",
                                _ownerWindow);
                            StatusMessage = "Manifest not located.";
                            return;
                        }

                        var services = (Application.Current as App)?.Services;
                        if (services == null)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Service Unavailable",
                                "Application services are not available.",
                                _ownerWindow);
                            StatusMessage = "Services not available.";
                            return;
                        }

                        var manifestService = services.GetService(typeof(ManifestService)) as ManifestService;
                        var totpService = new TotpService();
                        var recoveryCodeService = new RecoveryCodeService();

                        if (manifestService == null)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Service Unavailable",
                                "Manifest service is not available.",
                                _ownerWindow);
                            StatusMessage = "Manifest service unavailable.";
                            return;
                        }

                        // Read manifest
                        VaultManifest manifest;
                        try
                        {
                            manifest = manifestService.ReadManifest(manifestPath,
                                string.IsNullOrEmpty(passphrase) ? null : passphrase,
                                string.IsNullOrEmpty(keyfilePath) ? null : keyfilePath);
                        }
                        catch (Exception ex)
                        {
                            await _dialogService.ShowErrorAsync(
                                "Manifest Read Failed",
                                $"Unable to read vault manifest: {ex.Message}",
                                _ownerWindow);
                            StatusMessage = "Failed to read manifest.";
                            return;
                        }

                        // Check if TOTP is configured
                        if (string.IsNullOrWhiteSpace(manifest.TotpSecret))
                        {
                            await _dialogService.ShowInfoAsync(
                                "TOTP Not Configured",
                                "You haven't set up TOTP yet. Click 'Setup TOTP' to get started.",
                                _ownerWindow);
                            StatusMessage = "TOTP not configured.";
                            return;
                        }

                        // Generate current TOTP code
                        var now = DateTimeOffset.UtcNow;
                        var totpCode = totpService.GenerateCode(manifest.TotpSecret);
                        var secondsRemaining = 30 - (now.Second % 30);

                        // Build message
                        var messageBuilder = new StringBuilder();
                        messageBuilder.AppendLine("Your TOTP Code:");
                        messageBuilder.AppendLine();
                        messageBuilder.AppendLine($"  {totpCode.Substring(0, 3)} {totpCode.Substring(3)}");
                        messageBuilder.AppendLine();
                        messageBuilder.AppendLine($"Time remaining: {secondsRemaining} seconds");
                        messageBuilder.AppendLine();

                        // Show recovery codes if available
                        if (manifest.RecoveryCodes != null && manifest.RecoveryCodes.Length > 0)
                        {
                            var remainingCodes = recoveryCodeService.GetRemainingCodeCount(manifest);
                            messageBuilder.AppendLine($"Recovery Codes: {remainingCodes} unused");
                            messageBuilder.AppendLine();
                            messageBuilder.AppendLine("Note: Recovery codes are single-use backup codes");
                            messageBuilder.AppendLine("for account recovery. Save them securely!");
                        }

                        await _dialogService.ShowInfoAsync("TOTP Codes", messageBuilder.ToString(), _ownerWindow);
                        StatusMessage = "TOTP codes displayed.";
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error viewing TOTP codes");
                        await _dialogService.ShowErrorAsync(
                            "TOTP Error",
                            $"Unable to retrieve TOTP codes: {ex.Message}",
                            _ownerWindow);
                        StatusMessage = "Failed to retrieve TOTP codes.";
                    }
                },
                detail: "Retrieving your time-based codes.");
        }

        private async System.Threading.Tasks.Task ManageCategoriesAsync()
        {
            if (_ownerWindow == null)
            {
                return;
            }

            try
            {
                var services = (Avalonia.Application.Current as App)?.Services;
                var manifestService = services?.GetService(typeof(ManifestService)) as ManifestService;

                CategoryManagerViewModel newViewModel;

                if (_vaultViewModel != null)
                {
                    if (manifestService != null && _vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath) && File.Exists(manifestPath))
                    {
                        try
                        {
                            var manifest = manifestService.ReadManifest(
                                manifestPath,
                                string.IsNullOrWhiteSpace(passphrase) ? null : passphrase,
                                string.IsNullOrWhiteSpace(keyfilePath) ? null : keyfilePath);

                            newViewModel = new CategoryManagerViewModel(manifestService, manifest, manifestPath, _vaultViewModel, passphrase, keyfilePath);
                        }
                        catch (Exception ex)
                        {
                            await _dialogService.ShowWarningAsync(
                                "Manage Categories",
                                $"Unable to load manifest categories ({ex.Message}). Showing the in-memory list instead.",
                                _ownerWindow);

                            newViewModel = new CategoryManagerViewModel(_vaultViewModel);
                        }
                    }
                    else
                    {
                        newViewModel = new CategoryManagerViewModel(_vaultViewModel);
                    }
                }
                else
                {
                    newViewModel = new CategoryManagerViewModel();
                }

                AttachCategoryManagerViewModel(newViewModel);

                IsCategoryManagerPanelVisible = true;
                StatusMessage = "Category manager open";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Error", $"Failed to open category manager: {ex.Message}", _ownerWindow);
                StatusMessage = "Failed to open category manager";
            }
        }

        private void AttachCategoryManagerViewModel(CategoryManagerViewModel viewModel)
        {
            if (CategoryManagerViewModel != null)
            {
                CategoryManagerViewModel.DismissRequested -= OnCategoryManagerDismissed;
            }

            if (_ownerWindow != null)
            {
                viewModel.SetOwnerWindow(_ownerWindow);
            }

            viewModel.CloseOwnerOnDismiss = false;
            viewModel.DismissRequested += OnCategoryManagerDismissed;
            CategoryManagerViewModel = viewModel;
        }

        private void OnCategoryManagerDismissed()
        {
            DismissCategoryManagerPanel();
        }

        public void DismissCategoryManagerPanel()
        {
            if (CategoryManagerViewModel != null)
            {
                CategoryManagerViewModel.DismissRequested -= OnCategoryManagerDismissed;
                CategoryManagerViewModel = null;
            }

            IsCategoryManagerPanelVisible = false;
            StatusMessage = "Category manager closed";
        }

        private async System.Threading.Tasks.Task InstallChromeExtensionAsync()
        {
            try
            {
                // Check if extension folder exists
                var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserExtension", "Chrome");

                if (!Directory.Exists(extensionPath))
                {
                    await _dialogService.ShowWarningAsync(
                        "Extension Not Found",
                        "Chrome extension files not found in the installation directory.\n\nPlease ensure the BrowserExtension folder is present.",
                        _ownerWindow);
                    return;
                }

                // Open Chrome extensions page
                await _dialogService.ShowInfoAsync(
                    "Install Chrome Extension",
                    $"🌐 Chrome Extension Installation\n\n1. Enable 'Developer mode' in Chrome (toggle in top-right)\n2. Click 'Load unpacked'\n3. Navigate to:\n   {extensionPath}\n4. Select the folder and click 'Select Folder'\n\nThe extension will appear in your browser toolbar.\n\nNote: You can also drag the folder directly onto the Extensions page.",
                    _ownerWindow);

                // Try to open the extensions folder
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = extensionPath,
                    UseShellExecute = true
                });

                ExtensionStatusText = "Ready to Install";
                ExtensionStatusColor = "#FFA500"; // Orange
                StatusMessage = "Chrome extension installation guide shown";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Error", $"Failed to open extension folder: {ex.Message}", _ownerWindow);
                StatusMessage = "Failed to show extension installation";
            }
        }

        private async System.Threading.Tasks.Task InstallFirefoxExtensionAsync()
        {
            try
            {
                // Check if extension folder exists
                var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserExtension", "Firefox");

                if (!Directory.Exists(extensionPath))
                {
                    await _dialogService.ShowWarningAsync(
                        "Extension Not Found",
                        "Firefox extension files not found in the installation directory.\n\nPlease ensure the BrowserExtension folder is present.",
                        _ownerWindow);
                    return;
                }

                await _dialogService.ShowInfoAsync(
                    "Install Firefox Extension",
                    $"🦊 Firefox Extension Installation\n\n1. Open Firefox and navigate to:\n   about:debugging#/runtime/this-firefox\n\n2. Click 'Load Temporary Add-on'\n\n3. Navigate to:\n   {extensionPath}\n\n4. Select the 'manifest.json' file\n\nThe extension will appear in your browser toolbar.\n\nNote: Temporary extensions are removed when Firefox restarts. For permanent installation, the extension needs to be signed by Mozilla.",
                    _ownerWindow);

                // Try to open the extensions folder
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = extensionPath,
                    UseShellExecute = true
                });

                ExtensionStatusText = "Ready to Install";
                ExtensionStatusColor = "#FFA500"; // Orange
                StatusMessage = "Firefox extension installation guide shown";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Error", $"Failed to open extension folder: {ex.Message}", _ownerWindow);
                StatusMessage = "Failed to show extension installation";
            }
        }

        private async System.Threading.Tasks.Task ConfigureNativeHostAsync()
        {
            try
            {
                var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserExtension", "Install-NativeHost.ps1");

                if (!File.Exists(scriptPath))
                {
                    await _dialogService.ShowWarningAsync(
                        "Script Not Found",
                        "Native messaging host installation script not found.\n\nPlease ensure the BrowserExtension folder contains Install-NativeHost.ps1",
                        _ownerWindow);
                    return;
                }

                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "Configure Native Messaging Host",
                    "This will configure the native messaging host for browser communication.\n\nThe installation script will:\n• Register the native messaging host\n• Configure browser permissions\n• Set up communication between PhantomVault and the browser extension\n\nThis requires Administrator privileges.\n\nContinue?",
                    _ownerWindow);

                if (confirmed)
                {
                    await RunBusyOperationAsync(
                        "Configuring native host…",
                        async () =>
                        {
                            UpdateBusyStatus("Requesting elevated permissions…", "UAC prompt may appear in Windows.", progress: 25, indeterminate: false);

                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                                UseShellExecute = true,
                                Verb = "runas" // Run as admin
                            };

                            System.Diagnostics.Process.Start(psi);

                            UpdateBusyStatus("Waiting for script completion…", "Finishing native host registration.", progress: 70, indeterminate: false);
                            await Task.Delay(2000).ConfigureAwait(false); // Allow script time to register the host
                            UpdateBusyStatus("Finalising configuration…", progress: 100, indeterminate: false);
                        },
                        indeterminate: false,
                        detail: "Registering PhantomVault's browser integration.");

                    NativeHostStatusText = "Configured";
                    NativeHostStatusColor = "#28A745"; // Green
                    StatusMessage = "Native messaging host configured";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Configuration Failed", $"Failed to configure native host: {ex.Message}", _ownerWindow);
                StatusMessage = "Native host configuration failed";
            }
        }

        private async System.Threading.Tasks.Task CreateBackupAsync()
        {
            var services = (Avalonia.Application.Current as App)?.Services;
            if (services == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Backup Unavailable",
                    "Application services are not initialised. Restart PhantomVault and try again.",
                    _ownerWindow);
                StatusMessage = "Unable to access backup services.";
                return;
            }

            var backupService = services.GetService(typeof(BackupService)) as BackupService;
            if (backupService == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Backup Unavailable",
                    "The backup service could not be resolved. Repair or reinstall PhantomVault and retry.",
                    _ownerWindow);
                StatusMessage = "Backup service unavailable.";
                return;
            }

            var manifestService = services.GetService(typeof(ManifestService)) as ManifestService;

            if (_vaultViewModel == null)
            {
                await _dialogService.ShowWarningAsync(
                    "Vault Required",
                    "Open settings from an unlocked vault to create a backup.",
                    _ownerWindow);
                StatusMessage = "Backup requires an unlocked vault.";
                return;
            }

            if (!_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                await _dialogService.ShowWarningAsync(
                    "Manifest Not Found",
                    "PhantomVault could not locate the active manifest. Reopen the vault and try again.",
                    _ownerWindow);
                StatusMessage = "Manifest context unavailable.";
                return;
            }

            bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
            bool hasKeyfile = !string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath);
            if (!hasPassphrase && !hasKeyfile)
            {
                await _dialogService.ShowWarningAsync(
                    "Manifest Credentials Needed",
                    "Unlock the vault using its passphrase or keyfile before creating a backup.",
                    _ownerWindow);
                StatusMessage = "Backup cancelled (missing credentials).";
                return;
            }

            string? manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(manifestDirectory))
            {
                manifestDirectory = AppContext.BaseDirectory;
            }
            string backupDirectory = Path.Combine(manifestDirectory!, "backups");

            VaultManifest? manifest = null;
            string? manifestError = null;
            if (manifestService != null)
            {
                await Task.Run(() =>
                {
                    if (!manifestService.TryReadManifest(
                            manifestPath,
                            hasPassphrase ? passphrase : null,
                            hasKeyfile ? keyfilePath : null,
                            out manifest,
                            out manifestError))
                    {
                        manifest = null;
                    }
                });

                if (manifest != null)
                {
                    AutoBackupEnabled = manifest.AutoBackupEnabled;
                }
            }

            string? backupPath = null;
            int retentionDays = manifest?.BackupRetentionDays ?? 0;

            try
            {
                await RunBusyOperationAsync(
                    "Creating backup…",
                    async () =>
                    {
                        UpdateBusyStatus("Preparing manifest…", "Reading encrypted manifest payload.", progress: 20, indeterminate: false);

                        if (manifest == null && manifestError != null)
                        {
                            UpdateBusyStatus("Continuing without manifest metadata…", manifestError, progress: 25, indeterminate: false);
                        }

                        UpdateBusyStatus("Encrypting backup payload…", "Sealing manifest with a fresh AES-GCM key.", progress: 55, indeterminate: false);
                        backupPath = await backupService.CreateBackupAsync(manifestPath, passphrase, keyfilePath, backupDirectory);

                        if (retentionDays > 0)
                        {
                            UpdateBusyStatus("Pruning old backups…", $"Keeping the last {retentionDays} day history.", progress: 80, indeterminate: false);
                            await Task.Run(() => backupService.PruneBackups(backupDirectory, retentionDays));
                        }

                        UpdateBusyStatus("Finalising backup…", progress: 100, indeterminate: false);
                    },
                    indeterminate: false,
                    detail: $"Generating an encrypted snapshot in {backupDirectory}.");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Backup Failed",
                    $"Could not create the backup: {ex.Message}",
                    _ownerWindow);
                StatusMessage = "Backup failed.";
                return;
            }

            if (string.IsNullOrEmpty(backupPath))
            {
                StatusMessage = "Backup cancelled.";
                return;
            }

            string backupName = Path.GetFileName(backupPath);
            string backupLocation = Path.GetDirectoryName(backupPath) ?? backupDirectory;

            await _dialogService.ShowSuccessAsync(
                "Backup Created",
                $"Your vault manifest has been backed up.\n\nFile: {backupName}\nLocation: {backupLocation}",
                _ownerWindow);
            StatusMessage = $"Backup saved to {backupName}";
        }

        private async System.Threading.Tasks.Task RestoreBackupAsync()
        {
            var services = (Avalonia.Application.Current as App)?.Services;
            if (services == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Restore Unavailable",
                    "Application services are not initialised. Restart PhantomVault and try again.",
                    _ownerWindow);
                StatusMessage = "Unable to access restore services.";
                return;
            }

            var backupService = services.GetService(typeof(BackupService)) as BackupService;
            if (backupService == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Restore Unavailable",
                    "The backup service could not be resolved. Repair or reinstall PhantomVault and retry.",
                    _ownerWindow);
                StatusMessage = "Backup service unavailable.";
                return;
            }

            var manifestService = services.GetService(typeof(ManifestService)) as ManifestService;

            if (_vaultViewModel == null)
            {
                await _dialogService.ShowWarningAsync(
                    "Vault Required",
                    "Open settings from an unlocked vault to restore a backup.",
                    _ownerWindow);
                StatusMessage = "Restore requires an unlocked vault.";
                return;
            }

            if (!_vaultViewModel.TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                await _dialogService.ShowWarningAsync(
                    "Manifest Not Found",
                    "PhantomVault could not locate the active manifest. Reopen the vault and try again.",
                    _ownerWindow);
                StatusMessage = "Manifest context unavailable.";
                return;
            }

            bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
            bool hasKeyfile = !string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath);
            if (!hasPassphrase && !hasKeyfile)
            {
                await _dialogService.ShowWarningAsync(
                    "Manifest Credentials Needed",
                    "Unlock the vault using its passphrase or keyfile before restoring a backup.",
                    _ownerWindow);
                StatusMessage = "Restore cancelled (missing credentials).";
                return;
            }

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Restore Backup",
                "⚠ Warning: Restoring from backup will replace your current manifest. Continue?",
                _ownerWindow);

            if (!confirmed)
            {
                StatusMessage = "Restore cancelled.";
                return;
            }

            var topLevel = TopLevel.GetTopLevel(_ownerWindow);
            var storageProvider = topLevel?.StorageProvider ?? _ownerWindow?.StorageProvider;
            if (storageProvider == null)
            {
                await _dialogService.ShowErrorAsync(
                    "File Picker Unavailable",
                    "Cannot open the file picker on this platform.",
                    _ownerWindow);
                StatusMessage = "Restore cancelled.";
                return;
            }

            var backupFileType = new FilePickerFileType("PhantomVault Backups") { Patterns = new[] { "*.bak" } };
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Backup File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    backupFileType,
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files == null || files.Count == 0)
            {
                StatusMessage = "Restore cancelled.";
                return;
            }

            var storageFile = files[0];
            string? backupFilePath = storageFile.TryGetLocalPath();
            string? tempCopyPath = null;

            if (string.IsNullOrEmpty(backupFilePath))
            {
                tempCopyPath = Path.Combine(Path.GetTempPath(), $"phantomvault-restore-{Guid.NewGuid():N}.bak");
                try
                {
                    await using var source = await storageFile.OpenReadAsync();
                    await using var target = File.Create(tempCopyPath);
                    await source.CopyToAsync(target);
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowErrorAsync(
                        "Restore Failed",
                        $"Unable to read the selected backup file: {ex.Message}",
                        _ownerWindow);
                    StatusMessage = "Restore failed.";
                    if (tempCopyPath != null)
                    {
                        try { if (File.Exists(tempCopyPath)) File.Delete(tempCopyPath); } catch { }
                    }
                    return;
                }

                backupFilePath = tempCopyPath;
            }

            VaultManifest? manifest = null;
            string? manifestError = null;

            try
            {
                await RunBusyOperationAsync(
                    "Restoring backup…",
                    async () =>
                    {
                        UpdateBusyStatus("Validating backup file…", $"Opening {Path.GetFileName(backupFilePath)}.", progress: 25, indeterminate: false);
                        await backupService.RestoreBackupAsync(backupFilePath!, manifestPath, passphrase, keyfilePath);

                        if (manifestService != null)
                        {
                            UpdateBusyStatus("Reloading manifest…", "Refreshing retention and backup settings.", progress: 70, indeterminate: false);
                            await Task.Run(() =>
                            {
                                if (!manifestService.TryReadManifest(
                                        manifestPath,
                                        hasPassphrase ? passphrase : null,
                                        hasKeyfile ? keyfilePath : null,
                                        out manifest,
                                        out manifestError))
                                {
                                    manifest = null;
                                }
                            });

                            if (manifest != null)
                            {
                                AutoBackupEnabled = manifest.AutoBackupEnabled;
                            }
                        }

                        UpdateBusyStatus("Restore complete…", progress: 100, indeterminate: false);
                    },
                    indeterminate: false,
                    detail: $"Replacing {Path.GetFileName(manifestPath)} with the selected backup.");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Restore Failed",
                    $"Could not restore the backup: {ex.Message}",
                    _ownerWindow);
                StatusMessage = "Restore failed.";
                if (tempCopyPath != null)
                {
                    try { if (File.Exists(tempCopyPath)) File.Delete(tempCopyPath); } catch { }
                }
                return;
            }
            finally
            {
                if (tempCopyPath != null)
                {
                    try { if (File.Exists(tempCopyPath)) File.Delete(tempCopyPath); } catch { }
                }
            }

            string backupName = Path.GetFileName(backupFilePath!);

            if (manifest == null && !string.IsNullOrEmpty(manifestError))
            {
                await _dialogService.ShowWarningAsync(
                    "Backup Restored",
                    $"The manifest was restored from {backupName}, but PhantomVault could not reload metadata: {manifestError}",
                    _ownerWindow);
                StatusMessage = $"Restored backup {backupName} (metadata reload failed).";
                return;
            }

            await _dialogService.ShowSuccessAsync(
                "Backup Restored",
                $"Successfully restored the manifest from {backupName}.\n\nIf the vault is currently unlocked, re-open it to ensure all settings are refreshed.",
                _ownerWindow);
            StatusMessage = $"Restored backup {backupName}";
        }

        private async System.Threading.Tasks.Task ExportVaultAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Vault as JSON",
                    SuggestedFileName = $"vault_export_{DateTime.Now:yyyy-MM-dd}.json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (file != null)
                {
                    await RunBusyOperationAsync(
                        "Exporting vault…",
                        async () =>
                        {
                            UpdateBusyStatus("Serialising credentials…", progress: 25, indeterminate: false);

                            var exportData = new
                            {
                                ExportDate = DateTime.UtcNow,
                                Version = "5.0.0",
                                VaultName = VaultName,
                                TotalCredentials = _credentials.Count,
                                Credentials = _credentials.Select(c => new
                                {
                                    c.Title,
                                    c.Username,
                                    c.Password,
                                    c.Url,
                                    c.Notes,
                                    c.Group,
                                    c.Tags,
                                    c.CreatedUtc,
                                    c.LastUpdatedUtc,
                                    c.ExpiryUtc,
                                    CustomFields = c.CustomFields
                                }).ToList()
                            };

                            UpdateBusyStatus("Formatting JSON…", progress: 50, indeterminate: false);

                            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });

                            UpdateBusyStatus("Writing export file…", progress: 85, indeterminate: false);

                            await using var stream = await file.OpenWriteAsync();
                            await using var writer = new StreamWriter(stream);
                            await writer.WriteAsync(json);

                            UpdateBusyStatus("Finalising export…", progress: 100, indeterminate: false);
                        },
                        indeterminate: false,
                        detail: $"Saving {_credentials.Count} credentials to {file.Name}.");

                    await _dialogService.ShowSuccessAsync(
                        "Export Complete",
                        $"✓ Successfully exported {_credentials.Count} credentials\n\nFile: {file.Name}\n\n⚠ This file contains unencrypted credentials. Keep it secure!",
                        _ownerWindow);
                    StatusMessage = $"Exported {_credentials.Count} credentials to JSON";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("Export Failed", $"Error exporting vault: {ex.Message}", _ownerWindow);
                StatusMessage = "✗ Export failed";
            }
        }

        private async System.Threading.Tasks.Task ExportVaultCsvAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Vault as CSV",
                    SuggestedFileName = $"vault_export_{DateTime.Now:yyyy-MM-dd}.csv",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (file != null)
                {
                    StringBuilder csv = new();

                    await RunBusyOperationAsync(
                        "Exporting vault…",
                        async () =>
                        {
                            UpdateBusyStatus("Building CSV header…", progress: 15, indeterminate: false);
                            csv.AppendLine("Title,Username,Password,URL,Notes,Category,Tags,Created,LastUpdated,Expiry");

                            UpdateBusyStatus("Formatting credential rows…", progress: 55, indeterminate: false);
                            foreach (var cred in _credentials)
                            {
                                csv.AppendLine(string.Join(",",
                                    EscapeCsvField(cred.Title),
                                    EscapeCsvField(cred.Username),
                                    EscapeCsvField(cred.Password),
                                    EscapeCsvField(cred.Url),
                                    EscapeCsvField(cred.Notes),
                                    EscapeCsvField(cred.Group),
                                    EscapeCsvField(string.Join(";", cred.Tags)),
                                    cred.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                                    cred.LastUpdatedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                                    cred.ExpiryUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty));
                            }

                            UpdateBusyStatus("Writing export file…", progress: 85, indeterminate: false);

                            await using var stream = await file.OpenWriteAsync();
                            await using var writer = new StreamWriter(stream, Encoding.UTF8);
                            await writer.WriteAsync(csv.ToString());

                            UpdateBusyStatus("Finalising export…", progress: 100, indeterminate: false);
                        },
                        indeterminate: false,
                        detail: $"Saving {_credentials.Count} credentials to {file.Name}.");

                    await _dialogService.ShowSuccessAsync(
                        "Export Complete",
                        $"✓ Successfully exported {_credentials.Count} credentials\n\nFile: {file.Name}\nFormat: Title, Username, Password, URL, Notes, Category\n\n⚠ This file is not encrypted. Handle with care!",
                        _ownerWindow);
                    StatusMessage = $"Exported {_credentials.Count} credentials to CSV";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("Export Failed", $"Error exporting vault: {ex.Message}", _ownerWindow);
                StatusMessage = "✗ Export failed";
            }
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "\"\"";

            // Escape quotes and wrap in quotes if contains comma, quote, or newline
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }

            return "\"" + field + "\"";
        }

        private async System.Threading.Tasks.Task ExportKeePassAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Vault as KeePass XML",
                    SuggestedFileName = $"vault_export_{DateTime.Now:yyyy-MM-dd}.xml",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("KeePass XML Files") { Patterns = new[] { "*.xml" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (file != null)
                {
                    await RunBusyOperationAsync(
                        "Exporting vault…",
                        async () =>
                        {
                            UpdateBusyStatus("Preparing KeePass export…", progress: 40, indeterminate: false);
                            var service = new ImportExportService();
                            await service.ExportToKeePassXmlAsync(_credentials, file.Path.LocalPath, "PhantomVault Export");
                            UpdateBusyStatus("Writing XML file…", progress: 90, indeterminate: false);
                            UpdateBusyStatus("Finalising export…", progress: 100, indeterminate: false);
                        },
                        indeterminate: false,
                        detail: $"Saving {_credentials.Count} credentials to {file.Name}.");

                    await _dialogService.ShowSuccessAsync(
                        "Export Complete",
                        $"Successfully exported {_credentials.Count} credentials to KeePass XML format.\n\nFile: {file.Name}",
                        _ownerWindow);

                    StatusMessage = $"Exported {_credentials.Count} credentials to KeePass XML";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Export Failed",
                    $"Failed to export vault:\n{ex.Message}",
                    _ownerWindow);
                StatusMessage = "Export failed";
            }
        }

        private async System.Threading.Tasks.Task OpenImportWindowAsync()
        {
            try
            {
                // Create and show the import window with existing credentials for duplicate detection
                var importWindow = new ImportWindow(_credentials);

                // Wire up the import window's event (not the ViewModel's) to receive imported credentials
                importWindow.ImportCompleted += (sender, importedCredentials) =>
                {
                    // Add imported credentials to local collection
                    _credentials.AddRange(importedCredentials);

                    // Notify parent (VaultWindow) about new credentials
                    if (OnCredentialsImported != null)
                    {
                        OnCredentialsImported.Invoke(importedCredentials);
                    }
                };

                if (_ownerWindow != null)
                {
                    await importWindow.ShowDialog(_ownerWindow);
                }

                StatusMessage = "Import completed successfully";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Error",
                    $"Failed to open import window:\n{ex.Message}",
                    _ownerWindow);
                StatusMessage = "Failed to open import window";
            }
        }

        private async System.Threading.Tasks.Task ImportVaultAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Vault from JSON",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    int imported = 0;
                    int skipped = 0;

                    await RunBusyOperationAsync(
                        "Importing vault…",
                        async () =>
                        {
                            UpdateBusyStatus("Reading import file…", progress: 20, indeterminate: false);
                            await using var stream = await file.OpenReadAsync();
                            using var reader = new StreamReader(stream);
                            var json = await reader.ReadToEndAsync();

                            UpdateBusyStatus("Parsing credentials…", progress: 55, indeterminate: false);
                            var importData = JsonSerializer.Deserialize<JsonElement>(json);

                            if (importData.TryGetProperty("credentials", out var credentialsElement))
                            {
                                var newCredentials = new List<Credential>();

                                foreach (var credElement in credentialsElement.EnumerateArray())
                                {
                                    try
                                    {
                                        var title = credElement.GetProperty("title").GetString() ?? string.Empty;

                                        if (_credentials.Any(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            skipped++;
                                            continue;
                                        }

                                        var credential = new Credential
                                        {
                                            Title = title,
                                            Username = credElement.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty,
                                            Password = credElement.TryGetProperty("password", out var p) ? p.GetString() ?? string.Empty : string.Empty,
                                            Url = credElement.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                                            Notes = credElement.TryGetProperty("notes", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                                            Group = credElement.TryGetProperty("group", out var g) ? g.GetString() ?? "Logins" : "Logins",
                                            CreatedUtc = credElement.TryGetProperty("createdUtc", out var cu) ? DateTimeOffset.Parse(cu.GetString() ?? string.Empty) : DateTimeOffset.UtcNow,
                                            LastUpdatedUtc = DateTimeOffset.UtcNow
                                        };

                                        if (credElement.TryGetProperty("tags", out var tagsElement))
                                        {
                                            credential.Tags = tagsElement.EnumerateArray()
                                                .Select(t => t.GetString() ?? string.Empty)
                                                .Where(t => !string.IsNullOrEmpty(t))
                                                .ToList();
                                        }

                                        if (credElement.TryGetProperty("expiryUtc", out var expiry) && !string.IsNullOrEmpty(expiry.GetString()))
                                        {
                                            credential.ExpiryUtc = DateTimeOffset.Parse(expiry.GetString() ?? string.Empty);
                                        }

                                        newCredentials.Add(credential);
                                        imported++;
                                    }
                                    catch
                                    {
                                        skipped++;
                                    }
                                }

                                UpdateBusyStatus("Applying imported credentials…", progress: 85, indeterminate: false);
                                _credentials.AddRange(newCredentials);
                                OnCredentialsImported?.Invoke(_credentials);
                                UpdateBusyStatus("Finalising import…", progress: 100, indeterminate: false);
                            }
                        },
                        indeterminate: false,
                        detail: $"Processing {file.Name}.");

                    await _dialogService.ShowSuccessAsync(
                        "Import Complete",
                        $"✓ Successfully imported {imported} credentials\n✓ Skipped {skipped} duplicates\n✓ Total vault items: {_credentials.Count}",
                        _ownerWindow);
                    StatusMessage = $"Imported {imported} credentials from JSON";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("Import Failed", $"Error importing vault: {ex.Message}", _ownerWindow);
                StatusMessage = "✗ Import failed";
            }
        }

        private async System.Threading.Tasks.Task ImportVaultCsvAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Vault from CSV",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    int imported = 0;
                    int invalid = 0;

                    await RunBusyOperationAsync(
                        "Importing vault…",
                        async () =>
                        {
                            await using var stream = await file.OpenReadAsync();
                            using var reader = new StreamReader(stream);

                            UpdateBusyStatus("Reading CSV header…", progress: 15, indeterminate: false);
                            await reader.ReadLineAsync();

                            var newCredentials = new List<Credential>();
                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                {
                                    continue;
                                }

                                try
                                {
                                    var fields = ParseCsvLine(line);

                                    if (fields.Count < 3)
                                    {
                                        invalid++;
                                        continue;
                                    }

                                    var title = fields[0];
                                    var username = fields[1];
                                    var password = fields[2];

                                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(password))
                                    {
                                        invalid++;
                                        continue;
                                    }

                                    if (_credentials.Any(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        invalid++;
                                        continue;
                                    }

                                    var credential = new Credential
                                    {
                                        Title = title,
                                        Username = username,
                                        Password = password,
                                        Url = fields.Count > 3 ? fields[3] : string.Empty,
                                        Notes = fields.Count > 4 ? fields[4] : string.Empty,
                                        Group = fields.Count > 5 && !string.IsNullOrWhiteSpace(fields[5]) ? fields[5] : "Logins",
                                        CreatedUtc = DateTimeOffset.UtcNow,
                                        LastUpdatedUtc = DateTimeOffset.UtcNow
                                    };

                                    if (fields.Count > 6 && !string.IsNullOrWhiteSpace(fields[6]))
                                    {
                                        credential.Tags = fields[6].Split(';')
                                            .Select(t => t.Trim())
                                            .Where(t => !string.IsNullOrEmpty(t))
                                            .ToList();
                                    }

                                    newCredentials.Add(credential);
                                    imported++;
                                }
                                catch
                                {
                                    invalid++;
                                }
                            }

                            UpdateBusyStatus("Applying imported credentials…", progress: 85, indeterminate: false);
                            _credentials.AddRange(newCredentials);
                            OnCredentialsImported?.Invoke(_credentials);
                            UpdateBusyStatus("Finalising import…", progress: 100, indeterminate: false);
                        },
                        indeterminate: false,
                        detail: $"Processing {file.Name}.");

                    await _dialogService.ShowSuccessAsync(
                        "Import Complete",
                        $"✓ Successfully imported {imported} credentials\n✓ Skipped {invalid} invalid/duplicate rows\n✓ Total vault items: {_credentials.Count}",
                        _ownerWindow);
                    StatusMessage = $"Imported {imported} credentials from CSV";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("Import Failed", $"Error importing vault: {ex.Message}", _ownerWindow);
                StatusMessage = "✗ Import failed";
            }
        }

        private List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            fields.Add(currentField.ToString());
            return fields;
        }

        private async System.Threading.Tasks.Task ImportKeePassAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(_ownerWindow);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null)
                {
                    await _dialogService.ShowWarningAsync("Error", "File picker not available", _ownerWindow);
                    return;
                }

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import from KeePass Database",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("KeePass Database") { Patterns = new[] { "*.kdbx" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];

                    await _dialogService.ShowInfoAsync(
                        "KeePass Import",
                        $"Selected file: {file.Name}\n\nKeePass import requires the KeePassLib library for decrypting .kdbx files.\n\nThis feature is currently in development.\n\nYou can use 'Export to CSV' from KeePass, then use our CSV import feature as a workaround.",
                        _ownerWindow);

                    StatusMessage = "KeePass import in development - use CSV export from KeePass instead";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("Import Failed", $"Error: {ex.Message}", _ownerWindow);
                StatusMessage = "✗ Import failed";
            }
        }

        private async System.Threading.Tasks.Task ClearVaultDataAsync()
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Clear Vault Data",
                "🗑 WARNING: This will permanently delete ALL vault data!\n\nThis action cannot be undone. Are you absolutely sure?",
                _ownerWindow);

            if (confirmed)
            {
                await _dialogService.ShowWarningAsync(
                    "Data Cleared",
                    "This feature is disabled in demo mode.\n\nIn production, this would securely wipe all vault data.",
                    _ownerWindow);
                StatusMessage = "Clear data operation cancelled (demo mode)";
            }
        }

        private async System.Threading.Tasks.Task ResetToDefaultsAsync()
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Reset to Defaults",
                "This will reset all settings to their default values.\n\nYour vault data will not be affected. Continue?",
                _ownerWindow);

            if (confirmed)
            {
                // Reset to defaults
                ThemeSelection = 0;
                ClipboardClearTime = 1;
                AutoLockEnabled = true;
                AutoLockTime = 1;
                DefaultPasswordLength = 16;
                IncludeUppercase = true;
                IncludeLowercase = true;
                IncludeNumbers = true;
                IncludeSymbols = true;
                AutoFillEnabled = true;
                AutoFillOnPageLoad = false;
                ShowAutoFillIcon = true;
                AutoBackupEnabled = false;
                EnableDebugLogging = false;
                AllowConcurrentSessions = false;

                StatusMessage = "Settings reset to defaults";
            }
        }

        private async System.Threading.Tasks.Task OpenFlatIconDownloaderAsync()
        {
            if (_ownerWindow == null) return;

            // Create and show the icon downloader window
            var viewModel = new IconDownloaderViewModel();
            var window = new IconDownloaderWindow
            {
                DataContext = viewModel
            };
            viewModel.SetOwnerWindow(window);

            await window.ShowDialog(_ownerWindow);
        }

        private void Close()
        {
            DismissCategoryManagerPanel();
            _ownerWindow?.Close();
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
            RefreshPasskeyStatus();
        }

        public void SetCredentials(List<Credential> credentials)
        {
            _credentials = credentials ?? new List<Credential>();
        }

        public Action<List<Credential>>? OnCredentialsImported { get; set; }
    }
}
