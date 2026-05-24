using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Views;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Helpers;
using PhantomVault.UI.Desktop.Controls;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the vault window with full credential management UI.
    /// </summary>
    public sealed class VaultViewModel : ReactiveObject
    {
        private readonly VaultService _vaultService;
        private readonly ManifestService _manifestService;
        private readonly IdleLockService _idleLockService;
        private readonly IZkVaultService _zkVaultService;
        private readonly DialogService _dialogService;
        private readonly VaultLockDurationService _vaultLockDurationService;
        private readonly PhantomVault.UI.Services.SettingsDraftTracker _settingsDraftTracker;
        private readonly IUsbDetector _usbDetector;
        private readonly SecureTrashService _secureTrashService;
        private readonly IconManager _iconManager;
        private readonly IClipboardGuard? _clipboardGuard;
        private CancellationTokenSource? _clipboardClearCts;
        private readonly RekeyService? _rekeyService;
        private readonly SecurityCoordinator? _securityCoordinator;
        private readonly IUsbAutoInjectService? _autoInjectService;
        private readonly DecoyVaultService? _decoyVaultService;
        private readonly TamperDetectionService? _tamperDetectionService;
        private readonly IntegratedRecoveryService _integratedRecoveryService;
        private readonly IntegratedAttestorService _integratedAttestorService;
        private readonly RecoveryVaultPathResolver _recoveryVaultPathResolver;
        private readonly RecoverySuiteBootstrapService _recoverySuiteBootstrapService;
        private readonly ObscuraCredentialIndexService _obscuraCredentialIndexService;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private Window? _ownerWindow;
        private bool _autoLockSubscribed;
        private SecurityThreatLevel _currentThreatLevel = SecurityThreatLevel.None;
        private string _securityStatus = "Secure";
        private bool _showSecurityAlert;

        // TOTP Synchronization
        private PhantomVault.Core.Services.Sync.TotpSyncServiceObscura? _totpSyncService;

        private string _mountPath = string.Empty;
        // Vault database tracking for hybrid encryption
        private string? _vaultFilePath; // Path to vault.pvault file inside mounted container
        private string? _vaultPassword; // Cached password for vault operations
        private string? _vaultKeyfilePath; // Cached keyfile path for vault operations
        private bool _isPersistingCredentialIndex;
        private ObservableCollection<string> _items = new();
        private ObservableCollection<CredentialViewModel> _credentials = new();
        private ObservableCollection<CredentialViewModel> _filteredCredentials = new();
        private ObservableCollection<ListItemWrapper> _groupedListItems = new(); // Grouped list with category headers
        private ObservableCollection<CredentialViewModel> _passkeys = new(); // Separate collection for passkeys
        private ObservableCollection<CredentialViewModel> _flaggedCredentials = new();
        private ObservableCollection<CategoryViewModel> _categories = new();
        private ObservableCollection<SecureTrashItemViewModel> _filteredTrashItems = new();
        private CredentialViewModel? _selectedCredential; // Selected credential for detail view
        private SecureTrashItemViewModel? _selectedTrashItem;
        private bool _isBusy;
        private string _status = string.Empty;
        private string _vaultName = "My Vault";
        private string _searchText = string.Empty;
        private string _currentViewTitle = "All Items";
        private string _statusMessage = "Ready";
        private string _lastSyncTime = DateTime.Now.ToString("HH:mm");
        private int _sortOption = 0;
        private bool _isShowingAll = false;  // Start with dashboard, not All Items
        private bool _isShowingPasswords = false;
        private bool _isShowingFavorites = false;
        private bool _isShowingPasskeys = false;
        private bool _isShowingRecent = false;
        private bool _isShowingExpiringSoon = false;
        private bool _isShowingDashboard = false;  // Dashboard closed by default — user pulls it down
        private bool _isDashboardEnabled = true;   // User setting: dashboard on/off
        private bool _isInitializing = true;  // Prevent navigation during initialization
        private bool _privacyModeEnabled;
        private bool _isPasswordHealthPanelVisible;
        private string _viewModeIcon = "☰";
        private string _gridViewIconPath = "Assets/SVG/Current/Grid.svg"; // Start in list view, show grid icon to toggle TO grid
        private bool _isGridView = false;
        private bool _isDarkTheme = false;
        private string _totpSecretInput = string.Empty;
        private bool _isEditPanelVisible = false;
        private bool _isCategoryManagerPanelVisible = false;
        private bool _isFlaggedPanelVisible;
        private bool _isRecoveryPanelVisible;
        private string DashboardRecoveryStatus => IsEmbeddedRecoveryAvailable
            ? "Vault-backed recovery is available from this vault."
            : EmbeddedRecoveryStatus;
        private ViewModels.CategoryManagerViewModel? _categoryManagerViewModel;
        private Core.Models.EntryType? _currentEntryType = null; // null = show all types

        // Sidebar collapse state
        private bool _isSidebarCollapsed = true;
        private bool _isSidebarAutoCollapsed = false;

        // In-app lockscreen state
        private bool _isLockscreenVisible;
        private bool _isSoftLocked;
        private string _lockscreenTitle = "Vault Locked";
        private string _lockscreenMessage = "";
        private string _lockscreenPassword = string.Empty;
        private string _lockscreenPin = string.Empty;
        private string _lockscreenError = string.Empty;
        private int _lockscreenFailedUnlockAttempts;
        private DateTimeOffset? _lockscreenLockedUntilUtc;
        private bool _showPassphraseFallback;
        private bool _isDeveloperBypassMode;

        // Context needed to re-mount on hard lock (non-sensitive)
        private string? _usbRootPath;
        private string? _manifestPath;

        /// <summary>
        /// Read-only view of the current vault manifest path. Allows view-level
        /// services (e.g. auto-lock gating) to resolve PIN configuration without
        /// reaching into private state.
        /// </summary>
        public string? ManifestPath => _manifestPath;

        private string? _containerAbsPath;
        private string? _reauthKeyfilePath;
        private string? _masterVolumePath;
        private string? _transportLayoutRoot;
        private VaultProtectionTier _protectionTier = VaultProtectionTier.StandardSecure;
        private VaultStorageTransport _effectiveStorageTransport = VaultStorageTransport.FileSystem;
        private VaultStorageTransport? _requestedStorageTransport;

        public CategoryManagerViewModel? CategoryManagerViewModel
        {
            get => _categoryManagerViewModel;
            private set => this.RaiseAndSetIfChanged(ref _categoryManagerViewModel, value);
        }

        public bool IsCategoryManagerPanelVisible
        {
            get => _isCategoryManagerPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isCategoryManagerPanelVisible, value);
        }
        private AddEditCredentialViewModel? _editViewModel;
        private bool _isShowingSecureTrash;
        private string? _activeCategory;
        private string _activeCategoryDisplayName = string.Empty;
        // Undo/Redo stacks to support multiple actions
        private readonly Stack<(string ActionType, object Payload)> _undoStack = new();
        private readonly Stack<(string ActionType, object Payload)> _redoStack = new();

        // Sidebar quick filter highlight layout constants
        private const double QuickFilterButtonHeight = 44d;
        private const double QuickFilterSpacing = 6d;
        private const double QuickFilterHorizontalMargin = 0d;
        private const double QuickFilterBaseOffset = 0d;
        private const double QuickFilterActiveOpacity = 0.9d;
        private const double QuickFilterForwardNudge = 4d;
        private const double QuickFilterLightShiftRange = 0.16d;

        private const double CategoryButtonHeight = 38d;
        private const double CategoryButtonSpacing = 4d;
        private const double CategoryHighlightHorizontalMargin = 0d;
        private const double CategoryHighlightBaseOffset = 2d;
        private const double CategoryHighlightActiveOpacity = 0.9d;
        private const double CategoryHighlightForwardNudge = 4d;
        private const double CategoryHighlightLightShiftRange = 0.16d;

        private Thickness _quickFilterHighlightMargin = new(QuickFilterHorizontalMargin, QuickFilterBaseOffset, QuickFilterHorizontalMargin, 0);
        private double _quickFilterHighlightOpacity = QuickFilterActiveOpacity;
        private double _quickFilterHighlightOffset;
        private double _quickFilterHighlightLightShift = 0.5d;
        private double _quickFilterHighlightLightLeft = 0.18d;
        private double _quickFilterHighlightLightRight = 0.82d;
        private double _quickFilterHighlightSheenInnerLeft = 0.42d;
        private double _quickFilterHighlightSheenInnerRight = 0.58d;

        private Thickness _categoryHighlightMargin = new(CategoryHighlightHorizontalMargin, CategoryHighlightBaseOffset, CategoryHighlightHorizontalMargin, 0);
        private double _categoryHighlightOpacity;
        private double _categoryHighlightOffset;
        private double _categoryHighlightLightShift = 0.5d;
        private double _categoryHighlightLightLeft = 0.18d;
        private double _categoryHighlightLightRight = 0.82d;
        private double _categoryHighlightSheenInnerLeft = 0.42d;
        private double _categoryHighlightSheenInnerRight = 0.58d;

        public VaultViewModel(
            VaultService vaultService,
            ManifestService manifestService,
            IdleLockService idleLockService,
            IZkVaultService zkVaultService,
            DialogService dialogService,
            VaultLockDurationService vaultLockDurationService,
            IUsbDetector usbDetector,
            SecureTrashService secureTrashService,
            IconManager iconManager,
            IClipboardGuard? clipboardGuard = null,
            RekeyService? rekeyService = null,
            SecurityCoordinator? securityCoordinator = null,
            IUsbAutoInjectService? autoInjectService = null,
            DecoyVaultService? decoyVaultService = null,
            TamperDetectionService? tamperDetectionService = null,
            IntegratedRecoveryService? integratedRecoveryService = null,
            IntegratedAttestorService? integratedAttestorService = null,
            RecoveryVaultPathResolver? recoveryVaultPathResolver = null,
            RecoverySuiteBootstrapService? recoverySuiteBootstrapService = null,
            PhantomVault.UI.Services.SettingsDraftTracker? settingsDraftTracker = null)
        {
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _idleLockService = idleLockService ?? throw new ArgumentNullException(nameof(idleLockService));
            _zkVaultService = zkVaultService ?? throw new ArgumentNullException(nameof(zkVaultService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _vaultLockDurationService = vaultLockDurationService ?? throw new ArgumentNullException(nameof(vaultLockDurationService));
            // Issue #32: critical fix — when the ctor param is null (typical
            // path because VaultUnlockViewModel constructs VaultViewModel
            // directly without going through DI for this dep), resolve the
            // singleton tracker from the App service provider. Falling back
            // to `new SettingsDraftTracker()` here would create a fresh
            // instance that's not the one the migrated tab VMs stage into —
            // so HasUnsavedSettings would NEVER fire and the Save button
            // would never activate. Only fall back to a brand-new instance
            // if DI itself isn't available (tests, headless harnesses).
            _settingsDraftTracker = settingsDraftTracker
                ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.SettingsDraftTracker)) as PhantomVault.UI.Services.SettingsDraftTracker)
                ?? new PhantomVault.UI.Services.SettingsDraftTracker();
            // Re-emit the tracker's HasUnsavedChanges as a VM property so XAML
            // can bind to it for the Save button enabled state.
            _settingsDraftTracker.WhenAnyValue(t => t.HasUnsavedChanges)
                .Subscribe(v => this.RaisePropertyChanged(nameof(HasUnsavedSettings)));
            _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
            _secureTrashService = secureTrashService ?? throw new ArgumentNullException(nameof(secureTrashService));
            _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));
            _clipboardGuard = clipboardGuard;
            _rekeyService = rekeyService;
            _securityCoordinator = securityCoordinator;
            _autoInjectService = autoInjectService;
            _decoyVaultService = decoyVaultService;
            _tamperDetectionService = tamperDetectionService;
            _integratedRecoveryService = integratedRecoveryService ?? new IntegratedRecoveryService();
            _integratedAttestorService = integratedAttestorService ?? new IntegratedAttestorService();
            _recoveryVaultPathResolver = recoveryVaultPathResolver
                ?? ((Application.Current as App)?.Services?.GetService(typeof(RecoveryVaultPathResolver)) as RecoveryVaultPathResolver)
                ?? new RecoveryVaultPathResolver(new UsbArtifactProtectionService(new EncryptionService()));
            _recoverySuiteBootstrapService = recoverySuiteBootstrapService
                ?? ((Application.Current as App)?.Services?.GetService(typeof(RecoverySuiteBootstrapService)) as RecoverySuiteBootstrapService)
                ?? new RecoverySuiteBootstrapService(new UsbArtifactProtectionService(new EncryptionService()));
            _obscuraCredentialIndexService = (Application.Current as App)?.Services?.GetService(typeof(ObscuraCredentialIndexService)) as ObscuraCredentialIndexService
                ?? new ObscuraCredentialIndexService();
            _blackSecureRawVolumeService = (Application.Current as App)?.Services?.GetService(typeof(BlackSecureRawVolumeService)) as BlackSecureRawVolumeService
                ?? new BlackSecureRawVolumeService();

            // Subscribe to security events
            if (_securityCoordinator != null)
            {
                _securityCoordinator.ThreatLevelChanged += OnThreatLevelChanged;
                _securityCoordinator.CriticalThreatDetected += OnCriticalThreatDetected;
            }

            // Initialize USB auto-inject service
            if (_autoInjectService != null)
            {
                InitializeAutoInject();
            }

            // Initialize sample categories
            InitializeCategories();

            // Subscribe to USB removal to lock/zero immediately
            _usbDetector.RemovableDriveRemoved += OnUsbRemoved;

            // Subscribe to idle timer so inactivity actually locks the vault
            _idleLockService.IdleElapsed += OnIdleLockElapsed;

            // Initialize commands
            AddCredentialCommand = ReactiveCommand.CreateFromTask(AddCredentialAsync);
            AddCredentialWithTypeCommand = ReactiveCommand.CreateFromTask<string>(AddCredentialWithTypeAsync);
            AddLoginCredentialCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Login"));
            AddPaymentCardCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Payment Card"));
            AddCreditCardCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Credit Card"));
            AddBankAccountCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Bank Account"));
            AddIdentityCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Identity"));
            AddSecureNoteCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Secure Note"));
            AddApiKeyCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("API Key"));
            AddSoftwareLicenseCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Software License"));
            AddWiFiPasswordCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("WiFi Password"));
            AddContactCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Contact"));
            AddPinCodeCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("PIN Code"));
            DashboardViewModel = new DashboardViewModel();
            DashboardViewModel.NavigateToFilter = NavigateToVaultWithFilter;
            DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);
            DashboardViewModel.SetAttestorAvailability(_integratedAttestorService.IsAvailable, _integratedAttestorService.AvailabilityMessage);
            EditCredentialCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(EditCredentialAsync);
            DeleteCredentialCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(DeleteCredentialAsync);
            CloseEditPanelCommand = ReactiveCommand.Create(CloseEditPanel);
            CopyPasswordCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(CopyPasswordAsync);
            CopyUsernameCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(CopyUsernameAsync);
            CopyTotpCodeCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(CopyTotpCodeAsync);
            AddTotpCommand = ReactiveCommand.CreateFromTask(AddTotpAsync);
            EditTotpCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(EditTotpAsync);
            OpenAttestorCommand = ReactiveCommand.Create(OpenAttestor);
            SaveTotpSecretCommand = ReactiveCommand.CreateFromTask(SaveTotpSecretAsync);
            RemoveTotpCommand = ReactiveCommand.CreateFromTask(RemoveTotpAsync);
            OpenUrlCommand = ReactiveCommand.CreateFromTask<CredentialViewModel>(OpenUrlAsync);
            // TileClickCommand: when a tile (list or grid) is clicked, select the credential to show in detail view
            TileClickCommand = ReactiveCommand.Create<CredentialViewModel>(SelectCredential);
            ToggleFavoriteCommand = ReactiveCommand.Create<CredentialViewModel>(ToggleFavorite);

            ShowDashboardCommand = ReactiveCommand.Create(ShowDashboard);
            ShowPasswordsCommand = ReactiveCommand.Create(ShowPasswords);
            ShowAllCommand = ReactiveCommand.Create(ShowAll);
            ShowFavoritesCommand = ReactiveCommand.Create(ShowFavorites);
            ShowPasskeysCommand = ReactiveCommand.Create(ShowPasskeys);
            ShowRecentCommand = ReactiveCommand.Create(ShowRecent);
            ShowExpiringSoonCommand = ReactiveCommand.Create(ShowExpiringSoon);
            SelectCategoryCommand = ReactiveCommand.Create<string>(SelectCategory);
            SetEntryTypeCommand = ReactiveCommand.Create<string?>(SetEntryType);
            FilterByEntryTypeCommand = ReactiveCommand.Create<string>(FilterByEntryType);
            ManageCategoriesCommand = ReactiveCommand.CreateFromTask(ManageCategoriesAsync);
            ShowFlaggedPasswordsCommand = ReactiveCommand.CreateFromTask(ShowFlaggedPasswordsAsync);
            CloseFlaggedPasswordsCommand = ReactiveCommand.Create(CloseFlaggedPasswordsPanel);
            OpenPhantomRecoveryCommand = ReactiveCommand.CreateFromTask(OpenPhantomRecoveryAsync);
            ToggleRecoveryPanelCommand = ReactiveCommand.Create(ToggleRecoveryPanel);
            OpenSecurityOptionsCommand = ReactiveCommand.Create(OpenSecurityOptions);
            OpenPasswordHealthPanelCommand = ReactiveCommand.Create(OpenPasswordHealthPanel);
            ClosePasswordHealthPanelCommand = ReactiveCommand.Create(ClosePasswordHealthPanel);
            OpenTrashManagerCommand = ReactiveCommand.CreateFromTask(OpenTrashManagerAsync);
            UndoCommand = ReactiveCommand.CreateFromTask(UndoLastActionAsync);
            RedoCommand = ReactiveCommand.CreateFromTask(RedoLastActionAsync);
            RecoverTrashItemCommand = ReactiveCommand.CreateFromTask<SecureTrashItemViewModel?>(RestoreTrashItemAsync);
            PurgeTrashItemCommand = ReactiveCommand.CreateFromTask<SecureTrashItemViewModel?>(PurgeTrashItemAsync);
            RestoreSelectedTrashCommand = ReactiveCommand.CreateFromTask(RestoreSelectedTrashAsync);
            PurgeSelectedTrashCommand = ReactiveCommand.CreateFromTask(PurgeSelectedTrashAsync);
            ToggleTrashSelectAllCommand = ReactiveCommand.Create(ToggleTrashSelection);

            ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
            ToggleViewModeCommand = ReactiveCommand.Create(ToggleViewMode);
            ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            ClearSelectedCredentialCommand = ReactiveCommand.Create(() => { SelectedCredential = null; });
            // The actual palette window is opened by the View — the VM merely
            // raises this command so the View (which owns Window creation per
            // MVVM rules) can react. Action seeding happens in
            // BuildCommandPaletteActions() below.
            OpenCommandPaletteCommand = ReactiveCommand.Create(() => { });
            OpenSettingsPanelCommand = ReactiveCommand.Create(OpenSettingsPanel);
            // Issue #21: route close through the async intercept so unsaved
            // changes trigger the Save-or-Cancel prompt.
            CloseSettingsPanelCommand = ReactiveCommand.CreateFromTask(CloseSettingsPanelAsync);
            // Keep these commands always invokable; SaveStagedSettings and
            // DiscardStagedSettings fail closed when there is no dirty draft.
            // The visual dirty state is bound separately so the button cannot
            // get stranded disabled after a save/re-edit cycle.
            SaveSettingsCommand = ReactiveCommand.Create(SaveStagedSettings);
            DiscardSettingsCommand = ReactiveCommand.Create(DiscardStagedSettings);
            ToggleSecurityDashboardCommand = ReactiveCommand.Create(ToggleSecurityDashboard);
            OpenSecurityDashboardWindowCommand = ReactiveCommand.Create(() => 
            {
                StatusMessage = "🔍 BUTTON CLICKED - Command executed!";
                System.Diagnostics.Debug.WriteLine(">>> OpenSecurityDashboardWindowCommand button clicked");
                OpenSecurityDashboardWindow();
            });
            OpenEditPasswordCommand = ReactiveCommand.Create<PhantomVault.Core.Models.Credential>(credential =>
            {
                // Find the CredentialViewModel for this credential
                var credVM = _credentials.FirstOrDefault(c => c.GetCredential().Id == credential.Id);
                Views.EditPasswordWindow.ShowDialog(null, credential, credVM, this);
            });
            RefreshSecurityDashboard = ReactiveCommand.Create(() => RefreshSecurityDashboardMetrics());
            CopySuggestedPasswordCommand = ReactiveCommand.Create(CopySuggestedPassword);
            
            // Filter commands
            FilterShowAllCommand = ReactiveCommand.Create(() => { ShowAllFilter = true; ShowCriticalFilter = false; ShowLowFilter = false; ShowMediumFilter = false; ShowGoodFilter = false; });
            FilterCriticalCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowCriticalFilter = !ShowCriticalFilter; });
            FilterLowCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowLowFilter = !ShowLowFilter; });
            FilterMediumCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowMediumFilter = !ShowMediumFilter; });
            FilterGoodCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowGoodFilter = !ShowGoodFilter; });
            
            // Issue #31: route every tab-switch through the unsaved-changes
            // intercept. Pre-existing changes on any tab prompt Save & Switch
            // / Cancel before the tab content swaps.
            ShowGeneralSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowGeneralSettings));
            ShowSecuritySettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSecuritySettings));
            ShowThemeSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowThemeSettings));
            ShowImportExportSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowImportExportSettings));
            ShowAutoFillSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAutoFillSettings));
            ShowBackupSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowBackupSettings));
            ShowAccessibilitySettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAccessibilitySettings));
            ShowAdvancedSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAdvancedSettings));
            ShowRubbishBinSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowRubbishBinSettings));
            ShowPasswordHealthCommand = ReactiveCommand.Create(ShowPasswordHealth);
            ShowSyncSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSyncSettings));
            OpenIconManagerCommand = ReactiveCommand.Create(OpenIconManager);
            CloseIconManagerCommand = ReactiveCommand.Create(CloseIconManager);
            RotateNowCommand = ReactiveCommand.CreateFromTask(RotateNowAsync);
            DismissRotationBannerCommand = ReactiveCommand.Create(DismissRotationBanner);

            // Initialize Import/Export commands
            OpenImportWindowCommand = ReactiveCommand.CreateFromTask(OpenImportWindowAsync);
            ExportVaultCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("json"));
            ExportVaultCsvCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("csv"));
            ExportKeePassCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("keepass"));

            PrivacyModeEnabled = PrivacyShield.PrivacyModeEnabled;
            PrivacyShield.PrivacyModeChanged += OnPrivacyModeChanged;

            if (_secureTrashService.AutoPurgeEnabled)
            {
                var purged = _secureTrashService.SecurelyPurgeExpired();
                if (purged > 0)
                {
                    StatusMessage = $"Securely purged {purged} expired item{(purged == 1 ? string.Empty : "s")}";
                }
            }

            OpenItemCommand = ReactiveCommand.CreateFromTask<string>(async item =>
            {
                await _dialogService.ShowInfoAsync(
                    "Item Selected",
                    $"Selected item: {item}\n\nIn a production build, this would open the file with the appropriate program.",
                    _ownerWindow);
                Status = $"Selected: {item}";
            });

            LockCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await LockInAppAsync(LockReason.ManualLock);
            });

            UnlockWithPasswordCommand = ReactiveCommand.CreateFromTask(UnlockWithPasswordAsync);
            UnlockWithPinCommand = ReactiveCommand.CreateFromTask(UnlockWithPinAsync);
            ShowPassphraseFallbackCommand = ReactiveCommand.Create(ShowPassphraseFallback);
            DismissLockscreenCommand = ReactiveCommand.Create(DismissLockscreen);
            SetupPinLockCommand = ReactiveCommand.CreateFromTask(SetupPinLockAsync);

            // Subscribe to search text changes
            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFilters());

            // Subscribe to sort option changes
            this.WhenAnyValue(x => x.SortOption)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFilters());

            // Vault starts empty by default; entries are loaded from disk via the vault service.
            // (Previously LoadSampleCredentials() seeded placeholder demo entries — removed
            // so a freshly-created vault is genuinely empty.)
            // LoadSampleCredentials();

            // Initialize CategoryLandingViewModel
            CategoryLandingViewModel = new CategoryLandingViewModel(vaultService);

            // Subscribe to navigation messages from category landing page
            MessageBus.Current.Listen<NavigateToVaultWithFilterMessage>()
                .Subscribe(msg => NavigateToVaultWithFilter(msg.FilterType));

            // Load dashboard-enabled preference
            try
            {
                var userSettings = SettingsService.Load();
                _isDashboardEnabled = userSettings.DashboardEnabled;
                _isGridView = userSettings.PreferGridView;
                _gridViewIconPath = _isGridView ? "Assets/SVG/Current/List.svg" : "Assets/SVG/Current/Grid.svg";
            }
            catch { /* defaults to true */ }

            // Set initial view based on dashboard preference
            if (_isDashboardEnabled)
            {
                CurrentViewTitle = "Passwords";
                _isShowingDashboard = false;
                _isShowingPasswords = true;
                IsSidebarCollapsed = false;
            }
            else
            {
                // Skip dashboard — go straight to Passwords view
                _isShowingDashboard = false;
                _isShowingPasswords = true;
                CurrentViewTitle = "Passwords";
                IsSidebarCollapsed = false;
            }

            // Populate dashboard quick access tiles from sample data so they
            // are visible on the initial dashboard view (before any vault is loaded).
            if (IsShowingDashboard && _credentials.Count > 0)
            {
                DashboardViewModel.LoadQuickAccessCredentials(_credentials, _categories);
                DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);
            }

            // Initialization complete - allow navigation
            _isInitializing = false;
        }

        public ReactiveCommand<Unit, Unit> OpenTrashManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> UndoCommand { get; }
        public ReactiveCommand<Unit, Unit> RedoCommand { get; }

        /// <summary>
        /// Update all credentials that are in <paramref name="fromCategory"/> to have Group = <paramref name="toCategory"/>.
        /// Returns the number of credentials modified.
        /// Changes are persisted to the vault database automatically.
        /// </summary>
        public int MoveCredentialsToCategory(string fromCategory, string toCategory)
        {
            if (string.IsNullOrEmpty(fromCategory) || string.IsNullOrEmpty(toCategory)) return 0;
            int changed = 0;
            foreach (var vm in _credentials)
            {
                var cred = vm.GetCredential();
                if (cred.Group == fromCategory)
                {
                    cred.Group = toCategory;
                    cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                    vm.Refresh();
                    changed++;
                }
            }
            UpdateCategoryCounts();
            ApplyFilters();

            // Persist changes to vault database
            if (changed > 0)
            {
                _ = SaveVaultAsync();
            }

            return changed;
        }

        /// <summary>
        /// Permanently delete all credentials that belong to the given category.
        /// Returns the number of credentials removed.
        /// </summary>
        public int DeleteCredentialsInCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return 0;
            var toRemove = _credentials.Where(c => c.Group == category).ToList();
            foreach (var vm in toRemove)
            {
                _credentials.Remove(vm);
            }
            UpdateCategoryCounts();
            ApplyFilters();
            return toRemove.Count;
        }

        /// <summary>
        /// Move credentials in the given category to the application's Trash category.
        /// If a Trash category does not exist, create one named "Trash".
        /// Returns number of credentials moved.
        /// </summary>
        public int MoveCredentialsToTrash(string fromCategory)
        {
            if (string.IsNullOrEmpty(fromCategory)) return 0;
            var moved = 0;
            var affected = _credentials
                .Where(c => c.Group == fromCategory)
                .ToList();
            var secureEnabled = _secureTrashService.IsEnabled;

            foreach (var vm in affected)
            {
                var record = _secureTrashService.MoveToTrash(vm.GetCredential());
                if (secureEnabled)
                {
                    _undoStack.Push(("Delete", (object)record));
                }
                _credentials.Remove(vm);
                moved++;
            }

            UpdateCategoryCounts();
            ApplyFilters();

            return moved;
        }

        /// <summary>
        /// Gets a list of credentials in the specified category with basic info (Title, Username).
        /// Used for migration dialogs. Returns unique identifier based on Title+Username.
        /// </summary>
        public List<(string Key, string Title, string Username)> GetCredentialsInCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return new List<(string, string, string)>();

            return _credentials
                .Where(c => c.Group == category)
                .Select(vm =>
                {
                    var cred = vm.GetCredential();
                    var key = $"{cred.Title}|{cred.Username}";
                    return (key, cred.Title, cred.Username);
                })
                .ToList();
        }

        /// <summary>
        /// Moves specific credentials (by composite key) from source category to target category.
        /// The keys are in format "Title|Username" to uniquely identify credentials.
        /// Returns the number of credentials moved.
        /// </summary>
        public int MoveCredentialsByKeys(List<string> credentialKeys, string toCategory)
        {
            if (credentialKeys == null || credentialKeys.Count == 0 || string.IsNullOrEmpty(toCategory))
                return 0;

            int changed = 0;
            foreach (var vm in _credentials)
            {
                var cred = vm.GetCredential();
                var key = $"{cred.Title}|{cred.Username}";
                if (credentialKeys.Contains(key))
                {
                    cred.Group = toCategory;
                    cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                    vm.Refresh();
                    changed++;
                }
            }

            UpdateCategoryCounts();
            ApplyFilters();
            return changed;
        }

        private string? ResolveDeletedCategoryName()
        {
            try
            {
                // Prefer the stored manifest path from SetManifestContext
                string? manifestPath = _manifestPath;
                if (manifestPath == null && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.pvault");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }
                if (manifestPath == null && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.manifest");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }
                if (manifestPath == null) return null;
                // Use non-throwing TryReadManifest to avoid bubbling parse/decrypt exceptions
                if (!_manifestService.TryReadManifest(manifestPath, null, null, out var manifest, out var manifestError))
                {
                    // If manifest cannot be read, treat as no-deleted-category configured
                    // Optionally log manifestError to diagnostics in future
                    return null;
                }
                // Prefer a category explicitly named 'Deleted'
                var deleted = manifest?.Categories?.FirstOrDefault(c => string.Equals(c.Name, "Deleted", StringComparison.OrdinalIgnoreCase));
                return deleted?.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Restore a set of credentials to a target category (used by Trash Manager)
        /// </summary>
        public int RestoreCredentialsToCategory(List<Credential> credentials, string targetCategory)
        {
            int restored = 0;
            foreach (var cred in credentials)
            {
                var vm = _credentials.FirstOrDefault(c => c.GetCredential() == cred);
                if (vm != null)
                {
                    cred.Group = targetCategory;
                    cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                    vm.Refresh();
                    restored++;
                }
            }
            UpdateCategoryCounts();
            ApplyFilters();
            return restored;
        }

        public async Task OpenTrashManagerAsync()
        {
            var records = _secureTrashService.Records;
            if (records.Count == 0)
            {
                await _dialogService.ShowInfoAsync("Secure Rubbish Bin", "There are no items in the secure bin.", _ownerWindow);
                return;
            }

            var dialogResult = await _dialogService.ShowTrashManagerAsync(records.ToList(), _ownerWindow);

            if (dialogResult.Action == DialogService.TrashManagerAction.Restore)
            {
                var categories = _categories
                    .Where(c => !string.Equals(c.Name, "Secure Rubbish Bin", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .ToList();
                var target = await _dialogService.ShowSelectCategoryAsync(categories, "Restore selected items to category:", _ownerWindow);
                if (!string.IsNullOrEmpty(target))
                {
                    var restoredCount = 0;
                    foreach (var record in dialogResult.Selected)
                    {
                        var restored = _secureTrashService.Restore(record.Id);
                        if (restored.success && restored.credential != null)
                        {
                            restored.credential.Group = target;
                            var vm = new CredentialViewModel(restored.credential);
                            _credentials.Add(vm);
                            restoredCount++;
                        }
                    }
                    UpdateCategoryCounts();
                    ApplyFilters();
                    if (restoredCount > 0)
                    {
                        await _dialogService.ShowInfoAsync("Restored", $"Restored {restoredCount} item{(restoredCount == 1 ? string.Empty : "s")} to '{target}'.", _ownerWindow);
                        await SaveVaultAsync();
                    }
                    _undoStack.Push(("Restore", (object)(target, dialogResult.Selected.Select(r => r.Payload).ToList())));
                }
            }
            else if (dialogResult.Action == DialogService.TrashManagerAction.Empty)
            {
                var purged = 0;
                foreach (var record in dialogResult.Selected)
                {
                    if (_secureTrashService.SecurelyPurge(record.Id))
                    {
                        purged++;
                    }
                }
                UpdateCategoryCounts();
                ApplyFilters();
                if (purged > 0)
                {
                    await _dialogService.ShowInfoAsync("Secure Purge", $"Securely deleted {purged} item{(purged == 1 ? string.Empty : "s")}.", _ownerWindow);
                }
                _undoStack.Push(("EmptyTrash", (object)dialogResult.Selected.Select(r => r.Payload).ToList()));
            }
        }

        /// <summary>
        /// Replace the category list from manifest models and refresh counts/filters.
        /// Called by Category Manager after saving changes.
        /// </summary>
        public void UpdateCategoriesFromModels(System.Collections.Generic.List<PhantomVault.Core.Models.CategoryModel> models)
        {
            if (models == null || models.Count == 0) return;
            var ordered = models.OrderBy(m => m.Order).ToList();
            var newList = new ObservableCollection<CategoryViewModel>();
            foreach (var m in ordered)
            {
                newList.Add(new CategoryViewModel { Name = m.Name, Icon = IconPathMigrator.Migrate(m.Icon), TileColor = m.TileColor });
            }

            // Marshal changes onto the UI thread to avoid cross-thread collection/property updates.
            Dispatcher.UIThread.Post(() =>
            {
                Categories = newList;
                UpdateCategoryActiveStates();
                UpdateCategoryCounts();
                ApplyFilters();
            });
        }

        /// <summary>
        /// Updates the sidebar category tile colors using a mapping of name -> hex color (or null to clear).
        /// </summary>
        public void UpdateCategoryColors(System.Collections.Generic.Dictionary<string, string?> colorMap)
        {
            if (colorMap == null || colorMap.Count == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var cat in _categories)
                {
                    if (colorMap.TryGetValue(cat.Name, out var hex))
                    {
                        cat.TileColor = hex;
                    }
                }
            });
        }

        /// <summary>
        /// Returns a copy of the credential list that belong to the specified category.
        /// </summary>
        public List<Credential> GetCredentialsForCategory(string category)
        {
            return _credentials
                .Where(c => c.Group == category)
                .Select(c => c.GetCredential())
                .ToList();
        }

        public async Task UndoLastActionAsync()
        {
            if (!_undoStack.Any())
            {
                await _dialogService.ShowInfoAsync("Undo", "Nothing to undo.", _ownerWindow);
                return;
            }

            var entry = _undoStack.Pop();
            var pushToRedo = false;
            if (entry.ActionType == "MoveToTrash")
            {
                var payload = ((string fromCategory, List<Credential> list))entry.Payload;
                // Move items back from Trash to original category
                var restored = 0;
                foreach (var cred in payload.list)
                {
                    var vm = _credentials.FirstOrDefault(c => c.GetCredential() == cred);
                    if (vm != null)
                    {
                        cred.Group = payload.fromCategory;
                        cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                        vm.Refresh();
                        restored++;
                    }
                }
                UpdateCategoryCounts();
                ApplyFilters();
                await _dialogService.ShowInfoAsync("Undo", $"Restored {restored} items from Trash to '{payload.fromCategory}'.", _ownerWindow);
                pushToRedo = true;
            }
            else if (entry.ActionType == "Restore")
            {
                // revert a restore -> move items back to Trash
                var payload = ((string target, List<Credential> list))entry.Payload;
                var moved = 0;
                foreach (var cred in payload.list)
                {
                    var vm = _credentials.FirstOrDefault(c => c.GetCredential() == cred);
                    if (vm != null)
                    {
                        cred.Group = "Trash";
                        cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                        vm.Refresh();
                        moved++;
                    }
                }
                UpdateCategoryCounts();
                ApplyFilters();
                await _dialogService.ShowInfoAsync("Undo", $"Moved {moved} items back to Trash.", _ownerWindow);
                pushToRedo = true;
            }
            else if (entry.ActionType == "EmptyTrash")
            {
                // not easily undoable without deeper backup; inform user
                await _dialogService.ShowWarningAsync("Undo Not Available", "Emptying the trash cannot be undone.", _ownerWindow);
            }
            else if (entry.ActionType == "Delete")
            {
                if (entry.Payload is SecureTrashRecord record)
                {
                    var restored = _secureTrashService.Restore(record.Id);
                    if (restored.success && restored.credential != null)
                    {
                        if (!string.IsNullOrEmpty(restored.originalGroup))
                        {
                            restored.credential.Group = restored.originalGroup!;
                        }

                        var vm = new CredentialViewModel(restored.credential);
                        _credentials.Add(vm);
                        UpdateCategoryCounts();
                        ApplyFilters();
                        StatusMessage = $"Restored: {restored.credential.Title}";
                        await SaveVaultAsync();
                    }
                    else
                    {
                        await _dialogService.ShowWarningAsync(
                            "Undo",
                            "The entry has already been securely purged and cannot be restored.",
                            _ownerWindow);
                    }
                }
            }
            // Push to redo stack so we can redo this undo
            if (pushToRedo)
            {
                _redoStack.Push(entry);
            }
        }

        public async Task RedoLastActionAsync()
        {
            if (!_redoStack.Any())
            {
                await _dialogService.ShowInfoAsync("Redo", "Nothing to redo.", _ownerWindow);
                return;
            }

            var entry = _redoStack.Pop();
            // Re-execute the original action (which will push back to undo stack)
            if (entry.ActionType == "MoveToTrash")
            {
                var payload = ((string fromCategory, List<Credential> list))entry.Payload;
                // Re-move items from original category to Trash
                var moved = 0;
                foreach (var cred in payload.list)
                {
                    var vm = _credentials.FirstOrDefault(c => c.GetCredential() == cred);
                    if (vm != null)
                    {
                        cred.Group = "Trash";
                        cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                        vm.Refresh();
                        moved++;
                    }
                }
                UpdateCategoryCounts();
                ApplyFilters();
                await _dialogService.ShowInfoAsync("Redo", $"Moved {moved} items back to Trash.", _ownerWindow);
                _undoStack.Push(entry);
            }
            else if (entry.ActionType == "Restore")
            {
                // Re-restore items from Trash to target
                var payload = ((string target, List<Credential> list))entry.Payload;
                var restored = 0;
                foreach (var cred in payload.list)
                {
                    var vm = _credentials.FirstOrDefault(c => c.GetCredential() == cred);
                    if (vm != null)
                    {
                        cred.Group = payload.target;
                        cred.LastUpdatedUtc = DateTimeOffset.UtcNow;
                        vm.Refresh();
                        restored++;
                    }
                }
                UpdateCategoryCounts();
                ApplyFilters();
                await _dialogService.ShowInfoAsync("Redo", $"Restored {restored} items to '{payload.target}'.", _ownerWindow);
                _undoStack.Push(entry);
            }
        }

        public ObservableCollection<string> Items
        {
            get => _items;
            private set => this.RaiseAndSetIfChanged(ref _items, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public bool IsLockscreenVisible
        {
            get => _isLockscreenVisible;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isLockscreenVisible, value);
                this.RaisePropertyChanged(nameof(PinUnlockAvailable));
                this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
                this.RaisePropertyChanged(nameof(ShowLockscreenDismiss));
            }
        }

        public bool IsSoftLocked
        {
            get => _isSoftLocked;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isSoftLocked, value);
                this.RaisePropertyChanged(nameof(PinUnlockAvailable));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
                this.RaisePropertyChanged(nameof(ShowLockscreenDismiss));
            }
        }

        public string LockscreenTitle
        {
            get => _lockscreenTitle;
            private set => this.RaiseAndSetIfChanged(ref _lockscreenTitle, value);
        }

        public string LockscreenMessage
        {
            get => _lockscreenMessage;
            private set => this.RaiseAndSetIfChanged(ref _lockscreenMessage, value);
        }

        public string LockscreenPassword
        {
            get => _lockscreenPassword;
            set => this.RaiseAndSetIfChanged(ref _lockscreenPassword, value);
        }

        public string LockscreenPin
        {
            get => _lockscreenPin;
            set => this.RaiseAndSetIfChanged(ref _lockscreenPin, value);
        }

        public string LockscreenError
        {
            get => _lockscreenError;
            private set => this.RaiseAndSetIfChanged(ref _lockscreenError, value);
        }

        public bool PinUnlockAvailable
        {
            get
            {
                var settings = SettingsService.Load();
                return IsLockscreenVisible
                       && IsSoftLocked
                       && settings.EnablePinLock
                       && PinLockService.HasPinConfigured(settings, _manifestPath);
            }
        }

        public bool PasswordUnlockAvailable => IsLockscreenVisible && !string.IsNullOrWhiteSpace(_manifestPath);

        public bool IsDeveloperBypassMode => _isDeveloperBypassMode;

        public bool ShowLockscreenDismiss =>
            IsLockscreenVisible
            && IsDeveloperBypassMode
            && !PinUnlockAvailable
            && !PasswordUnlockAvailable;

        public ReactiveCommand<Unit, Unit> DismissLockscreenCommand { get; }
        public ReactiveCommand<Unit, Unit> SetupPinLockCommand { get; }

        public bool ShowPassphraseFallbackSection
        {
            get
            {
                // If PIN is available, keep passphrase hidden until requested.
                if (PinUnlockAvailable)
                    return _showPassphraseFallback && PasswordUnlockAvailable;

                // Otherwise show passphrase normally.
                return PasswordUnlockAvailable;
            }
        }

        public bool ShowPassphraseFallbackLink => PinUnlockAvailable && PasswordUnlockAvailable && !_showPassphraseFallback;

        private void ShowPassphraseFallback()
        {
            _showPassphraseFallback = true;
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
        }

        public void SetReauthContext(string usbRootPath, string manifestPath, string containerAbsPath, string? keyfilePath)
        {
            _usbRootPath = usbRootPath;
            _manifestPath = manifestPath;
            _containerAbsPath = containerAbsPath;
            _reauthKeyfilePath = keyfilePath;

            this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
            this.RaisePropertyChanged(nameof(PinUnlockAvailable));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
            this.RaisePropertyChanged(nameof(ShowLockscreenDismiss));
        }

        public void SetDeveloperBypassMode(bool enabled)
        {
            _isDeveloperBypassMode = enabled;
            this.RaisePropertyChanged(nameof(IsDeveloperBypassMode));
            this.RaisePropertyChanged(nameof(ShowLockscreenDismiss));
        }

        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        // Properties
        public string VaultName
        {
            get => _vaultName;
            set => this.RaiseAndSetIfChanged(ref _vaultName, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => this.RaiseAndSetIfChanged(ref _searchText, value);
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public string CurrentViewTitle
        {
            get => _currentViewTitle;
            set => this.RaiseAndSetIfChanged(ref _currentViewTitle, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string LastSyncTime
        {
            get => _lastSyncTime;
            set => this.RaiseAndSetIfChanged(ref _lastSyncTime, value);
        }

        public int SortOption
        {
            get => _sortOption;
            set
            {
                this.RaiseAndSetIfChanged(ref _sortOption, value);

                // Auto-switch to list view when Category sort is selected
                if (value == 4 && IsGridView)
                {
                    ViewModeIcon = "☰";
                    GridViewIconPath = "Assets/SVG/Current/Grid.svg";
                    IsGridView = false;
                    StatusMessage = "Switched to list view for category grouping";
                }
            }
        }

        public bool IsShowingAll
        {
            get => _isShowingAll;
            set => this.RaiseAndSetIfChanged(ref _isShowingAll, value);
        }

        public bool IsShowingPasswords
        {
            get => _isShowingPasswords;
            set => this.RaiseAndSetIfChanged(ref _isShowingPasswords, value);
        }

        public bool IsPasswordHealthPanelVisible
        {
            get => _isPasswordHealthPanelVisible;
            set => this.RaiseAndSetIfChanged(ref _isPasswordHealthPanelVisible, value);
        }

        public PasswordHealthViewModel? PasswordHealthPanelViewModel { get; private set; }

        public bool IsShowingFavorites
        {
            get => _isShowingFavorites;
            set => this.RaiseAndSetIfChanged(ref _isShowingFavorites, value);
        }

        public bool IsShowingPasskeys
        {
            get => _isShowingPasskeys;
            set => this.RaiseAndSetIfChanged(ref _isShowingPasskeys, value);
        }

        public bool IsShowingRecent
        {
            get => _isShowingRecent;
            set => this.RaiseAndSetIfChanged(ref _isShowingRecent, value);
        }

        public bool IsShowingExpiringSoon
        {
            get => _isShowingExpiringSoon;
            set => this.RaiseAndSetIfChanged(ref _isShowingExpiringSoon, value);
        }

        public bool IsShowingDashboard
        {
            get => _isShowingDashboard;
            set => this.RaiseAndSetIfChanged(ref _isShowingDashboard, value);
        }

        /// <summary>
        /// User setting: when false the Dashboard view is disabled, the sidebar
        /// Dashboard button is hidden, and the app starts on the Passwords view.
        /// </summary>
        public bool IsDashboardEnabled
        {
            get => _isDashboardEnabled;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isDashboardEnabled, value))
                {
                    // Issue #21: stage the change in the draft tracker instead
                    // of saving immediately. UI side-effects (navigation away
                    // from a disabled Dashboard) still fire so the toggle feels
                    // live, but persistence waits until the user clicks Save.
                    // Discard rewinds both this property and the persisted
                    // value back to whatever was on disk at the time of stage.
                    var previousPersisted = SafeLoadDashboardEnabled();
                    if (value == previousPersisted)
                    {
                        // User toggled back to the saved state — drop staged work.
                        _settingsDraftTracker.ClearKey("General.DashboardEnabled");
                    }
                    else
                    {
                        _settingsDraftTracker.Stage(
                            key: "General.DashboardEnabled",
                            commit: () =>
                            {
                                try
                                {
                                    var s = SettingsService.Load();
                                    s.DashboardEnabled = value;
                                    SettingsService.Save(s);
                                }
                                catch { /* best-effort */ }
                            },
                            discard: () =>
                            {
                                // Revert the UI-side value to the persisted one.
                                this.RaiseAndSetIfChanged(ref _isDashboardEnabled, previousPersisted, nameof(IsDashboardEnabled));
                            });
                    }

                    if (!value && IsShowingDashboard)
                    {
                        // Dashboard just got disabled while we're on it — navigate away
                        ShowPasswords();
                    }
                }
            }
        }

        private static bool SafeLoadDashboardEnabled()
        {
            try { return SettingsService.Load().DashboardEnabled; }
            catch { return true; }
        }

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"IsSidebarCollapsed changing from {_isSidebarCollapsed} to {value}");
#endif
                this.RaiseAndSetIfChanged(ref _isSidebarCollapsed, value);
            }
        }

        public bool IsSidebarAutoCollapsed
        {
            get => _isSidebarAutoCollapsed;
            set => this.RaiseAndSetIfChanged(ref _isSidebarAutoCollapsed, value);
        }

        public Core.Models.EntryType? CurrentEntryType
        {
            get => _currentEntryType;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentEntryType, value);
                ApplyFilters();
            }
        }

        public bool IsShowingSecureTrash
        {
            get => _isShowingSecureTrash;
            private set => this.RaiseAndSetIfChanged(ref _isShowingSecureTrash, value);
        }

        public bool EnableScreenshotProtection
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.EnableScreenshotProtection;
            }
            set
            {
                SettingsService.Update(settings => settings.EnableScreenshotProtection = value);
                this.RaisePropertyChanged();
            }
        }

        public bool RequireHardwareToken
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.RequireHardwareToken;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.RequireHardwareToken = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public bool RequireKeyfile
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.RequireKeyfile;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.RequireKeyfile = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public int IdleTimeoutMinutes
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.IdleTimeoutMinutes;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.IdleTimeoutMinutes = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public bool AutoCopyTotpWithPassword
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.AutoCopyTotpWithPassword;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.AutoCopyTotpWithPassword = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public bool EnableDecoyVault
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.EnableDecoyVault;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.EnableDecoyVault = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public int DecoyCredentialCount
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.DecoyCredentialCount;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.DecoyCredentialCount = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public bool DecoyReadOnlyMode
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.DecoyReadOnlyMode;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.DecoyReadOnlyMode = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public bool DecoyLogActivations
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.DecoyLogActivations;
            }
            set
            {
                var settings = SettingsService.Load();
                settings.DecoyLogActivations = value;
                SettingsService.Save(settings);
                this.RaisePropertyChanged();
            }
        }

        public Thickness QuickFilterHighlightMargin
        {
            get => _quickFilterHighlightMargin;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightMargin, value);
        }

        public double QuickFilterHighlightOpacity
        {
            get => _quickFilterHighlightOpacity;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightOpacity, value);
        }

        public double QuickFilterHighlightOffset
        {
            get => _quickFilterHighlightOffset;
            private set
            {
                if (Math.Abs(value - _quickFilterHighlightOffset) < 0.001d)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _quickFilterHighlightOffset, value);
                UpdateQuickFilterHighlightLightShift(value);
            }
        }

        public double QuickFilterHighlightLightShift
        {
            get => _quickFilterHighlightLightShift;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightLightShift, value);
        }

        public double QuickFilterHighlightLightLeft
        {
            get => _quickFilterHighlightLightLeft;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightLightLeft, value);
        }

        public double QuickFilterHighlightLightRight
        {
            get => _quickFilterHighlightLightRight;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightLightRight, value);
        }

        public double QuickFilterHighlightSheenInnerLeft
        {
            get => _quickFilterHighlightSheenInnerLeft;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightSheenInnerLeft, value);
        }

        public double QuickFilterHighlightSheenInnerRight
        {
            get => _quickFilterHighlightSheenInnerRight;
            private set => this.RaiseAndSetIfChanged(ref _quickFilterHighlightSheenInnerRight, value);
        }

        public Thickness CategoryHighlightMargin
        {
            get => _categoryHighlightMargin;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightMargin, value);
        }

        public double CategoryHighlightOpacity
        {
            get => _categoryHighlightOpacity;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightOpacity, value);
        }

        public double CategoryHighlightOffset
        {
            get => _categoryHighlightOffset;
            private set
            {
                if (Math.Abs(value - _categoryHighlightOffset) < 0.001d)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _categoryHighlightOffset, value);
                UpdateCategoryHighlightLightShift(value);
            }
        }

        public double CategoryHighlightLightShift
        {
            get => _categoryHighlightLightShift;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightLightShift, value);
        }

        public double CategoryHighlightLightLeft
        {
            get => _categoryHighlightLightLeft;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightLightLeft, value);
        }

        public double CategoryHighlightLightRight
        {
            get => _categoryHighlightLightRight;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightLightRight, value);
        }

        public double CategoryHighlightSheenInnerLeft
        {
            get => _categoryHighlightSheenInnerLeft;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightSheenInnerLeft, value);
        }

        public double CategoryHighlightSheenInnerRight
        {
            get => _categoryHighlightSheenInnerRight;
            private set => this.RaiseAndSetIfChanged(ref _categoryHighlightSheenInnerRight, value);
        }

        public string ViewModeIcon
        {
            get => _viewModeIcon;
            set => this.RaiseAndSetIfChanged(ref _viewModeIcon, value);
        }

        public string GridViewIconPath
        {
            get => _gridViewIconPath;
            set => this.RaiseAndSetIfChanged(ref _gridViewIconPath, value);
        }

        public bool IsGridView
        {
            get => _isGridView;
            set => this.RaiseAndSetIfChanged(ref _isGridView, value);
        }

        public int SelectedDefaultView
        {
            get
            {
                var settings = SettingsService.Load();
                return settings.PreferGridView ? 1 : 0;
            }
            set
            {
                // Issue #21: stage the change instead of persisting on every
                // ComboBox selection. UI updates (icon swap, status message)
                // stay immediate; the SettingsService write waits for Save.
                var preferGrid = value == 1;
                var previousPersisted = SettingsService.Load().PreferGridView;

                IsGridView = preferGrid;
                GridViewIconPath = preferGrid ? "Assets/SVG/Current/List.svg" : "Assets/SVG/Current/Grid.svg";
                StatusMessage = preferGrid ? "Default view set to grid" : "Default view set to list";
                this.RaisePropertyChanged();

                if (preferGrid == previousPersisted)
                {
                    _settingsDraftTracker.ClearKey("General.PreferGridView");
                }
                else
                {
                    _settingsDraftTracker.Stage(
                        key: "General.PreferGridView",
                        commit: () =>
                        {
                            var s = SettingsService.Load();
                            s.PreferGridView = preferGrid;
                            SettingsService.Save(s);
                        },
                        discard: () =>
                        {
                            // Roll the UI-visible view back; trigger property change so
                            // bound ComboBox/SelectedIndex updates.
                            IsGridView = previousPersisted;
                            GridViewIconPath = previousPersisted ? "Assets/SVG/Current/List.svg" : "Assets/SVG/Current/Grid.svg";
                            this.RaisePropertyChanged(nameof(SelectedDefaultView));
                        });
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
                ApplyThemeVariant();
            }
        }

        public bool PrivacyModeEnabled
        {
            get => _privacyModeEnabled;
            private set => this.RaiseAndSetIfChanged(ref _privacyModeEnabled, value);
        }

        public ObservableCollection<CredentialViewModel> FilteredCredentials
        {
            get => _filteredCredentials;
            private set => this.RaiseAndSetIfChanged(ref _filteredCredentials, value);
        }

        /// <summary>
        /// Gets the grouped list items with category headers when sorted by category.
        /// </summary>
        public ObservableCollection<ListItemWrapper> GroupedListItems
        {
            get => _groupedListItems;
            private set => this.RaiseAndSetIfChanged(ref _groupedListItems, value);
        }

        /// <summary>
        /// Returns true when sorted by category (SortOption == 4).
        /// </summary>
        public bool IsSortedByCategory => SortOption == 4;

        public ObservableCollection<SecureTrashItemViewModel> FilteredTrashItems
        {
            get => _filteredTrashItems;
            private set => this.RaiseAndSetIfChanged(ref _filteredTrashItems, value);
        }

        public ObservableCollection<CredentialViewModel> Passkeys
        {
            get => _passkeys;
            private set => this.RaiseAndSetIfChanged(ref _passkeys, value);
        }

        public ObservableCollection<CredentialViewModel> FlaggedCredentials
        {
            get => _flaggedCredentials;
            private set
            {
                if (ReferenceEquals(_flaggedCredentials, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _flaggedCredentials, value);
                this.RaisePropertyChanged(nameof(FlaggedCredentialCount));
                this.RaisePropertyChanged(nameof(HasFlaggedCredentials));
            }
        }

        public ObservableCollection<CategoryViewModel> Categories
        {
            get => _categories;
            private set => this.RaiseAndSetIfChanged(ref _categories, value);
        }

        public CredentialViewModel? SelectedCredential
        {
            get => _selectedCredential;
            set => this.RaiseAndSetIfChanged(ref _selectedCredential, value);
        }

        public SecureTrashItemViewModel? SelectedTrashItem
        {
            get => _selectedTrashItem;
            set => this.RaiseAndSetIfChanged(ref _selectedTrashItem, value);
        }

        public int TotalCount => _credentials.Count;
        public int FavoritesCount => _credentials.Count(c => c.IsFavorite);
        public int FilteredCount => IsShowingSecureTrash ? FilteredTrashItems.Count : FilteredCredentials.Count;
        public int PasskeysCount => _passkeys.Count;
        public int FlaggedCredentialCount => FlaggedCredentials.Count;
        public bool IsEmpty => FilteredCount == 0;

        public bool HasTrashSelection => FilteredTrashItems.Any(item => item.IsSelected);
        public bool HasFlaggedCredentials => FlaggedCredentialCount > 0;

        public string ActiveCategoryDisplayName
        {
            get => _activeCategoryDisplayName;
            private set => this.RaiseAndSetIfChanged(ref _activeCategoryDisplayName, value);
        }

        public bool IsShowingCategoryFiltered => !string.IsNullOrEmpty(_activeCategory) && (IsShowingAll || IsShowingFavorites);

        public bool IsEditPanelVisible
        {
            get => _isEditPanelVisible;
            set => this.RaiseAndSetIfChanged(ref _isEditPanelVisible, value);
        }

        public AddEditCredentialViewModel? EditViewModel
        {
            get => _editViewModel;
            set => this.RaiseAndSetIfChanged(ref _editViewModel, value);
        }

        public bool IsFlaggedPanelVisible
        {
            get => _isFlaggedPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isFlaggedPanelVisible, value);
        }

        public bool IsRecoveryPanelVisible
        {
            get => _isRecoveryPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isRecoveryPanelVisible, value);
        }

        public bool IsEmbeddedRecoveryAvailable => GetRecoveryVaultResolution().HasHeader;

        public string EmbeddedRecoveryStatus => GetRecoveryVaultResolution().Message;

        public bool IsExternalRecoveryAvailable => _integratedRecoveryService.IsAvailable;

        public string ExternalRecoveryStatus => _integratedRecoveryService.AvailabilityMessage;

        public bool IsAttestorAvailable => _integratedAttestorService.IsAvailable;

        public string AttestorStatus => _integratedAttestorService.AvailabilityMessage;

        private bool _isSettingsPanelVisible;
        public bool IsSettingsPanelVisible
        {
            get => _isSettingsPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSettingsPanelVisible, value);
        }

        private bool _isSecurityDashboardVisible;
        public bool IsSecurityDashboardVisible
        {
            get => _isSecurityDashboardVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSecurityDashboardVisible, value);
        }

        private ObservableCollection<PhantomVault.UI.Desktop.Controls.WeakCredentialItem> _weakCredentials = new();
        public ObservableCollection<PhantomVault.UI.Desktop.Controls.WeakCredentialItem> WeakCredentials
        {
            get => _weakCredentials;
            private set => this.RaiseAndSetIfChanged(ref _weakCredentials, value);
        }

        private ObservableCollection<DashboardCredentialItem> _filteredCredentialsForDashboard = new();
        public ObservableCollection<DashboardCredentialItem> FilteredCredentialsForDashboard
        {
            get => _filteredCredentialsForDashboard;
            private set => this.RaiseAndSetIfChanged(ref _filteredCredentialsForDashboard, value);
        }

        // Filter states
        private bool _showAllFilter = true;
        public bool ShowAllFilter
        {
            get => _showAllFilter;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showAllFilter, value))
                    ApplySecurityFilters();
            }
        }

        private bool _showCriticalFilter = false;
        public bool ShowCriticalFilter
        {
            get => _showCriticalFilter;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showCriticalFilter, value))
                    ApplySecurityFilters();
            }
        }

        private bool _showLowFilter = false;
        public bool ShowLowFilter
        {
            get => _showLowFilter;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showLowFilter, value))
                    ApplySecurityFilters();
            }
        }

        private bool _showMediumFilter = false;
        public bool ShowMediumFilter
        {
            get => _showMediumFilter;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showMediumFilter, value))
                    ApplySecurityFilters();
            }
        }

        private bool _showGoodFilter = false;
        public bool ShowGoodFilter
        {
            get => _showGoodFilter;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _showGoodFilter, value))
                    ApplySecurityFilters();
            }
        }

        // Security Dashboard metrics
        private int _securityScore = 0;
        public int SecurityScore
        {
            get => _securityScore;
            private set => this.RaiseAndSetIfChanged(ref _securityScore, value);
        }

        private int _breachedPasswordCount = 0;
        public int BreachedPasswordCount
        {
            get => _breachedPasswordCount;
            private set => this.RaiseAndSetIfChanged(ref _breachedPasswordCount, value);
        }

        private int _reusedPasswordCount = 0;
        public int ReusedPasswordCount
        {
            get => _reusedPasswordCount;
            private set => this.RaiseAndSetIfChanged(ref _reusedPasswordCount, value);
        }

        private int _expiringCredentialCount = 0;
        public int ExpiringCredentialCount
        {
            get => _expiringCredentialCount;
            private set => this.RaiseAndSetIfChanged(ref _expiringCredentialCount, value);
        }

        private int _twoFactorEnabledCount = 0;
        public int TwoFactorEnabledCount
        {
            get => _twoFactorEnabledCount;
            private set => this.RaiseAndSetIfChanged(ref _twoFactorEnabledCount, value);
        }

        private DateTime? _lastBreachCheckTime;
        public DateTime? LastBreachCheckTime
        {
            get => _lastBreachCheckTime;
            private set => this.RaiseAndSetIfChanged(ref _lastBreachCheckTime, value);
        }

        private bool _isRefreshingDashboard = false;
        public bool IsRefreshingDashboard
        {
            get => _isRefreshingDashboard;
            private set => this.RaiseAndSetIfChanged(ref _isRefreshingDashboard, value);
        }

        // Password Strength Tester
        private string _testPassword = string.Empty;
        public string TestPassword
        {
            get => _testPassword;
            set
            {
                this.RaiseAndSetIfChanged(ref _testPassword, value);
                UpdatePasswordTestResults();
            }
        }

        private int _testPasswordScore = 0;
        public int TestPasswordScore
        {
            get => _testPasswordScore;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordScore, value);
        }

        private string _testPasswordStrengthLabel = string.Empty;
        public string TestPasswordStrengthLabel
        {
            get => _testPasswordStrengthLabel;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordStrengthLabel, value);
        }

        private string _testPasswordStrengthColor = "#808080";
        public string TestPasswordStrengthColor
        {
            get => _testPasswordStrengthColor;
            private set => this.RaiseAndSetIfChanged(ref _testPasswordStrengthColor, value);
        }

        private string _suggestedPassword = string.Empty;
        public string SuggestedPassword
        {
            get => _suggestedPassword;
            private set
            {
                this.RaiseAndSetIfChanged(ref _suggestedPassword, value);
                this.RaisePropertyChanged(nameof(HasSuggestedPassword));
            }
        }

        private int _similarityPercentage = 0;
        public int SimilarityPercentage
        {
            get => _similarityPercentage;
            private set => this.RaiseAndSetIfChanged(ref _similarityPercentage, value);
        }

        public bool HasSuggestedPassword => !string.IsNullOrEmpty(_suggestedPassword);

        private bool _isIconManagerPanelVisible;
        public bool IsIconManagerPanelVisible
        {
            get => _isIconManagerPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isIconManagerPanelVisible, value);
        }

        private IconManagerViewModel? _iconManagerViewModel;
        public IconManagerViewModel? IconManagerViewModel
        {
            get => _iconManagerViewModel;
            private set => this.RaiseAndSetIfChanged(ref _iconManagerViewModel, value);
        }

        private bool _isShowingCategoryLanding = false;
        public bool IsShowingCategoryLanding
        {
            get => _isShowingCategoryLanding;
            set => this.RaiseAndSetIfChanged(ref _isShowingCategoryLanding, value);
        }

        private CategoryLandingViewModel? _categoryLandingViewModel;
        public CategoryLandingViewModel? CategoryLandingViewModel
        {
            get => _categoryLandingViewModel;
            private set => this.RaiseAndSetIfChanged(ref _categoryLandingViewModel, value);
        }

        public DashboardViewModel DashboardViewModel { get; }

        private bool _isRotationRequired;
        public bool IsRotationRequired
        {
            get => _isRotationRequired;
            private set => this.RaiseAndSetIfChanged(ref _isRotationRequired, value);
        }

        private int _daysSinceRotation;
        public int DaysSinceRotation
        {
            get => _daysSinceRotation;
            private set => this.RaiseAndSetIfChanged(ref _daysSinceRotation, value);
        }

        private bool _isRotationBannerDismissed;
        public bool IsRotationBannerDismissed
        {
            get => _isRotationBannerDismissed;
            private set => this.RaiseAndSetIfChanged(ref _isRotationBannerDismissed, value);
        }

        public SecurityThreatLevel CurrentThreatLevel
        {
            get => _currentThreatLevel;
            private set => this.RaiseAndSetIfChanged(ref _currentThreatLevel, value);
        }

        public string SecurityStatus
        {
            get => _securityStatus;
            private set => this.RaiseAndSetIfChanged(ref _securityStatus, value);
        }

        public bool ShowSecurityAlert
        {
            get => _showSecurityAlert;
            private set => this.RaiseAndSetIfChanged(ref _showSecurityAlert, value);
        }

        private object? _selectedSettingsContent;
        public object? SelectedSettingsContent
        {
            get => _selectedSettingsContent;
            private set => this.RaiseAndSetIfChanged(ref _selectedSettingsContent, value);
        }

        // Identifier of the currently active settings pane (e.g. "general",
        // "security"). Drives the nav highlight in SettingsPanelOverlay via
        // StringEqualsConverter and lets callers (command palette, deep links)
        // re-enter a specific pane without inspecting SelectedSettingsContent.
        private string _selectedSettingsTab = "general";
        public string SelectedSettingsTab
        {
            get => _selectedSettingsTab;
            private set => this.RaiseAndSetIfChanged(ref _selectedSettingsTab, value);
        }

        // Commands
        public ReactiveCommand<string, Unit> OpenItemCommand { get; }
        public ReactiveCommand<Unit, Unit> LockCommand { get; }
        public ReactiveCommand<Unit, Unit> UnlockWithPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> UnlockWithPinCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowPassphraseFallbackCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseEditPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> AddCredentialCommand { get; }
        public ReactiveCommand<string, Unit> AddCredentialWithTypeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddLoginCredentialCommand { get; }
        public ReactiveCommand<Unit, Unit> AddPaymentCardCommand { get; }
        public ReactiveCommand<Unit, Unit> AddCreditCardCommand { get; }
        public ReactiveCommand<Unit, Unit> AddBankAccountCommand { get; }
        public ReactiveCommand<Unit, Unit> AddIdentityCommand { get; }
        public ReactiveCommand<Unit, Unit> AddSecureNoteCommand { get; }
        public ReactiveCommand<Unit, Unit> AddApiKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> AddSoftwareLicenseCommand { get; }
        public ReactiveCommand<Unit, Unit> AddWiFiPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> AddContactCommand { get; }
        public ReactiveCommand<Unit, Unit> AddPinCodeCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> EditCredentialCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> DeleteCredentialCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> CopyPasswordCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> CopyUsernameCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> CopyTotpCodeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddTotpCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> EditTotpCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenAttestorCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveTotpSecretCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveTotpCommand { get; }

        private void OpenAttestor()
        {
            if (_integratedAttestorService.TryLaunch(out var errorMessage))
            {
                DashboardViewModel.SetAttestorAvailability(true, _integratedAttestorService.AvailabilityMessage);
                StatusMessage = "Opened PhantomAttestor.";
            }
            else
            {
                var message = errorMessage ?? _integratedAttestorService.AvailabilityMessage;
                DashboardViewModel.SetAttestorAvailability(_integratedAttestorService.IsAvailable, message);
                StatusMessage = message;
            }
        }

        public string TotpSecretInput
        {
            get => _totpSecretInput;
            set => this.RaiseAndSetIfChanged(ref _totpSecretInput, value);
        }
        public ReactiveCommand<CredentialViewModel, Unit> OpenUrlCommand { get; }
        // When a credential tile is clicked (both list and grid views), select it to show in detail view
        public ReactiveCommand<CredentialViewModel, Unit> TileClickCommand { get; }
        public ReactiveCommand<CredentialViewModel, Unit> ToggleFavoriteCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDashboardCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAllCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowFavoritesCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowPasskeysCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRecentCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowExpiringSoonCommand { get; }
        public ReactiveCommand<string, Unit> SelectCategoryCommand { get; }
        public ReactiveCommand<string?, Unit> SetEntryTypeCommand { get; }
        public ReactiveCommand<string, Unit> FilterByEntryTypeCommand { get; }
        public ReactiveCommand<Unit, Unit> ManageCategoriesCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowFlaggedPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseFlaggedPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenPhantomRecoveryCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleRecoveryPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSecurityOptionsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenPasswordHealthPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> ClosePasswordHealthPanelCommand { get; }
        public ReactiveCommand<SecureTrashItemViewModel?, Unit> RecoverTrashItemCommand { get; }
        public ReactiveCommand<SecureTrashItemViewModel?, Unit> PurgeTrashItemCommand { get; }
        public ReactiveCommand<Unit, Unit> RestoreSelectedTrashCommand { get; }
        public ReactiveCommand<Unit, Unit> PurgeSelectedTrashCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleTrashSelectAllCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleViewModeCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleSidebarCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearSelectedCredentialCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenCommandPaletteCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSettingsPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseSettingsPanelCommand { get; }
        // Issue #21: drives the overlay-bottom Save / Discard row.
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> DiscardSettingsCommand { get; }
        // Issue #21/#38: kept for back-compat callers but bindings now go to
        // SettingsDraftTracker.HasUnsavedChanges directly via the property
        // path below, so the binding listens to the tracker's own
        // INotifyPropertyChanged instead of going through a proxy getter +
        // manual RaisePropertyChanged that could miss subsequent cycles.
        public bool HasUnsavedSettings => _settingsDraftTracker.HasUnsavedChanges;
        // Public path for XAML to bind directly: "SettingsDraftTracker.HasUnsavedChanges".
        // Re-emitting through a getter dropped the second-and-later cycles
        // when the manual WhenAnyValue subscription order interacted poorly
        // with Avalonia's binding evaluator. Direct binding is bulletproof.
        public PhantomVault.UI.Services.SettingsDraftTracker SettingsDraftTracker => _settingsDraftTracker;
        public ReactiveCommand<Unit, Unit> ToggleSecurityDashboardCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSecurityDashboardWindowCommand { get; }
        public ReactiveCommand<PhantomVault.Core.Models.Credential, Unit> OpenEditPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshSecurityDashboard { get; }
        public ReactiveCommand<Unit, Unit> CopySuggestedPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> FilterShowAllCommand { get; }
        public ReactiveCommand<Unit, Unit> FilterCriticalCommand { get; }
        public ReactiveCommand<Unit, Unit> FilterLowCommand { get; }
        public ReactiveCommand<Unit, Unit> FilterMediumCommand { get; }
        public ReactiveCommand<Unit, Unit> FilterGoodCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowGeneralSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSecuritySettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowThemeSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowImportExportSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAutoFillSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowBackupSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAccessibilitySettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdvancedSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRubbishBinSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowPasswordHealthCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSyncSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenIconManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseIconManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> RotateNowCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissRotationBannerCommand { get; }

        // Import/Export commands
        public ReactiveCommand<Unit, Unit> OpenImportWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCsvCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportKeePassCommand { get; }

        /// <summary>
        /// Snapshot of available command-palette actions, built fresh each
        /// time the palette opens so they reflect current vault state
        /// (selected credential, lock state, etc.). The View calls this when
        /// it builds the palette window.
        /// </summary>
        public IReadOnlyList<CommandPaletteAction> BuildCommandPaletteActions()
        {
            // Helper: fire-and-forget a Unit ReactiveCommand. Palette closes
            // before the action runs (see CommandPaletteWindow.OnClosed), so
            // any dialog the command pops up will be parented correctly.
            static void Run(ReactiveCommand<Unit, Unit> cmd) => cmd.Execute().Subscribe(_ => { }, _ => { });

            var list = new List<CommandPaletteAction>
            {
                // ── Vault ───────────────────────────────────────────────
                new("Lock vault now",            "Vault",    () => Run(LockCommand),
                    subtitle: "Clear keys and require keyfile + password again",
                    glyph: "🔒",
                    searchKeywords: "lock,logout,sign out,exit"),

                // ── Add ────────────────────────────────────────────────
                new("New login",                 "Add",      () => Run(AddLoginCredentialCommand),
                    glyph: "+", searchKeywords: "create,add,account,credential"),
                new("New credit card",           "Add",      () => Run(AddCreditCardCommand),
                    glyph: "💳", searchKeywords: "payment,visa,mastercard"),
                new("New secure note",           "Add",      () => Run(AddSecureNoteCommand),
                    glyph: "📝", searchKeywords: "note,text"),
                new("New identity",              "Add",      () => Run(AddIdentityCommand),
                    glyph: "🪪", searchKeywords: "id,passport,license"),
                new("New API key",               "Add",      () => Run(AddApiKeyCommand),
                    glyph: "🔑", searchKeywords: "token,secret"),
                new("New Wi-Fi password",        "Add",      () => Run(AddWiFiPasswordCommand),
                    glyph: "📶", searchKeywords: "wifi,wireless,network"),
                new("New software license",      "Add",      () => Run(AddSoftwareLicenseCommand),
                    glyph: "🪪", searchKeywords: "license,product key"),
                new("New bank account",          "Add",      () => Run(AddBankAccountCommand),
                    glyph: "🏦", searchKeywords: "bank,account,iban"),
                new("New contact",               "Add",      () => Run(AddContactCommand),
                    glyph: "👤", searchKeywords: "contact,address book"),

                // ── Navigate ───────────────────────────────────────────
                new("Show dashboard",            "Navigate", () => Run(ShowDashboardCommand),
                    glyph: "🏠", searchKeywords: "home,overview"),
                new("Show all credentials",      "Navigate", () => Run(ShowAllCommand),
                    glyph: "📋", searchKeywords: "list,everything"),
                new("Show passwords",            "Navigate", () => Run(ShowPasswordsCommand),
                    glyph: "🔑", searchKeywords: "logins"),
                new("Show favourites",           "Navigate", () => Run(ShowFavoritesCommand),
                    glyph: "⭐", searchKeywords: "starred,pinned"),
                new("Show passkeys",             "Navigate", () => Run(ShowPasskeysCommand),
                    glyph: "🗝️", searchKeywords: "fido,webauthn"),
                new("Show recently used",        "Navigate", () => Run(ShowRecentCommand),
                    glyph: "🕒", searchKeywords: "history"),
                new("Show expiring soon",        "Navigate", () => Run(ShowExpiringSoonCommand),
                    glyph: "⏳", searchKeywords: "expiry,renewal"),
                new("Show flagged passwords",    "Navigate", () => Run(ShowFlaggedPasswordsCommand),
                    glyph: "🚩", searchKeywords: "weak,duplicate,breached"),

                // ── Tools ───────────────────────────────────────────────
                new("Open password health",      "Tools",    () => Run(OpenPasswordHealthPanelCommand),
                    glyph: "🩺", searchKeywords: "audit,strength,scan,duplicates"),
                new("Toggle security dashboard", "Tools",    () => Run(ToggleSecurityDashboardCommand),
                    glyph: "🛡️", searchKeywords: "security,overview"),
                new("Open security options",    "Tools",    () => Run(OpenSecurityOptionsCommand),
                    glyph: "⚙️", searchKeywords: "security,options"),
                new("Manage categories",         "Tools",    () => Run(ManageCategoriesCommand),
                    glyph: "🗂️", searchKeywords: "tags,folders,labels"),
                new("Toggle view mode",          "Tools",    () => Run(ToggleViewModeCommand),
                    glyph: "🔁", searchKeywords: "grid,list,layout"),
                new("Toggle sidebar",            "Tools",    () => Run(ToggleSidebarCommand),
                    glyph: "↔️", searchKeywords: "panel,collapse"),

                // ── Theme ───────────────────────────────────────────────
                new("Toggle dark / light theme", "Theme",    () => Run(ToggleThemeCommand),
                    glyph: "🌓", searchKeywords: "appearance,dark mode,light mode"),
                new("Open theme settings",       "Theme",    () => Run(ShowThemeSettingsCommand),
                    glyph: "🎨", searchKeywords: "colour,style"),

                // ── Settings ───────────────────────────────────────────
                new("Open settings",             "Settings", () => Run(OpenSettingsPanelCommand),
                    glyph: "⚙️", searchKeywords: "preferences,options,config"),
                new("Accessibility settings",    "Settings", () => Run(ShowAccessibilitySettingsCommand),
                    glyph: "♿", searchKeywords: "a11y,motion,contrast"),
                new("Auto-fill settings",        "Settings", () => Run(ShowAutoFillSettingsCommand),
                    glyph: "✍️", searchKeywords: "autofill,browser,injection"),
                new("Backup settings",           "Settings", () => Run(ShowBackupSettingsCommand),
                    glyph: "💾", searchKeywords: "snapshot,export,restore"),
                new("Advanced settings",         "Settings", () => Run(ShowAdvancedSettingsCommand),
                    glyph: "🛠️", searchKeywords: "developer,advanced"),
                new("Rubbish bin settings",      "Settings", () => Run(ShowRubbishBinSettingsCommand),
                    glyph: "🗑️", searchKeywords: "trash,recycle"),

                // ── Import / Export ────────────────────────────────────
                new("Import from file…",         "Import",   () => Run(OpenImportWindowCommand),
                    glyph: "📥", searchKeywords: "import,keepass,csv,1password,bitwarden"),
                new("Export vault (encrypted)",  "Export",   () => Run(ExportVaultCommand),
                    glyph: "📤", searchKeywords: "backup,export"),
                new("Export vault to CSV",       "Export",   () => Run(ExportVaultCsvCommand),
                    glyph: "📤", searchKeywords: "csv,spreadsheet"),
                new("Export to KeePass",         "Export",   () => Run(ExportKeePassCommand),
                    glyph: "📤", searchKeywords: "keepass,kdbx"),

                // ── Recovery ───────────────────────────────────────────
                new("Open Phantom Recovery",     "Recovery", () => Run(OpenPhantomRecoveryCommand),
                    glyph: "🆘", searchKeywords: "recovery,phrase,backup"),
                new("Open trash manager",        "Recovery", () => Run(OpenTrashManagerCommand),
                    glyph: "🗑️", searchKeywords: "trash,undelete,restore"),
            };

            // Selection-aware actions — only meaningful with a credential selected
            var sel = SelectedCredential;
            if (sel is not null)
            {
                list.Insert(0, new CommandPaletteAction(
                    title: $"Copy password — {sel.Title}",
                    category: "Selection",
                    execute: () => CopyPasswordCommand.Execute(sel).Subscribe(_ => { }, _ => { }),
                    glyph: "📋",
                    shortcut: "Ctrl+Shift+C",
                    searchKeywords: "copy,password,clipboard"));
                list.Insert(1, new CommandPaletteAction(
                    title: $"Copy username — {sel.Title}",
                    category: "Selection",
                    execute: () => CopyUsernameCommand.Execute(sel).Subscribe(_ => { }, _ => { }),
                    glyph: "📋",
                    searchKeywords: "copy,username,clipboard"));
                if (sel.HasTotpSecret)
                {
                    list.Insert(2, new CommandPaletteAction(
                        title: $"Copy TOTP code — {sel.Title}",
                        category: "Selection",
                        execute: () => CopyTotpCodeCommand.Execute(sel).Subscribe(_ => { }, _ => { }),
                        glyph: "🔢",
                        searchKeywords: "totp,otp,2fa,code"));
                }
            }

            return list;
        }

        // Private methods
        private void InitializeCategories()
        {
            _categories.Add(new CategoryViewModel { Name = "Logins", Icon = IconPathMigrator.LoginsIcon, Count = 0, TileColor = "#FDE68A" });
            _categories.Add(new CategoryViewModel { Name = "Credit Cards", Icon = IconPathMigrator.PaymentIcon, Count = 0, TileColor = "#A7F3D0" });
            _categories.Add(new CategoryViewModel { Name = "Secure Notes", Icon = IconPathMigrator.NotesIcon, Count = 0, TileColor = "#BFDBFE" });
            _categories.Add(new CategoryViewModel { Name = "Banking", Icon = IconPathMigrator.BankingIcon, Count = 0, TileColor = "#93C5FD" });
            _categories.Add(new CategoryViewModel { Name = "Personal", Icon = IconPathMigrator.PersonalIcon, Count = 0, TileColor = "#E9D5FF" });
            _categories.Add(new CategoryViewModel { Name = "WiFi", Icon = IconPathMigrator.WiFiIcon, Count = 0, TileColor = "#7DD3FC" });
            _categories.Add(new CategoryViewModel { Name = "ID", Icon = IconPathMigrator.IdIcon, Count = 0, TileColor = "#6EE7B7" });
            _categories.Add(new CategoryViewModel { Name = "Notes", Icon = IconPathMigrator.NotesIcon, Count = 0, TileColor = "#FCA5A5" });
            _categories.Add(new CategoryViewModel { Name = "Custom", Icon = IconPathMigrator.CustomIcon, Count = 0, TileColor = "#C4B5FD" });
            _categories.Add(new CategoryViewModel { Name = "Secure Rubbish Bin", Icon = IconPathMigrator.TrashIcon, Count = _secureTrashService.Records.Count, TileColor = "#6B7280" });
        }

        /// <summary>
        /// Attempts to read category colors from the vault manifest and apply them to the sidebar categories.
        /// Called during vault initialization so colours are visible immediately.
        /// </summary>
        private void LoadCategoryColorsFromManifest()
        {
            try
            {
                string? manifestPath = _manifestPath;
                if (string.IsNullOrEmpty(manifestPath) && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.pvault");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }
                if (string.IsNullOrEmpty(manifestPath) && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.manifest");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }
                if (string.IsNullOrEmpty(manifestPath) || !System.IO.File.Exists(manifestPath)) return;

                if (!_manifestService.TryReadManifest(manifestPath, null, null, out var manifest, out _))
                    return;

                if (manifest?.Categories == null || manifest.Categories.Count == 0) return;

                foreach (var cat in _categories)
                {
                    var manifestCat = manifest.Categories.FirstOrDefault(m =>
                        string.Equals(m.Name, cat.Name, StringComparison.OrdinalIgnoreCase));
                    if (manifestCat != null && !string.IsNullOrWhiteSpace(manifestCat.TileColor))
                    {
                        cat.TileColor = manifestCat.TileColor;
                    }
                }
            }
            catch
            {
                // Silently fail - category colors are cosmetic
            }
        }

        private void LoadSampleCredentials()
        {
            // Sample credentials for demonstration
            var sampleCreds = new List<Credential>
            {
                new Credential
                {
                    Title = "GitHub",
                    Username = "user@example.com",
                    Password = "SecurePass123!",
                    Url = "https://github.com",
                    Group = "Logins",
                    EntryType = EntryType.Password,
                    TotpSecret = "JBSWY3DPEHPK3PXP",
                    TotpIssuer = "GitHub",
                    TotpAccountName = "user@example.com",
                    TotpDigits = 6,
                    TotpTimeStep = 30,
                    TotpAlgorithm = "SHA1",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-3)
                },
                new Credential
                {
                    Title = "Gmail",
                    Username = "myemail@gmail.com",
                    Password = "AnotherPass456!",
                    Url = "https://gmail.com",
                    Group = "Logins",
                    EntryType = EntryType.Password,
                    TotpSecret = "HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ",
                    TotpIssuer = "Google",
                    TotpAccountName = "myemail@gmail.com",
                    TotpDigits = 6,
                    TotpTimeStep = 30,
                    TotpAlgorithm = "SHA1",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-1)
                },
                new Credential
                {
                    Title = "Bank of America",
                    Username = "account123",
                    Password = "BankPass789!",
                    Url = "https://bankofamerica.com",
                    Group = "Banking",
                    EntryType = EntryType.BankAccount,
                    Notes = "Main checking account",
                    BankName = "Bank of America",
                    BankAccountNumber = "****4321",
                    BankRoutingNumber = "026009593",
                    BankAccountType = "Checking",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-6)
                },
                new Credential
                {
                    Title = "Amazon",
                    Username = "shopper@example.com",
                    Password = "ShopPass321!",
                    Url = "https://amazon.com",
                    Group = "Logins",
                    EntryType = EntryType.Password,
                    CreatedUtc = DateTimeOffset.Now.AddDays(-15)
                },
                new Credential
                {
                    Title = "Netflix",
                    Username = "viewer@example.com",
                    Password = "StreamPass999!",
                    Url = "https://netflix.com",
                    Group = "Personal",
                    EntryType = EntryType.Password,
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-2)
                },
                new Credential
                {
                    Title = "Visa Platinum",
                    Group = "Credit Cards",
                    EntryType = EntryType.CreditCard,
                    CardholderName = "Alex Rivera",
                    CardNumber = "4111 1111 1111 1111",
                    CardType = "Visa",
                    CardExpiryMonth = "08",
                    CardExpiryYear = DateTimeOffset.Now.AddYears(2).Year.ToString(),
                    CardCVV = "842",
                    CardBillingAddress = "1452 Spruce St, Boston, MA",
                    Notes = "Travel rewards card",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-4)
                },
                new Credential
                {
                    Title = "Home WiFi",
                    Group = "WiFi",
                    EntryType = EntryType.WiFi,
                    WiFiSSID = "Rivera-5G",
                    WiFiSecurityType = "WPA3",
                    WiFiBSSID = "00:0a:f5:12:8c:ff",
                    Password = "StaySecure!2024",
                    Notes = "Router closet password label",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-8)
                },
                new Credential
                {
                    Title = "Passport - Alex Rivera",
                    Group = "ID",
                    EntryType = EntryType.Identity,
                    IdDocumentType = "Passport",
                    IdNumber = "N12345678",
                    IdIssuingCountry = "USA",
                    IdIssueDate = DateTimeOffset.Now.AddYears(-6),
                    IdExpiryDate = DateTimeOffset.Now.AddYears(4),
                    Notes = "Keep digital copy synced",
                    CreatedUtc = DateTimeOffset.Now.AddYears(-6)
                },
                new Credential
                {
                    Title = "Azure Production API",
                    Group = "Secure Notes",
                    EntryType = EntryType.ApiKey,
                    ApiKeyValue = "AZR-PROD-89F3-4B2C-AD21",
                    ApiEndpoint = "https://api.contoso.com/v1",
                    ApiEnvironment = "Production",
                    ApiDocumentationUrl = "https://docs.contoso.com/api",
                    Notes = "Rotate monthly",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-5)
                },
                new Credential
                {
                    Title = "Emergency Contact - Taylor Lee",
                    Group = "Personal",
                    EntryType = EntryType.Contact,
                    ContactFullName = "Taylor Lee",
                    ContactEmail = "taylor.lee@example.com",
                    ContactPhone = "+1 (555) 212-7744",
                    ContactAddress = "510 Lakeview Ave, Denver, CO",
                    ContactCompany = "Summit Robotics",
                    ContactJobTitle = "Security Lead",
                    Notes = "Handles recovery approvals",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-10)
                },
                new Credential
                {
                    Title = "GitHub 2FA Authenticator",
                    Group = "Notes",
                    EntryType = EntryType.TotpGenerator,
                    TotpSecret = "JBSWY3DPEHPK3PXP",
                    TotpIssuer = "GitHub",
                    TotpAccountName = "phantom.dev@example.com",
                    TotpDigits = 6,
                    TotpTimeStep = 30,
                    TotpAlgorithm = "SHA1",
                    Username = "phantom.dev@example.com",
                    Url = "https://github.com",
                    Notes = "Primary GitHub account two-factor authentication",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-3)
                },
                new Credential
                {
                    Title = "Slack",
                    Username = "alex.rivera@summit.io",
                    Password = "Sl@ck!Secure77",
                    Url = "https://summitrobotics.slack.com",
                    Group = "Logins",
                    EntryType = EntryType.Password,
                    Notes = "Team workspace - Summit Robotics",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-2)
                },
                new Credential
                {
                    Title = "Discord",
                    Username = "phantomdev#4291",
                    Password = "D!sc0rd_P@ss2025",
                    Url = "https://discord.com",
                    Group = "Personal",
                    EntryType = EntryType.Password,
                    CreatedUtc = DateTimeOffset.Now.AddDays(-45)
                },
                new Credential
                {
                    Title = "AWS Console",
                    Group = "Secure Notes",
                    EntryType = EntryType.ApiKey,
                    ApiKeyValue = "AKIAIOSFODNN7EXAMPLE",
                    ApiKeyType = "API Key",
                    ApiEndpoint = "https://console.aws.amazon.com",
                    ApiEnvironment = "Production",
                    ApiDocumentationUrl = "https://docs.aws.amazon.com",
                    TotpSecret = "KSQL4VTBKGO3XVCYNMQQ",
                    TotpIssuer = "Amazon Web Services",
                    TotpAccountName = "alex@summit.io",
                    TotpDigits = 6,
                    TotpTimeStep = 30,
                    TotpAlgorithm = "SHA1",
                    Notes = "Root account - use IAM for daily access",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-7)
                },
                new Credential
                {
                    Title = "Mastercard Gold",
                    Group = "Credit Cards",
                    EntryType = EntryType.CreditCard,
                    CardholderName = "Alex Rivera",
                    CardNumber = "5425 2334 3010 9903",
                    CardType = "Mastercard",
                    CardExpiryMonth = "03",
                    CardExpiryYear = DateTimeOffset.Now.AddYears(3).Year.ToString(),
                    CardCVV = "517",
                    CardBillingAddress = "1452 Spruce St, Boston, MA",
                    Notes = "Everyday purchases",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-9)
                },
                new Credential
                {
                    Title = "Driver Licence - Alex Rivera",
                    Group = "ID",
                    EntryType = EntryType.Identity,
                    IdDocumentType = "Driver Licence",
                    IdNumber = "D12-345-6789",
                    IdIssuingCountry = "USA",
                    IdIssuingState = "Massachusetts",
                    IdIssueDate = DateTimeOffset.Now.AddYears(-4),
                    IdExpiryDate = DateTimeOffset.Now.AddYears(1),
                    Notes = "Renew before expiry",
                    CreatedUtc = DateTimeOffset.Now.AddYears(-4)
                }
            };

            foreach (var cred in sampleCreds)
            {
                var vm = new CredentialViewModel(cred);
                // Make first two favorites for demo
                if (_credentials.Count < 2)
                {
                    vm.IsFavorite = true;
                }
                _credentials.Add(vm);
            }

            UpdateCategoryCounts();
            // Don't apply filters if dashboard is showing - let it stay on dashboard
            if (!IsShowingDashboard)
            {
                ApplyFilters();
            }
        }

        private void UpdateCategoryCounts()
        {
            foreach (var category in _categories)
            {
                if (string.Equals(category.Name, "Secure Rubbish Bin", StringComparison.OrdinalIgnoreCase))
                {
                    category.Count = _secureTrashService.Records.Count;
                }
                else
                {
                    category.Count = _credentials.Count(c => c.Group == category.Name);
                }
            }
        }

        private void ApplyFilters()
        {
            if (IsShowingSecureTrash)
            {
                ApplyTrashFilters();
                UpdateFlaggedCredentials();
                return;
            }

            // Exclude passkeys from regular credentials view
            var filtered = _credentials.Where(c => !c.GetCredential().IsPasskey).AsEnumerable();

            // Apply entry type filter if specified
            if (_currentEntryType.HasValue)
            {
                filtered = filtered.Where(c => c.GetCredential().EntryType == _currentEntryType.Value);
            }

            if (!string.IsNullOrEmpty(_activeCategory))
            {
                filtered = filtered.Where(c => string.Equals(c.Group, _activeCategory, StringComparison.OrdinalIgnoreCase));
            }

            // Apply search filter with fuzzy matching
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchResults = new List<Services.SearchResult<CredentialViewModel>>();

                foreach (var credential in filtered)
                {
                    // Calculate scores for different fields
                    int titleScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Title);
                    int usernameScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Username);
                    int groupScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Group);
                    int urlScore = !string.IsNullOrEmpty(credential.Url)
                        ? Services.FuzzyMatcher.CalculateScore(SearchText, credential.Url)
                        : 0;

                    // Use highest score
                    int maxScore = Math.Max(Math.Max(titleScore, usernameScore), Math.Max(groupScore, urlScore));

                    if (maxScore >= 30) // Threshold for match
                    {
                        searchResults.Add(new Services.SearchResult<CredentialViewModel>(credential)
                        {
                            Score = maxScore,
                            MatchedField = maxScore == titleScore ? "Title" :
                                          maxScore == usernameScore ? "Username" :
                                          maxScore == groupScore ? "Group" : "URL"
                        });
                    }
                }

                // Sort by score descending, then by title
                filtered = searchResults
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.Item.Title)
                    .Select(r => r.Item);
            }

            // Apply view filter
            if (IsShowingFavorites)
            {
                filtered = filtered.Where(c => c.IsFavorite);
            }
            else if (IsShowingRecent)
            {
                filtered = filtered.OrderByDescending(c => c.LastUpdatedUtc).Take(10);
            }
            else if (IsShowingExpiringSoon)
            {
                var now = DateTimeOffset.UtcNow;
                var threshold = now.AddDays(30);
                filtered = filtered.Where(c =>
                {
                    var cred = c.GetCredential();
                    return cred.ExpiryUtc.HasValue &&
                           cred.ExpiryUtc.Value > now &&
                           cred.ExpiryUtc.Value <= threshold;
                }).OrderBy(c => c.GetCredential().ExpiryUtc);
            }

            // Apply sorting
            filtered = SortOption switch
            {
                0 => filtered.OrderBy(c => c.Title), // Name A-Z
                1 => filtered.OrderByDescending(c => c.Title), // Name Z-A
                2 => filtered.OrderByDescending(c => c.CreatedUtc), // Recently Added
                3 => filtered.OrderByDescending(c => c.LastUpdatedUtc), // Recently Modified
                4 => filtered.OrderBy(c => c.Group).ThenBy(c => c.Title), // Category
                _ => filtered
            };

            // Materialize the filtered collection before posting to UI thread
            var materializedList = filtered.ToList();

            // Build grouped list with category headers when sorted by category
            var groupedItems = new List<ListItemWrapper>();
            if (SortOption == 4) // Category sort
            {
                var grouped = materializedList.GroupBy(c => c.Group ?? "Uncategorized");
                foreach (var group in grouped)
                {
                    // Find category color
                    var categoryVm = _categories.FirstOrDefault(cat =>
                        string.Equals(cat.Name, group.Key, StringComparison.OrdinalIgnoreCase));
                    var categoryColor = categoryVm?.TileColor;

                    // Add category header
                    groupedItems.Add(ListItemWrapper.CreateCategoryHeader(group.Key, categoryColor, group.Count()));

                    // Add credentials in this category
                    foreach (var cred in group)
                    {
                        groupedItems.Add(ListItemWrapper.CreateCredential(cred));
                    }
                }
            }
            else
            {
                // No grouping - just wrap credentials
                foreach (var cred in materializedList)
                {
                    groupedItems.Add(ListItemWrapper.CreateCredential(cred));
                }
            }

            UpdateFlaggedCredentials();

            Dispatcher.UIThread.Post(() =>
            {
                // Replace entire collection to force UI rebind
                FilteredCredentials = new ObservableCollection<CredentialViewModel>(materializedList);
                GroupedListItems = new ObservableCollection<ListItemWrapper>(groupedItems);

                // Also raise property change notifications
                this.RaisePropertyChanged(nameof(FilteredCount));
                this.RaisePropertyChanged(nameof(IsEmpty));
                this.RaisePropertyChanged(nameof(HasTrashSelection));
                this.RaisePropertyChanged(nameof(TotalCount));
                this.RaisePropertyChanged(nameof(IsSortedByCategory));
            });
        }

        private void ApplyTrashFilters()
        {
            var previousSelectionId = SelectedTrashItem?.Record.Id;
            var selectionMap = FilteredTrashItems.ToDictionary(item => item.Record.Id, item => item.IsSelected);

            var records = _secureTrashService.Records.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                records = records.Where(r =>
                {
                    var payload = r.Payload ?? new Credential();
                    return (!string.IsNullOrEmpty(payload.Title) && payload.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(payload.Username) && payload.Username.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(payload.Url) && payload.Url.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(r.OriginalGroup) && r.OriginalGroup.Contains(search, StringComparison.OrdinalIgnoreCase));
                });
            }

            var items = records
                .Select(record =>
                {
                    var viewModel = new SecureTrashItemViewModel(record);
                    if (selectionMap.TryGetValue(record.Id, out var isSelected))
                    {
                        viewModel.IsSelected = isSelected;
                    }
                    return viewModel;
                })
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var item in FilteredTrashItems)
                {
                    item.PropertyChanged -= TrashItemOnPropertyChanged;
                }

                FilteredTrashItems = new ObservableCollection<SecureTrashItemViewModel>(items);

                foreach (var item in FilteredTrashItems)
                {
                    item.PropertyChanged += TrashItemOnPropertyChanged;
                }

                if (previousSelectionId.HasValue)
                {
                    SelectedTrashItem = FilteredTrashItems.FirstOrDefault(i => i.Record.Id == previousSelectionId.Value);
                }

                if (SelectedTrashItem == null)
                {
                    SelectedTrashItem = FilteredTrashItems.FirstOrDefault();
                }

                this.RaisePropertyChanged(nameof(FilteredCount));
                this.RaisePropertyChanged(nameof(IsEmpty));
                this.RaisePropertyChanged(nameof(HasTrashSelection));
            });
        }

        private void ToggleTrashSelection()
        {
            if (!FilteredTrashItems.Any())
            {
                return;
            }

            var shouldSelectAll = FilteredTrashItems.Any(item => !item.IsSelected);
            foreach (var item in FilteredTrashItems)
            {
                item.IsSelected = shouldSelectAll;
            }

            this.RaisePropertyChanged(nameof(HasTrashSelection));
        }

        private bool TryRestoreTrashRecord(SecureTrashItemViewModel? item, out Credential? credential)
        {
            credential = null;
            if (item == null)
            {
                return false;
            }

            var result = _secureTrashService.Restore(item.Record.Id);
            if (!result.success || result.credential == null)
            {
                return false;
            }

            var restored = result.credential;
            restored.Group = !string.IsNullOrEmpty(result.originalGroup) ? result.originalGroup! : "Unsorted";
            restored.LastUpdatedUtc = DateTimeOffset.UtcNow;

            var vm = new CredentialViewModel(restored);
            _credentials.Add(vm);

            credential = restored;
            return true;
        }

        private async Task RestoreTrashItemAsync(SecureTrashItemViewModel? item)
        {
            if (!TryRestoreTrashRecord(item, out _))
            {
                await _dialogService.ShowErrorAsync("Restore Failed", "Unable to restore the selected item.", _ownerWindow);
                return;
            }

            UpdateCategoryCounts();
            ApplyFilters();
            await SaveVaultAsync();
            if (item != null)
            {
                StatusMessage = $"Restored '{item.Title}'";
            }
        }

        private async Task RestoreSelectedTrashAsync()
        {
            var selected = FilteredTrashItems.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                await _dialogService.ShowInfoAsync("Restore Selected", "No items selected to restore.", _ownerWindow);
                return;
            }

            var confirm = await _dialogService.ShowConfirmationAsync(
                "Restore Selected",
                $"Restore {selected.Count} selected item{(selected.Count == 1 ? string.Empty : "s")} to a category?",
                _ownerWindow);

            if (!confirm)
            {
                return;
            }

            // Ask target category
            var categories = _categories
                .Where(c => !string.Equals(c.Name, "Secure Rubbish Bin", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

            var target = await _dialogService.ShowSelectCategoryAsync(categories, "Restore selected items to category:", _ownerWindow);
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            var restoredCount = 0;
            foreach (var item in selected)
            {
                var restored = _secureTrashService.Restore(item.Record.Id);
                if (restored.success && restored.credential != null)
                {
                    restored.credential.Group = target;
                    _credentials.Add(new CredentialViewModel(restored.credential));
                    restoredCount++;
                }
            }

            if (restoredCount > 0)
            {
                UpdateCategoryCounts();
                ApplyFilters();
                await _dialogService.ShowInfoAsync("Restored", $"Restored {restoredCount} item{(restoredCount == 1 ? string.Empty : "s")} to '{target}'.", _ownerWindow);
                await SaveVaultAsync();
                _undoStack.Push(("Restore", (object)(target, selected.Select(s => s.Record.Payload).ToList())));
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

        private async Task PurgeTrashItemAsync(SecureTrashItemViewModel? item)
        {
            if (item == null) return;

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Securely Delete",
                $"Permanently delete '{item.Title}'? This cannot be undone.",
                _ownerWindow);

            if (!confirmed) return;

            if (_secureTrashService.SecurelyPurge(item.Record.Id))
            {
                UpdateCategoryCounts();
                ApplyFilters();
                StatusMessage = $"Securely deleted '{item.Title}'";
                _undoStack.Push(("Delete", (object)item.Record));
                await SaveVaultAsync();
            }
            else
            {
                await _dialogService.ShowErrorAsync("Delete Failed", "Unable to securely delete the selected item.", _ownerWindow);
            }
        }

        private async Task PurgeSelectedTrashAsync()
        {
            var selected = FilteredTrashItems.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Securely Delete Selected",
                $"Permanently delete {selected.Count} selected item{(selected.Count == 1 ? string.Empty : "s")}? This cannot be undone.",
                _ownerWindow);

            if (!confirmed)
            {
                return;
            }

            var purgedCount = 0;
            foreach (var item in selected)
            {
                if (_secureTrashService.SecurelyPurge(item.Record.Id))
                {
                    purgedCount++;
                }
            }

            if (purgedCount > 0)
            {
                UpdateCategoryCounts();
                ApplyFilters();
                StatusMessage = purgedCount == 1 ? "Securely deleted 1 item" : $"Securely deleted {purgedCount} items";
            }
            else
            {
                await _dialogService.ShowErrorAsync("Delete Failed", "No items were securely deleted.", _ownerWindow);
            }
        }

        private void TrashItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SecureTrashItemViewModel.IsSelected))
            {
                this.RaisePropertyChanged(nameof(HasTrashSelection));
            }
        }

        public Task ShowAddCredentialDialogAsync()
        {
            return AddCredentialAsync();
        }

        private async Task AddCredentialAsync()
        {
            // Default to Password type
            await AddCredentialWithTypeAsync("0");
        }

        private async Task AddCredentialWithTypeAsync(string entryTypeValue)
        {
            Core.Models.EntryType entryType = Core.Models.EntryType.Password;
            var isSecureNote = false;

            // Map string names to EntryType enum
            if (!string.IsNullOrEmpty(entryTypeValue))
            {
                if (int.TryParse(entryTypeValue, out int typeInt) && Enum.IsDefined(typeof(Core.Models.EntryType), typeInt))
                {
                    entryType = (Core.Models.EntryType)typeInt;
                }
                else
                {
                    // Map friendly names to enum values
                    var lowerValue = entryTypeValue.ToLowerInvariant();
                    switch (lowerValue)
                    {
                        case "login":
                        case "password":
                            entryType = Core.Models.EntryType.Password;
                            break;
                        case "payment card":
                        case "credit card":
                        case "card":
                            entryType = Core.Models.EntryType.CreditCard;
                            break;
                        case "bank account":
                        case "bank":
                            entryType = Core.Models.EntryType.BankAccount;
                            break;
                        case "identity":
                        case "id":
                            entryType = Core.Models.EntryType.Identity;
                            break;
                        case "secure note":
                        case "note":
                            entryType = Core.Models.EntryType.Password; // Notes use Password type with empty password
                            isSecureNote = true;
                            break;
                        case "software license":
                        case "license":
                        case "api key":
                        case "api":
                        case "sdk":
                        case "sdk key":
                        case "secret":
                        case "token":
                        case "private key":
                        case "public key":
                            entryType = Core.Models.EntryType.ApiKey;
                            break;
                        case "wifi password":
                        case "wifi":
                            entryType = Core.Models.EntryType.WiFi;
                            break;
                        case "contact":
                        case "personal info":
                        case "personal information":
                            entryType = Core.Models.EntryType.Contact;
                            break;
                        case "pin code":
                        case "pin":
                            entryType = Core.Models.EntryType.PinCode;
                            break;
                        default:
                            entryType = Core.Models.EntryType.Password;
                            break;
                    }
                }
            }

            // Create a new credential with the specified type
            var newCredential = new Credential
            {
                EntryType = entryType
            };

            if (isSecureNote)
            {
                newCredential.Group = "Secure Notes";
            }

            // Open overlay panel for adding new credentials
            CloseAllOverlays(); // Close any other open overlays
            EditViewModel = new AddEditCredentialViewModel(newCredential, (credential) =>
            {
                OnCredentialSaved(credential);
                CloseEditPanel();
            });

            IsEditPanelVisible = true;

            // Set the entry type filter to match what we're adding
            SetEntryType(entryTypeValue);
            StatusMessage = $"Adding new {entryTypeValue ?? entryType.ToString()} entry";

            await Task.CompletedTask;
        }

        private async Task EditCredentialAsync(CredentialViewModel credentialVm)
        {
            if (credentialVm == null)
            {
                return;
            }

            // Keep detail panel aligned with whichever tile triggered the edit action
            SelectedCredential = credentialVm;

            var editingTitle = credentialVm.Title;
            try
            {
                Debug.WriteLine($"[EDIT] Opening credential: {editingTitle}");
            }
            catch
            {
                // Diagnostics only
            }

            StatusMessage = string.IsNullOrWhiteSpace(editingTitle)
                ? "Editing credential"
                : $"Editing: {editingTitle}";

            // Open overlay panel for editing instead of dialog
            CloseAllOverlays(); // Close any other open overlays
            EditViewModel = new AddEditCredentialViewModel(credentialVm.GetCredential(), (credential) =>
            {
                OnCredentialSaved(credential);
                CloseEditPanel();
            });

            IsEditPanelVisible = true;

            // Window-based editing removed - now using overlay panel
            await Task.CompletedTask;
        }

        public void CloseEditPanel()
        {
            IsEditPanelVisible = false;
            EditViewModel = null;
        }

        /// <summary>
        /// Closes all overlay panels to ensure only one is visible at a time
        /// </summary>
        private void CloseAllOverlays()
        {
            IsEditPanelVisible = false;
            IsCategoryManagerPanelVisible = false;
            IsPasswordHealthPanelVisible = false;
            IsFlaggedPanelVisible = false;
            _isRecoveryPanelVisible = false;
            IsSettingsPanelVisible = false;
        }

        private void OnCredentialSaved(Credential credential)
        {
            // Check if this is an existing credential being edited
            var existing = _credentials.FirstOrDefault(c => c.GetCredential() == credential);

            if (existing != null)
            {
                // Update existing credential
                var index = _credentials.IndexOf(existing);
                var updatedVm = new CredentialViewModel(credential);
                _credentials[index] = updatedVm;
                SelectedCredential = updatedVm;
                StatusMessage = $"Updated: {credential.Title}";
            }
            else
            {
                // Add new credential
                var newVm = new CredentialViewModel(credential);
                _credentials.Add(newVm);
                SelectedCredential = newVm;
                StatusMessage = $"Added: {credential.Title}";
            }

            // Update UI
            UpdateCategoryCounts();
            ApplyFilters();

            // Persist changes to the encrypted vault database
            _ = SaveVaultAsync();
        }

        private async Task DeleteCredentialAsync(CredentialViewModel credential)
        {
            var secureEnabled = _secureTrashService.IsEnabled;
            var retentionDays = _secureTrashService.RetentionDays;
            var message = secureEnabled
                ? $"Are you sure you want to delete '{credential.Title}'?\n\nThe entry will move to the Secure Rubbish Bin and be purged after {retentionDays} day{(retentionDays == 1 ? string.Empty : "s")} unless you restore it."
                : $"Are you sure you want to delete '{credential.Title}'?\n\nSecure deletion will run immediately and cannot be undone.";

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Delete Credential",
                message,
                _ownerWindow);

            if (!confirmed)
            {
                return;
            }

            var model = credential.GetCredential();
            var record = _secureTrashService.MoveToTrash(model);

            Dispatcher.UIThread.Post(() =>
            {
                _credentials.Remove(credential);
                UpdateCategoryCounts();
                ApplyFilters();
                StatusMessage = secureEnabled
                    ? $"Moved to Secure Rubbish Bin: {credential.Title}"
                    : $"Securely deleted: {credential.Title}";
            });

            if (secureEnabled)
            {
                _undoStack.Push(("Delete", (object)record));
            }

            // Persist vault after deletion (credential already removed from collection)
            await SaveVaultAsync();
        }

        /// <summary>
        /// Persists the in-memory credentials to the encrypted vault.pvault file.
        /// This writes to a temporary plaintext file and uses the zero-knowledge
        /// service to encrypt it into the mounted container.
        /// </summary>
        private async Task SaveVaultAsync()
        {
            try
            {
                // CRITICAL: If decoy vault is active, silently ignore all write operations
                // This prevents attackers from modifying the real vault while decoy is active
                if (_tamperDetectionService?.IsDecoyActive == true && _decoyVaultService?.IsDecoyActive == true)
                {
                    // Simulate write delay to make decoy appear realistic
                    await Task.Delay(Random.Shared.Next(50, 150));
                    return;
                }

                if (string.IsNullOrEmpty(_mountPath) || string.IsNullOrEmpty(_vaultFilePath))
                    return;

                // Build the database object
                var database = new VaultDatabase
                {
                    Version = "2.0",
                    EncryptionType = "ZeroKnowledge-VaultFileZk",
                    Created = System.DateTime.UtcNow,
                    VaultName = _vaultName,
                    Description = "Saved by PhantomVault",
                    Groups = new System.Collections.Generic.List<VaultGroup>()
                };

                // Group credentials by Group name
                var grouped = _credentials.Select(c => c.GetCredential()).GroupBy(c => c.Group ?? "General");
                foreach (var g in grouped)
                {
                    var vg = new VaultGroup
                    {
                        Id = System.Guid.NewGuid().ToString("N"),
                        Name = g.Key,
                        Icon = "folder",
                        Entries = g.ToList()
                    };
                    database.Groups.Add(vg);
                }

                // Serialize to JSON
                var json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = true });

                // Encrypt directly from memory to avoid writing plaintext to disk.
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                try
                {
                    await using var ms = new MemoryStream(bytes, writable: false);
                    if (IsPhantomContainerFile(_vaultFilePath!))
                    {
                        await using var encryptedPayload = new MemoryStream();
                        await _zkVaultService.EncryptStreamToStreamAsync(ms, encryptedPayload);
                        encryptedPayload.Position = 0;
                        await _vaultService.SaveVaultPayloadAsync(
                            _vaultFilePath!,
                            encryptedPayload,
                            _vaultPassword,
                            _vaultKeyfilePath).ConfigureAwait(false);
                    }
                    else
                    {
                        await _zkVaultService.EncryptStreamAsync(ms, _vaultFilePath!);
                    }

                    await PersistCredentialIndexAsync(false);
                    await FlushTransportArtifactsAsync();

                    StatusMessage = "Vault saved";
                }
                finally
                {
                    // Zero the plaintext bytes after encryption (best-effort, should never fail)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Save Failed", $"Failed to save vault: {ex.Message}", _ownerWindow);
                StatusMessage = "Failed to save vault";
            }
        }

        private async Task PersistCredentialIndexAsync(bool repackMasterVolume)
        {
            if (_isPersistingCredentialIndex || string.IsNullOrWhiteSpace(_mountPath) || !Directory.Exists(_mountPath))
                return;

            _isPersistingCredentialIndex = true;
            try
            {
                await _obscuraCredentialIndexService.ExportAsync(
                    _mountPath,
                    _vaultName,
                    _credentials.Select(static credential => credential.GetCredential())).ConfigureAwait(false);

                if (repackMasterVolume)
                {
                    await FlushTransportArtifactsAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultViewModel] Failed to persist Obscura credential index: {ex.Message}");
            }
            finally
            {
                _isPersistingCredentialIndex = false;
            }
        }

        private async Task FlushTransportArtifactsAsync()
        {
            if (string.IsNullOrWhiteSpace(_transportLayoutRoot) || !Directory.Exists(_transportLayoutRoot))
                return;

            if (_effectiveStorageTransport == VaultStorageTransport.RawDevice &&
                !string.IsNullOrWhiteSpace(_usbRootPath) &&
                _usbRootPath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
            {
                await _blackSecureRawVolumeService.CreateVolumeFromDirectoryAsync(_usbRootPath, _transportLayoutRoot).ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_masterVolumePath))
            {
                var obscuraVolumeService = new ObscuraVolumeService();
                await obscuraVolumeService.CreateVolumeFromDirectoryAsync(_masterVolumePath, _transportLayoutRoot).ConfigureAwait(false);
            }
        }

        private async Task CopyPasswordAsync(CredentialViewModel credential)
        {
            try
            {
                _idleLockService.Reset();
                // Check clipboard guard
                if (_clipboardGuard != null && !_clipboardGuard.CanCopy())
                {
                    await _dialogService.ShowWarningAsync(
                        "Clipboard Blocked",
                        "Too many clipboard operations detected. Please wait before copying again.",
                        _ownerWindow);
                    StatusMessage = "Clipboard temporarily blocked";
                    return;
                }

                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard != null)
                {
                    // Choose the most sensitive field per entry type
                    var secret = credential.EntryType switch
                    {
                        EntryType.WiFi => credential.WiFiPassword,
                        EntryType.ApiKey => credential.ApiKeyValue,
                        EntryType.CreditCard => string.IsNullOrWhiteSpace(credential.CardCVV)
                            ? credential.CardPIN
                            : credential.CardCVV,
                        EntryType.BankAccount => credential.BankAccountNumber,
                        EntryType.TotpGenerator => credential.CurrentTotpCode,
                        EntryType.PinCode => credential.PinValue,
                        _ => credential.Password
                    };

                    if (string.IsNullOrWhiteSpace(secret))
                    {
                        StatusMessage = "Nothing to copy";
                        return;
                    }

                    // Check if we should auto-copy TOTP with password  
                    var settings = SettingsService.Load();
                    var autoCopyTotp = settings.AutoCopyTotpWithPassword;
                    var totpCopied = false;

                    if (autoCopyTotp && credential.EntryType == EntryType.Password && credential.HasTotpSecret && !string.IsNullOrWhiteSpace(credential.CurrentTotpCode))
                    {
                        // Append TOTP code to password
                        secret = $"{secret}\nTOTP: {credential.CurrentTotpCode}";
                        totpCopied = true;
                    }

                    await clipboard.SetTextAsync(secret);
                    StatusMessage = totpCopied
                        ? $"Password and TOTP copied for: {credential.Title}"
                        : $"Secret copied for: {credential.Title}";

                    // Register copy with guard
                    _clipboardGuard?.RegisterCopy(credential.Title);

                    // Cancel any previous clipboard clear timer so it doesn't wipe the new content
                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

                    // Schedule clipboard clearing based on user settings
                    var clearDelay = settings.GetClipboardClearDelay();
                    if (clearDelay.HasValue)
                    {
                        var copiedSecret = secret; // Capture actual copied value
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(clearDelay.Value, clearToken);
                                await Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    var currentClip = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                                    if (currentClip != null)
                                    {
                                        var currentText = await currentClip.TryGetTextAsync();
                                        if (currentText == copiedSecret)
                                        {
                                            await currentClip.ClearAsync();
                                        }
                                    }
                                });
                            }
                            catch (OperationCanceledException) { /* New copy cancelled this timer */ }
                            catch { /* Ignore clipboard clear failures */ }
                        });
                    }

                    // Show brief confirmation
                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    $"Failed to copy password: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task CopyUsernameAsync(CredentialViewModel credential)
        {
            try
            {
                _idleLockService.Reset();
                if (_clipboardGuard != null && !_clipboardGuard.CanCopy())
                {
                    await _dialogService.ShowWarningAsync(
                        "Clipboard Blocked",
                        "Too many clipboard operations detected. Please wait before copying again.",
                        _ownerWindow);
                    StatusMessage = "Clipboard temporarily blocked";
                    return;
                }

                // Skip if username is empty
                if (string.IsNullOrWhiteSpace(credential.Username))
                {
                    StatusMessage = "Nothing to copy";
                    return;
                }

                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(credential.Username);
                    StatusMessage = $"Username copied for: {credential.Title}";

                    // Register copy with guard
                    _clipboardGuard?.RegisterCopy(credential.Title);

                    // Cancel any previous clipboard clear timer so it doesn't wipe the new content
                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

                    // Schedule clipboard clearing based on user settings
                    var settings = SettingsService.Load();
                    var clearDelay = settings.GetClipboardClearDelay();
                    if (clearDelay.HasValue)
                    {
                        var copiedUsername = credential.Username; // Capture copied value
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(clearDelay.Value, clearToken);
                                await Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    var currentClip = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                                    if (currentClip != null)
                                    {
                                        var currentText = await currentClip.TryGetTextAsync();
                                        if (currentText == copiedUsername)
                                        {
                                            await currentClip.ClearAsync();
                                        }
                                    }
                                });
                            }
                            catch (OperationCanceledException) { /* New copy cancelled this timer */ }
                            catch { /* Ignore clipboard clear failures */ }
                        });
                    }

                    // Show brief confirmation
                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    $"Failed to copy username: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task CopyTotpCodeAsync(CredentialViewModel credential)
        {
            try
            {
                // Check clipboard guard
                if (_clipboardGuard != null && !_clipboardGuard.CanCopy())
                {
                    await _dialogService.ShowWarningAsync(
                        "Clipboard Blocked",
                        "Too many clipboard operations detected. Please wait before copying again.",
                        _ownerWindow);
                    StatusMessage = "Clipboard temporarily blocked";
                    return;
                }

                // Skip if TOTP code is empty
                if (string.IsNullOrWhiteSpace(credential.CurrentTotpCode))
                {
                    StatusMessage = "No TOTP code available";
                    return;
                }

                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(credential.CurrentTotpCode);
                    StatusMessage = $"TOTP code copied for: {credential.Title}";

                    // Register copy with guard
                    _clipboardGuard?.RegisterCopy(credential.Title);

                    // Cancel any previous clipboard clear timer so it doesn't wipe the new content
                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

                    // TOTP codes expire, so clear clipboard after code expires
                    var clearDelay = TimeSpan.FromSeconds(credential.TotpSecondsRemaining + 5);
                    var copiedCode = credential.CurrentTotpCode;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(clearDelay, clearToken);
                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                var currentClip = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                                if (currentClip != null)
                                {
                                    var currentText = await currentClip.TryGetTextAsync();
                                    if (currentText == copiedCode)
                                    {
                                        await currentClip.ClearAsync();
                                    }
                                }
                            });
                        }
                        catch (OperationCanceledException) { /* New copy cancelled this timer */ }
                        catch { /* Ignore clipboard clear failures */ }
                    });

                    // Show brief confirmation
                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    $"Failed to copy TOTP code: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task AddTotpAsync()
        {
            try
            {
                if (SelectedCredential == null)
                {
                    await _dialogService.ShowWarningAsync(
                        "No Entry Selected",
                        "Please select a password entry to add TOTP authentication.",
                        _ownerWindow);
                    return;
                }

                // Pre-populate with credential info
                var viewModel = new TotpScannerViewModel
                {
                    Issuer = SelectedCredential.Title,
                    AccountName = SelectedCredential.Username
                };
                var dialog = new TotpScannerDialog(viewModel)
                {
                    Title = "Add TOTP to " + SelectedCredential.Title
                };

                var result = await dialog.ShowDialog<TotpScanResult?>(_ownerWindow!);

                if (result != null && result.Success)
                {
                    // Find the credential in the underlying collection
                    var credential = _credentials.FirstOrDefault(c => c.Title == SelectedCredential.Title);
                    if (credential != null)
                    {
                        // Get the core credential
                        var coreCredential = credential.GetCredential();

                        // Update TOTP fields
                        coreCredential.TotpSecret = result.Secret;
                        coreCredential.TotpDigits = result.Digits;
                        coreCredential.TotpTimeStep = result.Period;
                        coreCredential.TotpAlgorithm = result.Algorithm;
                        coreCredential.TotpIssuer = result.Issuer;
                        coreCredential.TotpAccountName = result.AccountName;
                        coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        // Save the vault to persist changes
                        await SaveVaultAsync();

                        // Recreate the credential view model to reflect changes
                        var index = _credentials.IndexOf(credential);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCredential);
                        _credentials.Insert(index, newCredentialVM);

                        // Update selected credential
                        SelectedCredential = newCredentialVM;

                        StatusMessage = $"TOTP added to {SelectedCredential.Title}";
                    }
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Add TOTP Failed",
                    $"Failed to add TOTP: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task EditTotpAsync(CredentialViewModel credential)
        {
            try
            {
                // Pre-populate with existing TOTP data
                var viewModel = new TotpScannerViewModel
                {
                    Issuer = credential.TotpIssuer,
                    AccountName = credential.TotpAccountName,
                    SecretKey = credential.TotpSecret,
                    Digits = credential.TotpDigits,
                    Period = credential.TotpTimeStep,
                    Algorithm = credential.TotpAlgorithm,
                    IsEditing = true
                };

                var dialog = new TotpScannerDialog(viewModel)
                {
                    Title = "TOTP Settings – " + credential.Title
                };

                var result = await dialog.ShowDialog<TotpScanResult?>(_ownerWindow!);

                if (result != null && result.Success)
                {
                    // Handle delete request
                    if (result.Deleted)
                    {
                        await RemoveTotpAsync();
                        return;
                    }

                    // Find the credential in the underlying collection
                    var credentialVM = _credentials.FirstOrDefault(c => c.Title == credential.Title);
                    if (credentialVM != null)
                    {
                        // Get the core credential
                        var coreCredential = credentialVM.GetCredential();

                        // Update TOTP fields
                        coreCredential.TotpSecret = result.Secret;
                        coreCredential.TotpDigits = result.Digits;
                        coreCredential.TotpTimeStep = result.Period;
                        coreCredential.TotpAlgorithm = result.Algorithm;
                        coreCredential.TotpIssuer = result.Issuer;
                        coreCredential.TotpAccountName = result.AccountName;
                        coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        // Save the vault to persist changes
                        await SaveVaultAsync();

                        // Recreate the credential view model to reflect changes
                        var index = _credentials.IndexOf(credentialVM);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCredential);
                        _credentials.Insert(index, newCredentialVM);

                        // Update selected credential if it was the one being edited
                        if (SelectedCredential?.Title == credential.Title)
                        {
                            SelectedCredential = newCredentialVM;
                        }

                        StatusMessage = $"TOTP updated for {credential.Title}";
                    }
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Edit TOTP Failed",
                    $"Failed to edit TOTP: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task SaveTotpSecretAsync()
        {
            try
            {
                if (SelectedCredential == null)
                {
                    await _dialogService.ShowWarningAsync(
                        "No Entry Selected",
                        "Please select a password entry to add TOTP authentication.",
                        _ownerWindow);
                    return;
                }

                var secret = TotpSecretInput?.Trim().Replace(" ", "").ToUpperInvariant();
                if (string.IsNullOrEmpty(secret))
                {
                    await _dialogService.ShowWarningAsync(
                        "Missing Secret Key",
                        "Please enter a valid Base32 TOTP secret key.",
                        _ownerWindow);
                    return;
                }

                // Basic Base32 validation
                var validBase32 = System.Text.RegularExpressions.Regex.IsMatch(secret, @"^[A-Z2-7]+=*$");
                if (!validBase32)
                {
                    await _dialogService.ShowWarningAsync(
                        "Invalid Secret Key",
                        "The secret key must be a valid Base32 string (letters A-Z and digits 2-7).",
                        _ownerWindow);
                    return;
                }

                // Find the credential in the underlying collection
                var credential = _credentials.FirstOrDefault(c => c == SelectedCredential);
                if (credential != null)
                {
                    var coreCredential = credential.GetCredential();

                    // Update TOTP fields
                    coreCredential.TotpSecret = secret;
                    coreCredential.TotpDigits = 6;
                    coreCredential.TotpTimeStep = 30;
                    coreCredential.TotpAlgorithm = "SHA1";
                    coreCredential.TotpIssuer = coreCredential.Title;
                    coreCredential.TotpAccountName = coreCredential.Username;
                    coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                    // Save the vault to persist changes
                    await SaveVaultAsync();

                    // Sync updated TOTP to shared file for Attestor
                    await ExportTotpToSyncAsync();

                    // Recreate the credential view model to start the TOTP timer
                    var index = _credentials.IndexOf(credential);
                    _credentials.RemoveAt(index);
                    var newCredentialVM = new CredentialViewModel(coreCredential);
                    _credentials.Insert(index, newCredentialVM);

                    // Update selected credential
                    SelectedCredential = newCredentialVM;

                    // Clear the input
                    TotpSecretInput = string.Empty;

                    StatusMessage = $"TOTP added to {newCredentialVM.Title}";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Save TOTP Failed",
                    $"Failed to save TOTP secret: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task RemoveTotpAsync()
        {
            try
            {
                if (SelectedCredential == null) return;

                var credential = _credentials.FirstOrDefault(c => c == SelectedCredential);
                if (credential != null)
                {
                    var coreCredential = credential.GetCredential();

                    // Clear TOTP fields
                    coreCredential.TotpSecret = string.Empty;
                    coreCredential.TotpIssuer = string.Empty;
                    coreCredential.TotpAccountName = string.Empty;
                    coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                    await SaveVaultAsync();

                    // Sync updated TOTP to shared file for Attestor (entry removed)
                    await ExportTotpToSyncAsync();

                    // Recreate credential VM
                    var index = _credentials.IndexOf(credential);
                    _credentials.RemoveAt(index);
                    var newCredentialVM = new CredentialViewModel(coreCredential);
                    _credentials.Insert(index, newCredentialVM);

                    SelectedCredential = newCredentialVM;

                    StatusMessage = $"TOTP removed from {newCredentialVM.Title}";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Remove TOTP Failed",
                    $"Failed to remove TOTP: {ex.Message}",
                    _ownerWindow);
            }
        }

        private async Task OpenUrlAsync(CredentialViewModel credential)
        {
            try
            {
                if (!string.IsNullOrEmpty(credential.Url))
                {
                    var uri = new Uri(credential.Url);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                    StatusMessage = $"Opening: {credential.Url}";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Open Failed",
                    $"Failed to open URL: {ex.Message}",
                    _ownerWindow);
            }
        }

        private void SelectCredential(CredentialViewModel credential)
        {
            SelectedCredential = credential;
            StatusMessage = $"Selected: {credential.Title}";
        }

        private void ToggleFavorite(CredentialViewModel credential)
        {
            credential.IsFavorite = !credential.IsFavorite;
            StatusMessage = credential.IsFavorite
                ? $"Added to favorites: {credential.Title}"
                : $"Removed from favorites: {credential.Title}";
            this.RaisePropertyChanged(nameof(FavoritesCount));
        }

        private void UpdateQuickFilterHighlight(int index)
        {
            var top = QuickFilterBaseOffset + index * (QuickFilterButtonHeight + QuickFilterSpacing);
            QuickFilterHighlightMargin = new Thickness(QuickFilterHorizontalMargin, top, QuickFilterHorizontalMargin, 0);
            QuickFilterHighlightOpacity = QuickFilterActiveOpacity;
            QuickFilterHighlightOffset = QuickFilterForwardNudge;
            _ = Dispatcher.UIThread.InvokeAsync(() => QuickFilterHighlightOffset = 0, DispatcherPriority.Background);
        }

        private void HideQuickFilterHighlight()
        {
            QuickFilterHighlightOpacity = 0;
            QuickFilterHighlightOffset = 0;
            QuickFilterHighlightLightShift = 0.5d;
            QuickFilterHighlightLightLeft = 0.18d;
            QuickFilterHighlightLightRight = 0.82d;
            QuickFilterHighlightSheenInnerLeft = 0.42d;
            QuickFilterHighlightSheenInnerRight = 0.58d;
        }

        private void UpdateQuickFilterHighlightLightShift(double offset)
        {
            var normalized = Math.Clamp(offset / QuickFilterForwardNudge, -1d, 1d);
            var center = 0.5d + normalized * QuickFilterLightShiftRange;
            var clampedCenter = Math.Clamp(center, 0.35d, 0.65d);
            QuickFilterHighlightLightShift = clampedCenter;
            var movementIntensity = Math.Abs(normalized);
            var outerSpread = 0.32d + movementIntensity * 0.06d;
            QuickFilterHighlightLightLeft = Math.Clamp(clampedCenter - outerSpread, 0.02d, 0.28d);
            QuickFilterHighlightLightRight = Math.Clamp(clampedCenter + outerSpread, 0.72d, 0.98d);
            var innerSpread = 0.085d - movementIntensity * 0.025d;
            QuickFilterHighlightSheenInnerLeft = Math.Clamp(clampedCenter - innerSpread, 0.32d, 0.48d);
            QuickFilterHighlightSheenInnerRight = Math.Clamp(clampedCenter + innerSpread, 0.52d, 0.68d);
        }

        private void UpdateCategoryHighlight(int index)
        {
            var top = CategoryHighlightBaseOffset + index * (CategoryButtonHeight + CategoryButtonSpacing);
            CategoryHighlightMargin = new Thickness(CategoryHighlightHorizontalMargin, top, CategoryHighlightHorizontalMargin, 0);
            CategoryHighlightOpacity = CategoryHighlightActiveOpacity;
            CategoryHighlightOffset = CategoryHighlightForwardNudge;
            _ = Dispatcher.UIThread.InvokeAsync(() => CategoryHighlightOffset = 0, DispatcherPriority.Background);
        }

        private void HideCategoryHighlight()
        {
            CategoryHighlightOpacity = 0;
            CategoryHighlightOffset = 0;
            CategoryHighlightLightShift = 0.5d;
            CategoryHighlightLightLeft = 0.18d;
            CategoryHighlightLightRight = 0.82d;
            CategoryHighlightSheenInnerLeft = 0.42d;
            CategoryHighlightSheenInnerRight = 0.58d;
        }

        private void UpdateCategoryHighlightLightShift(double offset)
        {
            var normalized = Math.Clamp(offset / CategoryHighlightForwardNudge, -1d, 1d);
            var center = 0.5d + normalized * CategoryHighlightLightShiftRange;
            var clampedCenter = Math.Clamp(center, 0.35d, 0.65d);
            CategoryHighlightLightShift = clampedCenter;
            var movementIntensity = Math.Abs(normalized);
            var outerSpread = 0.32d + movementIntensity * 0.06d;
            CategoryHighlightLightLeft = Math.Clamp(clampedCenter - outerSpread, 0.02d, 0.28d);
            CategoryHighlightLightRight = Math.Clamp(clampedCenter + outerSpread, 0.72d, 0.98d);
            var innerSpread = 0.085d - movementIntensity * 0.025d;
            CategoryHighlightSheenInnerLeft = Math.Clamp(clampedCenter - innerSpread, 0.32d, 0.48d);
            CategoryHighlightSheenInnerRight = Math.Clamp(clampedCenter + innerSpread, 0.52d, 0.68d);
        }

        private void ShowDashboard()
        {
            // If dashboard is disabled by user, redirect to Passwords view
            if (!IsDashboardEnabled)
            {
                ShowPasswords();
                return;
            }

            // FORCE COMPLETE VIEW DESTRUCTION AND RECREATION
            // Step 1: Hide dashboard view to unload from visual tree
            IsShowingDashboard = false;

            // Step 2: Force UI thread to process the visibility change
            System.Threading.Tasks.Task.Delay(1).Wait();

            // Step 3: Clear all old data completely
            DashboardViewModel.ClearDashboard();

            // Step 4: Clear all other view states
            IsShowingAll = false;
            IsShowingPasswords = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            IsShowingCategoryLanding = false;
            SetActiveCategory(null);
            SelectedCredential = null;
            SelectedTrashItem = null;
            CurrentViewTitle = "Dashboard";

            // Always collapse sidebar in dashboard view
            IsSidebarCollapsed = true;

            // Step 5: Show dashboard view (forces re-render with clean state)
            IsShowingDashboard = true;

            // Step 6: Load fresh data
            _ = DashboardViewModel.LoadDashboardDataAsync();
            DashboardViewModel.OwnerWindow = _ownerWindow;
            DashboardViewModel.ClipboardGuard = _clipboardGuard;
            DashboardViewModel.LoadQuickAccessCredentials(_credentials, _categories);
            DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);

            // Force rebind ToggleButton IsChecked (ToggleButton internal toggle can desync OneWay binding)
            this.RaisePropertyChanged(nameof(IsShowingDashboard));
            this.RaisePropertyChanged(nameof(IsShowingPasswords));
        }

        private void ShowPasswords()
        {
            if (_isInitializing)
            {
                return;
            }

            EnsureVaultVisible();
            IsShowingAll = false;
            IsShowingPasswords = true;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "Passwords";
            IsSidebarCollapsed = false;  // Expand sidebar to show full navigation

            FilterByEntryType("Password");
            IsShowingPasswords = true;
            IsShowingDashboard = false;
            CurrentViewTitle = "Passwords";
            ApplyFilters();

            // Force rebind ToggleButton IsChecked (ToggleButton internal toggle can desync OneWay binding)
            this.RaisePropertyChanged(nameof(IsShowingPasswords));
            this.RaisePropertyChanged(nameof(IsShowingDashboard));
        }

        private void ShowAll()
        {
            // Don't switch away from dashboard during initialization
            if (_isInitializing)
            {
                return;
            }

            IsShowingDashboard = false;
            IsShowingAll = true;
            IsShowingPasswords = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "All Items";
            ApplyFilters();
            UpdateQuickFilterHighlight(0);
        }

        private void ShowFavorites()
        {
            IsShowingDashboard = false;
            IsShowingAll = false;
            IsShowingFavorites = true;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "Favorites";
            ApplyFilters();
            UpdateQuickFilterHighlight(1);
        }

        private void ShowPasskeys()
        {
            IsShowingAll = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = true;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "Passkeys";
            // Don't call ApplyFilters - we'll show the Passkeys collection directly
            UpdateQuickFilterHighlight(2);
        }

        private void ShowRecent()
        {
            IsShowingAll = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = true;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "Recently Used";
            ApplyFilters();
            UpdateQuickFilterHighlight(3);
        }

        private void ShowExpiringSoon()
        {
            IsShowingAll = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = true;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            CurrentViewTitle = "Expiring Soon";
            ApplyFilters();
            UpdateQuickFilterHighlight(4);
        }

        private void SelectCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return;
            }

            IsShowingAll = false;
            IsShowingFavorites = false;
            IsShowingRecent = false;
            IsShowingPasskeys = false;
            IsShowingExpiringSoon = false;

            if (string.Equals(categoryName, "Secure Rubbish Bin", StringComparison.OrdinalIgnoreCase))
            {
                ActivateSecureTrash();
                return;
            }

            IsShowingSecureTrash = false;
            SetActiveCategory(categoryName);
            SelectedTrashItem = null;
            CurrentViewTitle = categoryName;
            HideQuickFilterHighlight();
            ApplyFilters();
        }

        private void SetEntryType(string? entryTypeValue)
        {
            if (string.IsNullOrEmpty(entryTypeValue))
            {
                // Show all types
                CurrentEntryType = null;
                CurrentViewTitle = "All Entry Types";
            }
            else if (int.TryParse(entryTypeValue, out int typeInt) && Enum.IsDefined(typeof(Core.Models.EntryType), typeInt))
            {
                CurrentEntryType = (Core.Models.EntryType)typeInt;
                CurrentViewTitle = CurrentEntryType.Value.ToString();
            }
        }

        private void FilterByEntryType(string entryTypeValue)
        {
            var keepPasswordsNavActive = IsShowingPasswords || IsShowingDashboard;

            // Reset other filters
            IsShowingAll = false;
            IsShowingFavorites = false;
            IsShowingPasskeys = false;
            IsShowingRecent = false;
            IsShowingExpiringSoon = false;
            IsShowingSecureTrash = false;
            SetActiveCategory(null);
            SelectedTrashItem = null;
            IsShowingDashboard = false;
            IsShowingPasswords = keepPasswordsNavActive;

            if (entryTypeValue == "All")
            {
                CurrentEntryType = null;
                CurrentViewTitle = "All Entry Types";
            }
            else if (Enum.TryParse<Core.Models.EntryType>(entryTypeValue, out var parsedType))
            {
                CurrentEntryType = parsedType;
                CurrentViewTitle = entryTypeValue switch
                {
                    "Password" => "Passwords",
                    "CreditCard" => "Credit Cards",
                    "BankAccount" => "Bank Accounts",
                    "Identity" => "Identity Documents",
                    "ApiKey" => "API Keys",
                    "WiFi" => "WiFi Networks",
                    "Contact" => "Contacts",
                    "PinCode" => "PIN Codes",
                    _ => entryTypeValue
                };
            }

            ApplyFilters();
        }

        private void NavigateToVaultWithFilter(string filterType)
        {
            // Hide the category landing page and show the vault view
            IsShowingCategoryLanding = false;

            // Apply the appropriate filter
            switch (filterType)
            {
                case "Dashboard":
                    IsShowingDashboard = true;
                    break;
                case "PasswordHealth":
                    IsShowingDashboard = false;
                    OpenPasswordHealthPanel();
                    break;
                case "Recovery":
                    IsShowingDashboard = false;
                    ToggleRecoveryPanel();
                    break;
                case "SecurityOptions":
                    IsShowingDashboard = false;
                    OpenSettingsPanel();
                    ShowSecuritySettings();
                    break;
                case "All":
                    IsShowingDashboard = false;
                    ShowAll();
                    break;
                case "Password":
                    IsShowingDashboard = false;
                    FilterByEntryType("Password");
                    break;
                case "CreditCard":
                    IsShowingDashboard = false;
                    FilterByEntryType("CreditCard");
                    break;
                case "SecureNote":
                    IsShowingDashboard = false;
                    FilterByEntryType("SecureNote");
                    break;
                case "Identity":
                    IsShowingDashboard = false;
                    FilterByEntryType("Identity");
                    break;
                case "WiFi":
                    IsShowingDashboard = false;
                    FilterByEntryType("WiFi");
                    break;
                case "BankAccount":
                    IsShowingDashboard = false;
                    FilterByEntryType("BankAccount");
                    break;
                case "ApiKey":
                    IsShowingDashboard = false;
                    FilterByEntryType("ApiKey");
                    break;
                case "Contact":
                    IsShowingDashboard = false;
                    FilterByEntryType("Contact");
                    break;
                case "PinCode":
                    IsShowingDashboard = false;
                    FilterByEntryType("PinCode");
                    break;
                case "Passkey":
                    IsShowingDashboard = false;
                    ShowPasskeys();
                    break;
                case "TOTP":
                    IsShowingDashboard = false;
                    // Filter to show only credentials with TOTP
                    IsShowingAll = false;
                    IsShowingFavorites = false;
                    IsShowingPasskeys = false;
                    IsShowingRecent = false;
                    IsShowingExpiringSoon = false;
                    IsShowingSecureTrash = false;
                    SetActiveCategory(null);
                    CurrentViewTitle = "TOTP Codes";
                    ApplyFilters();
                    break;
                case "Favorites":
                    IsShowingDashboard = false;
                    ShowFavorites();
                    break;
                default:
                    if (filterType.StartsWith("Category:", StringComparison.Ordinal))
                    {
                        IsShowingDashboard = false;
                        SelectCategory(filterType.Substring("Category:".Length));
                    }
                    else
                    {
                        IsShowingDashboard = false;
                        ShowAll();
                    }
                    break;
            }
        }

        private void ActivateSecureTrash()
        {
            SetActiveCategory(null);
            IsShowingSecureTrash = true;
            SelectedCredential = null;
            SelectedTrashItem = null;
            IsGridView = false;
            CurrentViewTitle = "Secure Rubbish Bin";
            IsShowingExpiringSoon = false;
            IsShowingRecent = false;
            IsShowingFavorites = false;
            IsShowingAll = false;
            IsShowingPasskeys = false;
            HideQuickFilterHighlight();
            ApplyTrashFilters();
        }

        private void SetActiveCategory(string? categoryName)
        {
            _activeCategory = categoryName;
            ActiveCategoryDisplayName = categoryName ?? string.Empty;
            this.RaisePropertyChanged(nameof(IsShowingCategoryFiltered));
            UpdateCategoryActiveStates();
        }

        private void UpdateCategoryActiveStates()
        {
            if (_categories == null || _categories.Count == 0)
            {
                HideCategoryHighlight();
                return;
            }

            var activeIndex = -1;
            for (var i = 0; i < _categories.Count; i++)
            {
                var category = _categories[i];
                var isMatch = !string.IsNullOrWhiteSpace(_activeCategory) &&
                              string.Equals(category.Name, _activeCategory, StringComparison.OrdinalIgnoreCase);
                category.IsActive = isMatch;
                if (isMatch)
                {
                    activeIndex = i;
                }
            }

            if (activeIndex >= 0)
            {
                UpdateCategoryHighlight(activeIndex);
            }
            else
            {
                HideCategoryHighlight();
            }
        }

        public Task ShowCategoryManagerAsync()
        {
            return ManageCategoriesAsync();
        }

        internal bool TryGetManifestContext(out string manifestPath, out string? passphrase, out string? keyfilePath)
        {
            manifestPath = string.Empty;
            passphrase = _vaultPassword;
            keyfilePath = _vaultKeyfilePath;

            if (!string.IsNullOrEmpty(_mountPath))
            {
                var pvault = Path.Combine(_mountPath, "vault.pvault");
                if (File.Exists(pvault))
                {
                    manifestPath = pvault;
                    return true;
                }
                var legacy = Path.Combine(_mountPath, "vault.manifest");
                if (File.Exists(legacy))
                {
                    manifestPath = legacy;
                    return true;
                }
            }

            var fallbackPvault = Path.Combine(AppContext.BaseDirectory, "vault.pvault");
            if (File.Exists(fallbackPvault))
            {
                manifestPath = fallbackPvault;
                return true;
            }
            var fallbackManifest = Path.Combine(AppContext.BaseDirectory, "vault.manifest");
            if (File.Exists(fallbackManifest))
            {
                manifestPath = fallbackManifest;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sets the manifest context with the provided credentials.
        /// This allows the vault to be initialized with password/keyfile before mounting.
        /// </summary>
        /// <param name="manifestPath">Path to the vault manifest file.</param>
        /// <param name="password">The vault password.</param>
        /// <param name="keyfilePath">Optional path to the keyfile.</param>
        internal void SetManifestContext(string manifestPath, string? password, string? keyfilePath)
        {
            // Store the credentials for later use
            _vaultPassword = password;
            _vaultKeyfilePath = keyfilePath;

            // Persist re-auth context for in-app lock/unlock.
            _manifestPath = manifestPath;
            _reauthKeyfilePath = keyfilePath;

            // Clear password parameter immediately after copying to field (security best practice)
            password = null!;

            // Set mount path to the directory containing the manifest
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
            {
                _mountPath = manifestDir;
                _usbRootPath = manifestDir;
            }

            _transportLayoutRoot = TryResolveTransportLayoutRoot(manifestPath);

            try
            {
                var manifest = _manifestService.ReadManifest(manifestPath, _vaultPassword, keyfilePath);
                ApplyManifestTransportState(manifest, manifestPath);
            }
            catch
            {
                _masterVolumePath = null;
                _protectionTier = VaultProtectionTier.StandardSecure;
                _effectiveStorageTransport = VaultStorageTransport.FileSystem;
                _requestedStorageTransport = null;
            }

            // Clear local parameter after copying to field (security best practice)
            password = null!;

            this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
            this.RaisePropertyChanged(nameof(PinUnlockAvailable));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));

            // Apply manifest category colors so they appear immediately in the sidebar
            LoadCategoryColorsFromManifest();
        }

        private async Task ManageCategoriesAsync()
        {
            try
            {
                EnsureVaultVisible();
                // Attempt to locate the manifest file using the stored path or by searching on disk.
                string? manifestPath = _manifestPath;
                // If we have a mount path, check for .pvault then legacy .manifest
                if (manifestPath == null && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.pvault");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }
                if (manifestPath == null && !string.IsNullOrEmpty(_mountPath))
                {
                    var candidate = System.IO.Path.Combine(_mountPath, "vault.manifest");
                    if (System.IO.File.Exists(candidate)) manifestPath = candidate;
                }

                if (manifestPath != null)
                {
                    // Use cached vault credentials from initial unlock — never re-prompt the user.
                    var passphrase = string.IsNullOrEmpty(_vaultPassword) ? null : _vaultPassword;
                    var keyfile = string.IsNullOrEmpty(_vaultKeyfilePath) ? null : _vaultKeyfilePath;

                    try
                    {
                        var manifest = _manifestService.ReadManifest(manifestPath, passphrase, keyfile);

                        CloseAllOverlays();
                        var categoryVm = new CategoryManagerViewModel(_manifestService, manifest, manifestPath, this, passphrase: passphrase, keyfilePath: keyfile ?? manifest.KeyfilePath);
                        AttachCategoryManagerViewModel(categoryVm);
                        IsCategoryManagerPanelVisible = true;
                        StatusMessage = "Managing categories";
                    }
                    catch (Exception innerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CategoryManager] Failed to read manifest with cached credentials: {innerEx.Message}");
                        await _dialogService.ShowErrorAsync("Category Manager", $"Failed to read manifest: {innerEx.Message}", _ownerWindow);
                        StatusMessage = "Category manager error";
                    }
                }
                else
                {
                    // No manifest found; still open a manager synced to the current sidebar categories
                    CloseAllOverlays(); // Close any other open overlays
                    var categoryVm = new CategoryManagerViewModel(this);
                    AttachCategoryManagerViewModel(categoryVm);
                    IsCategoryManagerPanelVisible = true;
                    StatusMessage = "Managing categories";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Manage Categories", $"Unable to open category manager: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", _ownerWindow);
                StatusMessage = "Category manager error";
            }
        }

        private void ClearSearch()
        {
            SearchText = string.Empty;
            this.RaisePropertyChanged(nameof(HasSearchText));
        }

        private void ToggleViewMode()
        {
            if (IsShowingSecureTrash)
            {
                StatusMessage = "Secure rubbish bin is list view only";
                return;
            }

            ViewModeIcon = ViewModeIcon == "☰" ? "▦" : "☰";
            GridViewIconPath = IsGridView ? "Assets/SVG/Current/Grid.svg" : "Assets/SVG/Current/List.svg";
            IsGridView = !IsGridView;
            StatusMessage = ViewModeIcon == "☰" ? "List view" : "Grid view";
        }

        private void ToggleSidebar()
        {
            // Don't allow sidebar expansion when on dashboard
            if (IsShowingDashboard && IsSidebarCollapsed)
            {
                StatusMessage = "Sidebar cannot be expanded in dashboard view";
                return;
            }

            // Toggle between collapsed and expanded states
            // If auto-collapsed, this is a manual expand
            if (_isSidebarAutoCollapsed)
            {
                _isSidebarAutoCollapsed = false;
            }

            IsSidebarCollapsed = !IsSidebarCollapsed;
            StatusMessage = IsSidebarCollapsed ? "Sidebar collapsed" : "Sidebar expanded";
        }

        public void UpdateSidebarVisibility(double windowWidth)
        {
            // In smaller windows (<1200px), auto-collapse unless manually expanded
            // In larger windows (>=1200px), respect user's preference (don't auto-expand)
            if (windowWidth >= 1200)
            {
                // Large screen: clear auto-collapse state, but don't force expand
                _isSidebarAutoCollapsed = false;
            }
            else
            {
                // Small screen: auto-collapse unless user manually expanded
                if (!_isSidebarAutoCollapsed && !IsSidebarCollapsed)
                {
                    // First time going small: auto-collapse
                    _isSidebarAutoCollapsed = true;
                    IsSidebarCollapsed = true;
                }
                // If user manually expanded (_isSidebarAutoCollapsed == false), respect their choice
            }
        }

        private void ToggleTheme()
        {
            // Light mode only — toggle disabled
        }

        private void ApplyThemeVariant()
        {
            try
            {
                if (Avalonia.Application.Current is App app)
                {
                    app.SetTheme("light");
                    StatusMessage = "Light theme";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to switch theme";
                System.Diagnostics.Debug.WriteLine($"Theme toggle error: {ex.Message}");
            }
        }

        private void OnPrivacyModeChanged(object? sender, EventArgs e)
        {
            PrivacyModeEnabled = PrivacyShield.PrivacyModeEnabled;
        }

        private void RegisterAutoLockSession()
        {
            if (string.IsNullOrWhiteSpace(_mountPath))
            {
                _vaultLockDurationService.SetActiveSession(null);
                return;
            }

            var session = new VaultLockSessionContext
            {
                MountPath = _mountPath,
                FlushPendingWritesAsync = SaveVaultAsync,
                ReleaseHandlesAsync = ReleaseTransientHandlesAsync,
                ZeroizeSensitiveData = ClearSensitiveState,
                OnCleanupFailed = ex => Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"Lock cleanup issue: {ex.Message}";
                })
            };

            _vaultLockDurationService.SetActiveSession(session);
        }

        private void ClearSensitiveState()
        {
            if (!string.IsNullOrEmpty(_vaultPassword))
            {
                try
                {
                    var chars = _vaultPassword.ToCharArray();
                    Array.Clear(chars, 0, chars.Length);
                }
                catch
                {
                    // Best-effort scrub of managed string copy.
                }
            }

            _vaultPassword = null;
            _vaultKeyfilePath = null;
        }

        private Task ReleaseTransientHandlesAsync()
        {
            return Task.CompletedTask;
        }

        private void OnVaultLocked(object? sender, VaultLockEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (e.Result.Error != null)
                {
                    StatusMessage = $"Lock cleanup failed: {e.Result.Error.Message}";
                    LockscreenError = e.Result.Error.Message;
                }

                // Always enter the lockscreen instead of closing the window.
                EnterLockedState(
                    title: "Vault Locked",
                    message: e.Reason == LockReason.AutoLock ? "Vault auto-locked due to inactivity." : "Vault locked.",
                    softLock: false);

                // Clear sensitive UI state so nothing is visible behind the overlay.
                ClearVaultUiState();

                // Auto-lock should not kill the app window.
                await Task.CompletedTask;
            });
        }

        private void OnIdleLockElapsed()
        {
            // Issue #1: auto-lock must not activate before a PIN is configured for this
            // vault. If the user has never set a PIN, idle expiry is a no-op — there is
            // nothing to fall back to except the full passphrase, which is a bad UX
            // when the user hasn't opted into PIN-protected sessions yet.
            try
            {
                var settings = SettingsService.Load();
                bool pinConfigured = settings != null
                    && settings.EnablePinLock
                    && PinLockService.HasPinConfigured(settings, _manifestPath);
                if (!pinConfigured)
                {
                    return;
                }
            }
            catch
            {
                // If settings can't be loaded for any reason, fail closed by skipping
                // auto-lock rather than locking a user who has never set a PIN.
                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                await LockInAppAsync(LockReason.AutoLock);
            });
        }

        private void ClearVaultUiState()
        {
            _items.Clear();
            _credentials.Clear();
            _filteredCredentials.Clear();
            _groupedListItems.Clear();
            _passkeys.Clear();
            _flaggedCredentials.Clear();
            _filteredTrashItems.Clear();
            SelectedCredential = null;
            SelectedTrashItem = null;

            // Dispose TOTP sync service
            if (_totpSyncService != null)
            {
                _totpSyncService.EntriesChanged -= OnTotpEntriesChanged;
                _totpSyncService.Dispose();
                _totpSyncService = null;
            }
        }

        private void EnterLockedState(string title, string message, bool softLock)
        {
            LockscreenTitle = title;
            LockscreenMessage = message;
            LockscreenError = string.Empty;
            LockscreenPassword = string.Empty;
            LockscreenPin = string.Empty;
            _lockscreenFailedUnlockAttempts = 0;
            _lockscreenLockedUntilUtc = null;

            // If a vault was opened via a flow that didn't provide re-auth context,
            // try to locate the manifest on the current mount/base directory.
            // Skip this in developer bypass mode (no real vault to re-auth).
            if (!_isDeveloperBypassMode
                && string.IsNullOrWhiteSpace(_manifestPath)
                && TryGetManifestContext(out var inferredManifestPath, out _, out _))
            {
                _manifestPath = inferredManifestPath;
                this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
            }

            _showPassphraseFallback = false;
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));

            IsSoftLocked = softLock;
            IsLockscreenVisible = true;
        }

        private void ExitLockedState()
        {
            LockscreenError = string.Empty;
            LockscreenPassword = string.Empty;
            LockscreenPin = string.Empty;
            _lockscreenFailedUnlockAttempts = 0;
            _lockscreenLockedUntilUtc = null;

            _showPassphraseFallback = false;
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));

            IsSoftLocked = false;
            IsLockscreenVisible = false;
        }

        private void DismissLockscreen()
        {
            // Developer bypass has no mounted vault to re-auth; avoid trapping the user.
            _vaultLockDurationService.UnlockVault();
            ExitLockedState();
        }

        private async Task SetupPinLockAsync()
        {
            try
            {
                var dialog = new PhantomVault.UI.Views.Dialogs.PinSetupDialog(_manifestPath);

                if (_ownerWindow != null)
                {
                    await dialog.ShowDialog(_ownerWindow);
                }
                else
                {
                    dialog.Show();
                }

                var viewModel = dialog.DataContext as PhantomVault.UI.ViewModels.Dialogs.PinSetupDialogViewModel;
                if (viewModel?.Success == true)
                {
                    // Update settings to enable PIN lock
                    var settings = SettingsService.Load();
                    settings.EnablePinLock = true;
                    SettingsService.Save(settings);

                    // Issue #1: now that a PIN is configured, arm the auto-lock
                    // timer and reset it so the freshly-armed countdown starts
                    // from now rather than from whenever the vault was unlocked.
                    _vaultLockDurationService.AutoLockEnabled = true;
                    _vaultLockDurationService.ResetTimer();

                    // Refresh lockscreen computed properties
                    this.RaisePropertyChanged(nameof(PinUnlockAvailable));
                    this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PIN setup failed: {ex.Message}");
            }
        }

        private async Task LockInAppAsync(LockReason reason)
        {
            // If PIN lock is enabled, we default manual lock to soft-lock (UI lock).
            // Auto-lock can be configured in settings.
            var settings = SettingsService.Load();
            bool pinConfigured = settings.EnablePinLock && PinLockService.HasPinConfigured(settings, _manifestPath);
            bool usePinForAutoLock = settings.UsePinLockForAutoLock && pinConfigured;

            if (reason == LockReason.AutoLock && usePinForAutoLock)
            {
                EnterLockedState("Vault Locked", "Vault locked due to inactivity.", softLock: true);
                ClearVaultUiState();
                return;
            }

            if (reason == LockReason.ManualLock && pinConfigured)
            {
                EnterLockedState("Vault Locked", "Vault locked.", softLock: true);
                ClearVaultUiState();
                return;
            }

            // Hard lock: dismount and wipe keys.
            if (string.IsNullOrEmpty(_mountPath))
            {
                // Dev-bypass / no mount path: still lock UI.
                EnterLockedState("Vault Locked", "Vault locked.", softLock: true);
                ClearVaultUiState();
                return;
            }

            IsBusy = true;
            StatusMessage = "Locking vault...";

            try
            {
                var result = await _vaultLockDurationService.LockVaultAsync(reason);
                if (!result.Success && result.Error != null)
                    throw new InvalidOperationException(result.Error.Message, result.Error);

                EnterLockedState("Vault Locked", reason == LockReason.AutoLock ? "Vault auto-locked due to inactivity." : "Vault locked.", softLock: false);
                ClearVaultUiState();
                StatusMessage = "Vault locked.";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Lock Failed",
                    $"Failed to lock vault: {ex.Message}",
                    _ownerWindow);
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private IServiceProvider? TryGetServiceProvider()
        {
            return (Avalonia.Application.Current as App)?.Services;
        }

        private T? TryResolve<T>() where T : class
        {
            return TryGetServiceProvider()?.GetService(typeof(T)) as T;
        }

        private async Task<string?> AskForTotpAsync(Window? owner)
        {
            var dialog = new Window
            {
                Title = "Enter one-time code",
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "Enter the 6-digit code from your authenticator:", FontWeight = Avalonia.Media.FontWeight.SemiBold });
            var box = new TextBox { Width = 320, Watermark = "123456" };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };
            var ok = new Button { IsDefault = true, Content = new TextBlock { Text = "Verify" }, Width = 80 };
            var cancel = new Button { IsCancel = true, Content = new TextBlock { Text = "Cancel" }, Width = 80 };
            buttonPanel.Children.Add(ok);
            buttonPanel.Children.Add(cancel);

            panel.Children.Add(box);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            ok.Click += (_, __) => { result = box.Text; dialog.Close(); };
            cancel.Click += (_, __) => { dialog.Close(); };

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        private async Task<bool> PerformReauthChecksAsync(string password)
        {
            if (string.IsNullOrWhiteSpace(_manifestPath))
            {
                LockscreenError = "Missing manifest context; cannot re-authenticate.";
                return false;
            }

            VaultManifest manifest;
            try
            {
                manifest = _manifestService.ReadManifest(_manifestPath, password, _reauthKeyfilePath);
            }
            catch (Exception ex)
            {
                LockscreenError = $"Invalid passphrase: {ex.Message}";
                return false;
            }

            // USB binding check (best-effort)
            if (!string.IsNullOrEmpty(_usbRootPath) && !string.IsNullOrEmpty(manifest.DeviceId))
            {
                var usbBinding = TryResolve<UsbBindingService>();
                if (usbBinding != null)
                {
                    var deviceId = usbBinding.ComputeDeviceId(_usbRootPath);
                    if (!string.Equals(manifest.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        LockscreenError = "USB device mismatch. Insert the original USB drive.";
                        return false;
                    }
                }
            }

            // TOTP
            if (!string.IsNullOrEmpty(manifest.TotpSecret))
            {
                var totpService = TryResolve<TotpService>();
                if (totpService == null)
                {
                    LockscreenError = "TOTP service unavailable.";
                    return false;
                }

                Window? owner = _ownerWindow;
                if (owner == null)
                {
                    owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(w => w.IsActive);
                }

                var code = await AskForTotpAsync(owner);
                if (code == null)
                    return false;

                var expected = totpService.GenerateCode(manifest.TotpSecret);
                if (!string.Equals(code, expected, StringComparison.Ordinal))
                {
                    LockscreenError = "Invalid one-time code.";
                    return false;
                }
            }

            // Hardware token presence
            if (manifest.RequiresHardwareToken)
            {
                var yubi = TryResolve<YubiKeyService>();
                if (yubi == null)
                {
                    LockscreenError = "Hardware-token presence-check service unavailable.";
                    return false;
                }

                try
                {
                    if (!yubi.IsTokenPresent())
                    {
                        LockscreenError = "The required hardware-token presence check could not confirm the expected device.";
                        return false;
                    }
                }
                catch (NotImplementedException)
                {
                    LockscreenError = "Hardware-token presence checks are not available in this build.";
                    return false;
                }
            }

            // Local device authenticator (Windows Hello-backed)
            if (!string.IsNullOrEmpty(manifest.PasskeyId))
            {
                var services = TryGetServiceProvider();
                var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                    ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService
                    ?? new PasskeyService();

                try
                {
                    var credentialId = Convert.FromBase64String(manifest.PasskeyId);
                    byte[] challenge = new byte[32];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

                    bool ok = await passkeyService.AuthenticateAsync(credentialId, "PhantomVault", challenge);
                    if (!ok)
                    {
                        LockscreenError = "Device-authenticator verification failed.";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    LockscreenError = $"Device-authenticator verification error: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private Task UnlockWithPinAsync()
        {
            LockscreenError = string.Empty;

            if (!CanAttemptLockscreenUnlock())
            {
                return Task.CompletedTask;
            }

            if (!PinUnlockAvailable)
            {
                LockscreenError = "PIN unlock is not available for this lock.";
                return Task.CompletedTask;
            }

            if (!PinLockService.VerifyPin(LockscreenPin, _manifestPath))
            {
                RegisterFailedLockscreenAttempt("Invalid PIN.");
                return Task.CompletedTask;
            }

            ResetLockscreenUnlockFailures();
            ExitLockedState();
            return Task.CompletedTask;
        }

        private async Task UnlockWithPasswordAsync()
        {
            LockscreenError = string.Empty;

            if (!CanAttemptLockscreenUnlock())
            {
                return;
            }

            var password = LockscreenPassword;
            if (string.IsNullOrWhiteSpace(password))
            {
                LockscreenError = "Enter your passphrase.";
                return;
            }

            IsBusy = true;
            try
            {
                bool ok = await PerformReauthChecksAsync(password);
                if (!ok)
                {
                    RegisterFailedLockscreenAttempt(LockscreenError);
                    return;
                }

                ResetLockscreenUnlockFailures();

                if (IsSoftLocked)
                {
                    ExitLockedState();
                    return;
                }

                // Hard unlock: re-mount and reload.
                if (string.IsNullOrWhiteSpace(_containerAbsPath))
                {
                    LockscreenError = "Missing container path; cannot re-mount.";
                    return;
                }

                var manifest = _manifestService.ReadManifest(_manifestPath!, password, _reauthKeyfilePath);
                byte[] vaultDatabaseKey = new VaultDatabaseKeyService(new EncryptionService())
                    .DeriveKey(manifest, password, _reauthKeyfilePath);
                try
                {
                    if (!await _zkVaultService.UnlockWithHybridKeyAsync(vaultDatabaseKey).ConfigureAwait(false))
                    {
                        LockscreenError = "Failed to rehydrate the vault database key.";
                        return;
                    }
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(vaultDatabaseKey);
                }

                string mountPath = await _vaultService.MountVaultAsync(_containerAbsPath, "Vault", password);
                await LoadAsync(mountPath, password, _reauthKeyfilePath);

                ExitLockedState();
            }
            catch (Exception ex)
            {
                RegisterFailedLockscreenAttempt(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanAttemptLockscreenUnlock()
        {
            if (!_lockscreenLockedUntilUtc.HasValue)
            {
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= _lockscreenLockedUntilUtc.Value)
            {
                ResetLockscreenUnlockFailures();
                return true;
            }

            LockscreenError = $"Too many failed unlock attempts. Try again after {_lockscreenLockedUntilUtc.Value.ToLocalTime():G}.";
            return false;
        }

        private void ResetLockscreenUnlockFailures()
        {
            _lockscreenFailedUnlockAttempts = 0;
            _lockscreenLockedUntilUtc = null;
        }

        private void RegisterFailedLockscreenAttempt(string? baseMessage)
        {
            if (string.IsNullOrWhiteSpace(baseMessage))
            {
                return;
            }

            var settings = SettingsService.Load();
            var maxAttempts = settings.MaxFailedUnlockAttempts;
            if (!maxAttempts.HasValue || maxAttempts.Value <= 0)
            {
                LockscreenError = baseMessage;
                return;
            }

            _lockscreenFailedUnlockAttempts++;

            if (_lockscreenFailedUnlockAttempts >= maxAttempts.Value)
            {
                _lockscreenLockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10);
                _lockscreenFailedUnlockAttempts = 0;
                LockscreenError = $"{baseMessage} Too many failed unlock attempts. Try again after {_lockscreenLockedUntilUtc.Value.ToLocalTime():G}.";
                return;
            }

            var remainingAttempts = maxAttempts.Value - _lockscreenFailedUnlockAttempts;
            var attemptWord = remainingAttempts == 1 ? "attempt" : "attempts";
            LockscreenError = $"{baseMessage} {remainingAttempts} {attemptWord} remaining before temporary lockout.";
        }

        private void OwnerWindowOnClosed(object? sender, EventArgs e)
        {
            if (_autoLockSubscribed)
            {
                _vaultLockDurationService.VaultLocked -= OnVaultLocked;
                _autoLockSubscribed = false;
            }

            _usbDetector.RemovableDriveRemoved -= OnUsbRemoved;

            _vaultLockDurationService.SetActiveSession(null);
            ClearSensitiveState();
            _ownerWindow = null;
        }

        /// <summary>
        /// Loads the encrypted vault database from the mounted container.
        /// Uses zero-knowledge decryption to read the vault.pvault file.
        /// </summary>
        public async Task LoadAsync(string mountPath, string password, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _mountPath = mountPath;
                _vaultPassword = password;
                _vaultKeyfilePath = keyfilePath;

                if (!string.IsNullOrWhiteSpace(_manifestPath) && File.Exists(_manifestPath))
                {
                    try
                    {
                        var runtimeManifest = _manifestService.ReadManifest(_manifestPath, password, keyfilePath);
                        ApplyManifestTransportState(runtimeManifest, _manifestPath);
                    }
                    catch
                    {
                        // Best effort: vault loading can continue even if master volume metadata can't be re-read here.
                    }
                }

                // Clear password parameter immediately after copying to field (security best practice)
                password = null!;

                _vaultLockDurationService.SetActiveSession(null);

                // Clear existing items
                Items.Clear();
                _credentials.Clear();

                if (!Directory.Exists(mountPath))
                {
                    StatusMessage = "Mount path does not exist";
                    return;
                }

                // Path to the encrypted vault database
                _vaultFilePath = Path.Combine(mountPath, "vault.pvault");
                if (!File.Exists(_vaultFilePath))
                {
                    var nestedVaultPath = Path.Combine(mountPath, "vaults", "vault.pvault");
                    if (File.Exists(nestedVaultPath))
                    {
                        _vaultFilePath = nestedVaultPath;
                    }
                }

                if (!File.Exists(_vaultFilePath))
                {
                    StatusMessage = "Vault database not found";
                    return;
                }

                VaultDatabase? database;

                // CRITICAL: Check if decoy vault is active
                if (_tamperDetectionService?.IsDecoyActive == true && _decoyVaultService?.IsDecoyActive == true)
                {
                    // Return decoy database instead of real vault
                    database = _decoyVaultService.DecoyDatabase;

                    if (database == null)
                    {
                        StatusMessage = "Decoy vault not initialized";
                        return;
                    }
                }
                else
                {
                    // Normal operation: Decrypt and load the vault database using ZK service
                    // The ZK service should already be unlocked by SignInDialogViewModel
                    if (!_zkVaultService.IsUnlocked)
                    {
                        StatusMessage = "Zero-knowledge vault service is not unlocked";
                        return;
                    }

                    Stream stream;
                    if (IsPhantomContainerFile(_vaultFilePath))
                    {
                        await using var encryptedPayloadStream = await _vaultService.OpenVaultPayloadStreamAsync(
                            _vaultFilePath,
                            _vaultPassword,
                            _vaultKeyfilePath,
                            cancellationToken).ConfigureAwait(false);
                        stream = await _zkVaultService.OpenEncryptedStreamForViewingAsync(encryptedPayloadStream, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        stream = await _zkVaultService.OpenFileStreamForViewingAsync(_vaultFilePath, null, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await using (stream.ConfigureAwait(false))
                    {
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                        // Deserialize the vault database structure
                        database = JsonSerializer.Deserialize<VaultDatabase>(json);
                    }

                    if (database == null)
                    {
                        StatusMessage = "Failed to parse vault database";
                        return;
                    }
                }

                // Load credentials from all groups
                if (database.Groups != null)
                {
                    foreach (var group in database.Groups)
                    {
                        if (group.Entries != null)
                        {
                            foreach (var credential in group.Entries)
                            {
                                // Set the group name from the parent group
                                credential.Group = group.Name;
                                _credentials.Add(new CredentialViewModel(credential));
                            }
                        }
                    }
                }

                // Update UI
                UpdateCategoryCounts();

                // Apply manifest category colors so they appear immediately
                LoadCategoryColorsFromManifest();

                // Set "Logins" as the default category filter
                SelectCategory("Logins");

                // Load file system entries for the Items list
                foreach (var entry in Directory.GetFileSystemEntries(mountPath))
                {
                    Items.Add(Path.GetFileName(entry));
                }

                StatusMessage = $"Loaded {_credentials.Count} credentials";

                RegisterAutoLockSession();

                // Issue #1: arm auto-lock only when a PIN has been configured.
                // Without a PIN the only fallback is the full passphrase, which
                // is a poor surprise for a user who hasn't opted in to a PIN
                // session. Recomputed here on every vault open so it picks up
                // PIN setup that happened in a previous session.
                try
                {
                    var lockSettings = SettingsService.Load();
                    bool pinConfigured = lockSettings != null
                        && lockSettings.EnablePinLock
                        && PinLockService.HasPinConfigured(lockSettings, _manifestPath);
                    _vaultLockDurationService.AutoLockEnabled = pinConfigured;
                }
                catch
                {
                    // Fail closed: if we can't read settings, disable auto-lock
                    // rather than surprise-locking the user.
                    _vaultLockDurationService.AutoLockEnabled = false;
                }

                _vaultLockDurationService.UnlockVault();

                // Start idle-lock timer. Fires IdleElapsed after 15 minutes of inactivity,
                // which is handled in App to lock and wipe the vault.
                _idleLockService.Reset();

                // Analyze passwords and update Security Dashboard
                UpdateWeakCredentials();

                // Check if key rotation is required
                CheckRotationStatus();

                // Initialize TOTP synchronization service
                await InitializeTotpSyncAsync();

                // Load dashboard data so quick access tiles are populated on startup
                if (_isShowingDashboard)
                {
                    _ = DashboardViewModel.LoadDashboardDataAsync();
                    DashboardViewModel.OwnerWindow = _ownerWindow;
                    DashboardViewModel.ClipboardGuard = _clipboardGuard;
                    DashboardViewModel.LoadQuickAccessCredentials(_credentials, _categories);
                    DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);
                }

                await PersistCredentialIndexAsync(!string.IsNullOrWhiteSpace(_masterVolumePath));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VaultLoad] LoadAsync threw {ExType} — mountPath={MountPath}", ex.GetType().Name, mountPath);
                await _dialogService.ShowErrorAsync(
                    "Load Failed",
                    $"Failed to load vault database: {ex.Message}",
                    _ownerWindow);
                StatusMessage = "Failed to load vault";
            }
        }

        /// <summary>
        /// Legacy method for loading file system entries only (for backwards compatibility).
        /// For full vault loading with credential decryption, use LoadAsync instead.
        /// </summary>
        public void Load(string mountPath)
        {
            _mountPath = mountPath;
            Items.Clear();
            if (Directory.Exists(mountPath))
            {
                foreach (var entry in Directory.GetFileSystemEntries(mountPath))
                {
                    Items.Add(Path.GetFileName(entry));
                }
            }
        }

        /// <summary>
        /// Sets the owner window for dialog display.
        /// </summary>
        public void SetOwnerWindow(Window window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            if (_ownerWindow != null)
            {
                _ownerWindow.Closed -= OwnerWindowOnClosed;
            }

            _ownerWindow = window;
            _ownerWindow.Closed += OwnerWindowOnClosed;

            if (!_autoLockSubscribed)
            {
                _vaultLockDurationService.VaultLocked += OnVaultLocked;
                _autoLockSubscribed = true;
            }
        }

        /// <summary>
        /// Gets all credentials for export.
        /// </summary>
        public List<Credential> GetAllCredentials()
        {
            return _credentials.Select(c => c.GetCredential()).ToList();
        }

        /// <summary>
        /// Adds a credential from import operation.
        /// </summary>
        public void AddCredentialFromImport(Credential credential)
        {
            PrivacyShield.DebugSensitive("VaultVM", () => $"AddCredentialFromImport called for: {credential.Title}");
            PrivacyShield.DebugInfo("VaultVM", $"IsPasskey: {credential.IsPasskey}");
            PrivacyShield.DebugInfo("VaultVM", $"Current credentials count: {_credentials.Count}");
            PrivacyShield.DebugInfo("VaultVM", $"Current filtered count: {FilteredCredentials.Count}");

            var credentialVm = new CredentialViewModel(credential);

            // Add to main collection
            _credentials.Add(credentialVm);
            PrivacyShield.DebugInfo("VaultVM", $"Added to _credentials. New count: {_credentials.Count}");

            UpdateFlaggedCredentials();

            // DIRECTLY add to appropriate collection based on type
            Dispatcher.UIThread.Post(() =>
            {
                if (credential.IsPasskey)
                {
                    PrivacyShield.DebugInfo("VaultVM", "DIRECTLY adding to Passkeys collection");
                    _passkeys.Add(credentialVm);
                    PrivacyShield.DebugInfo("VaultVM", $"Passkeys count now: {_passkeys.Count}");
                    this.RaisePropertyChanged(nameof(Passkeys));
                    this.RaisePropertyChanged(nameof(PasskeysCount));
                }
                else
                {
                    PrivacyShield.DebugInfo("VaultVM", "DIRECTLY adding to FilteredCredentials");
                    _filteredCredentials.Add(credentialVm);
                    PrivacyShield.DebugInfo("VaultVM", $"FilteredCredentials count now: {_filteredCredentials.Count}");
                    this.RaisePropertyChanged(nameof(FilteredCredentials));
                    this.RaisePropertyChanged(nameof(FilteredCount));
                    this.RaisePropertyChanged(nameof(IsEmpty));
                }

                // Force property change notifications
                this.RaisePropertyChanged(nameof(TotalCount));

                PrivacyShield.DebugInfo("VaultVM", "Raised all property notifications");
            });

            // Also update category counts
            UpdateCategoryCounts();

            // Save the vault to persist imported credentials
            _ = SaveVaultAsync();

            PrivacyShield.DebugInfo("VaultVM", "AddCredentialFromImport complete");
        }

        private async Task ShowFlaggedPasswordsAsync()
        {
            var flaggedSnapshot = CreateFlaggedCredentialList();
            if (flaggedSnapshot.Count == 0)
            {
                await _dialogService.ShowInfoAsync(
                    "Flagged Passwords",
                    "No flagged passwords were found. Passwords shorter than 12 characters are automatically flagged here so you can harden them.",
                    _ownerWindow);
                return;
            }

            UpdateFlaggedCredentials(flaggedSnapshot);
            CloseAllOverlays(); // Close any other open overlays
            IsFlaggedPanelVisible = true;
            StatusMessage = "Reviewing flagged passwords";
        }

        private void CloseFlaggedPasswordsPanel()
        {
            if (!IsFlaggedPanelVisible)
            {
                return;
            }

            IsFlaggedPanelVisible = false;
            StatusMessage = "Flagged passwords panel closed";
        }

        /// <summary>
        /// Opens the PhantomRecovery application integrated into PhantomVault
        /// </summary>
        private async Task OpenPhantomRecoveryAsync()
        {
            await Task.CompletedTask;

            if (!_integratedRecoveryService.IsAvailable)
            {
                StatusMessage = ExternalRecoveryStatus;
                return;
            }

            var resolution = GetRecoveryVaultResolution();
            if (!resolution.Resolved)
            {
                StatusMessage = resolution.Message;
                return;
            }

            var recoveryVaultPath = resolution.VaultPath;
            StageRecoveryBootstrapArtifacts(recoveryVaultPath);
            if (_integratedRecoveryService.TryLaunch(recoveryVaultPath, out var errorMessage))
            {
                StatusMessage = $"Opened PhantomRecovery for {recoveryVaultPath}.";
                return;
            }

            StatusMessage = errorMessage ?? "Failed to launch PhantomRecovery.";
        }

        private void ApplyManifestTransportState(VaultManifest manifest, string manifestPath)
        {
            _masterVolumePath = manifest.MasterVolumePath;
            _protectionTier = manifest.ProtectionTier;
            _effectiveStorageTransport = manifest.EffectiveStorageTransport;
            _requestedStorageTransport = manifest.RequestedStorageTransport;
            _transportLayoutRoot = TryResolveTransportLayoutRoot(manifestPath)
                ?? TryResolveTransportLayoutRoot(manifest.RootContainerPath);
        }

        private static bool IsPhantomContainerFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            Span<byte> magic = stackalloc byte[8];
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < magic.Length)
                    return false;

                stream.ReadExactly(magic);
                return Encoding.ASCII.GetString(magic) == "PHANTOM1";
            }
            catch
            {
                return false;
            }
        }

        private static string? TryResolveTransportLayoutRoot(string? manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                return null;

            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestDirectory))
                return null;

            var parent = Directory.GetParent(manifestDirectory);
            return parent?.FullName;
        }

        /// <summary>
        /// Toggles the embedded recovery panel visibility
        /// </summary>
        private void ToggleRecoveryPanel()
        {
            if (!IsEmbeddedRecoveryAvailable)
            {
                if (IsExternalRecoveryAvailable)
                {
                    _ = OpenPhantomRecoveryAsync();
                }
                else
                {
                    StatusMessage = EmbeddedRecoveryStatus;
                    IsRecoveryPanelVisible = false;
                }
                return;
            }

            IsRecoveryPanelVisible = !IsRecoveryPanelVisible;
            if (IsRecoveryPanelVisible)
            {
                EnsureVaultVisible();
                // Close other panels when opening recovery
                IsEditPanelVisible = false;
                IsSettingsPanelVisible = false;
                IsFlaggedPanelVisible = false;
                IsIconManagerPanelVisible = false;
                StatusMessage = "Recovery panel opened";
            }
            else
            {
                StatusMessage = "Recovery panel closed";
            }
        }

        /// <summary>
        /// Closes the recovery panel
        /// </summary>
        public void CloseRecoveryPanel()
        {
            if (IsRecoveryPanelVisible)
            {
                IsRecoveryPanelVisible = false;
                StatusMessage = "Recovery panel closed";
            }
        }

        private RecoveryVaultResolution GetRecoveryVaultResolution()
        {
            if (!TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                return new RecoveryVaultResolution(
                    false,
                    string.Empty,
                    false,
                    false,
                    "Open this vault first so PhantomRecovery can resolve its suite workspace.");
            }

            try
            {
                var manifest = _manifestService.ReadManifest(manifestPath, passphrase, keyfilePath);
                return _recoveryVaultPathResolver.Resolve(manifestPath, manifest, passphrase, keyfilePath);
            }
            catch (Exception ex)
            {
                return new RecoveryVaultResolution(
                    false,
                    string.Empty,
                    false,
                    false,
                    $"Failed to resolve the PhantomRecovery workspace from this vault: {ex.Message}");
            }
        }

        private void StageRecoveryBootstrapArtifacts(string recoveryVaultPath)
        {
            if (!TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                return;
            }

            try
            {
                var manifest = _manifestService.ReadManifest(manifestPath, passphrase, keyfilePath);
                _recoverySuiteBootstrapService.StageBootstrapArtifacts(
                    manifestPath,
                    manifest,
                    passphrase,
                    keyfilePath,
                    recoveryVaultPath);
            }
            catch
            {
                // Best effort only; failure to stage bootstrap metadata should not block Recovery launch.
            }
        }

        public void DismissFlaggedPasswordsPanel()
        {
            CloseFlaggedPasswordsPanel();
        }

        private void UpdateFlaggedCredentials(List<CredentialViewModel>? snapshot = null)
        {
            var flagged = snapshot ?? CreateFlaggedCredentialList();

            void Apply()
            {
                FlaggedCredentials = new ObservableCollection<CredentialViewModel>(flagged);
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply);
            }
        }

        private List<CredentialViewModel> CreateFlaggedCredentialList()
        {
            return _credentials
                .Where(c => c.ShowPasswordFlag)
                .OrderBy(c => GetFlagPriority(c.PasswordFlagText))
                .ThenBy(c => c.Title)
                .ToList();
        }

        private static int GetFlagPriority(string? flagText)
        {
            return flagText switch
            {
                "Weak" => 0,
                "OK" => 1,
                _ => 2
            };
        }

        private void ApplyFiltersSynchronous()
        {
            var filtered = _credentials.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.ToLower();
                filtered = filtered.Where(c =>
                    c.Title.ToLower().Contains(search) ||
                    c.Username.ToLower().Contains(search) ||
                    c.Group.ToLower().Contains(search) ||
                    (!string.IsNullOrEmpty(c.Url) && c.Url.ToLower().Contains(search)));
            }

            // Apply view filter
            if (IsShowingFavorites)
            {
                filtered = filtered.Where(c => c.IsFavorite);
            }
            else if (IsShowingRecent)
            {
                filtered = filtered.OrderByDescending(c => c.LastUpdatedUtc).Take(10);
            }
            else if (IsShowingExpiringSoon)
            {
                var now = DateTimeOffset.UtcNow;
                var threshold = now.AddDays(30);
                filtered = filtered.Where(c =>
                {
                    var cred = c.GetCredential();
                    return cred.ExpiryUtc.HasValue &&
                           cred.ExpiryUtc.Value > now &&
                           cred.ExpiryUtc.Value <= threshold;
                }).OrderBy(c => c.GetCredential().ExpiryUtc);
            }

            // Apply sorting
            filtered = SortOption switch
            {
                0 => filtered.OrderBy(c => c.Title), // Name A-Z
                1 => filtered.OrderByDescending(c => c.Title), // Name Z-A
                2 => filtered.OrderByDescending(c => c.CreatedUtc), // Recently Added
                3 => filtered.OrderByDescending(c => c.LastUpdatedUtc), // Recently Modified
                4 => filtered.OrderBy(c => c.Group).ThenBy(c => c.Title), // Category
                _ => filtered
            };

            // Replace entire collection to force UI rebind
            FilteredCredentials = new ObservableCollection<CredentialViewModel>(filtered);
        }

        private void OpenSettingsPanel()
        {
            // Entry point from the top-level "Settings" button. Always lands on
            // General; tab switches inside the panel go through the individual
            // Show*Settings handlers and reuse EnsureSettingsPanelOpen.
            ShowGeneralSettings();
            StatusMessage = "Settings opened";
        }

        /// <summary>
        /// Guarantees the settings overlay is visible and records the active
        /// pane key. Safe to call when the panel is already open (no overlay
        /// churn). When the panel is closed it first dismisses any other
        /// overlay so the settings panel takes focus cleanly.
        /// </summary>
        private void EnsureSettingsPanelOpen(string tabKey)
        {
            if (!IsSettingsPanelVisible)
            {
                CloseAllOverlays();
                IsSettingsPanelVisible = true;
            }
            SelectedSettingsTab = tabKey;
        }

        // Issue #21: prompt the user when there are staged but unsaved settings
        // changes before the overlay closes. Returns to caller via the
        // CloseSettingsPanelCommand wrapper.
        private async Task CloseSettingsPanelAsync()
        {
            if (_settingsDraftTracker.HasUnsavedChanges)
            {
                // The existing two-button confirmation dialog maps to
                // "Save & Close" (true) / "Cancel" (false). False means stay
                // on the settings panel so the user can review.
                bool save = await _dialogService.ShowConfirmationAsync(
                    "Some settings have changed",
                    "Save the staged changes before closing? Click Cancel to stay and review.",
                    confirmText: "Save & Close",
                    cancelText: "Cancel",
                    owner: _ownerWindow);
                if (!save) return; // user wants to keep editing
                _settingsDraftTracker.CommitAll();
                StatusMessage = "Settings saved";
            }

            IsSettingsPanelVisible = false;
            SelectedSettingsContent = null;
            if (!_settingsDraftTracker.HasUnsavedChanges)
                StatusMessage = "Settings closed";
        }

        private void SaveStagedSettings()
        {
            if (!_settingsDraftTracker.HasUnsavedChanges) return;
            int n = _settingsDraftTracker.CommitAll();
            StatusMessage = n == 1 ? "Setting saved" : $"Saved {n} settings";
        }

        // Issue #31: tab-switch unsaved-changes intercept. If the user has
        // staged changes on the currently-active tab (or anywhere — the
        // tracker is overlay-wide), prompt before swapping tab content.
        // Returns true if the switch should proceed, false to stay on the
        // current tab.
        private async Task SwitchTabIfAllowedAsync(Action switchAction)
        {
            if (_settingsDraftTracker.HasUnsavedChanges)
            {
                bool save = await _dialogService.ShowConfirmationAsync(
                    "Some settings have changed",
                    "Save the staged changes before switching tabs? Click Cancel to stay and review.",
                    confirmText: "Save & Switch",
                    cancelText: "Cancel",
                    owner: _ownerWindow);
                if (!save) return; // stay on current tab
                _settingsDraftTracker.CommitAll();
                StatusMessage = "Settings saved";
            }
            switchAction();
        }

        private void DiscardStagedSettings()
        {
            if (!_settingsDraftTracker.HasUnsavedChanges) return;
            int n = _settingsDraftTracker.DiscardAll();
            StatusMessage = n == 1 ? "Setting discarded" : $"Discarded {n} pending settings";
        }

        private void ToggleSecurityDashboard()
        {
            IsSecurityDashboardVisible = !IsSecurityDashboardVisible;

            if (IsSecurityDashboardVisible)
            {
                _ = CloseSettingsPanelAsync();
                // Refresh all dashboard metrics when opened
                UpdateWeakCredentials();
                RefreshSecurityDashboardMetrics();
            }
            StatusMessage = IsSecurityDashboardVisible ? "Security Dashboard opened" : "Security Dashboard closed";
        }

        private void OpenSecurityDashboardWindow()
        {
            System.Diagnostics.Debug.WriteLine(">>> OpenSecurityDashboardWindow called");
            StatusMessage = "🔍 Opening Security Dashboard window...";
            
            try
            {
                // Close the summary dashboard overlay
                if (IsSecurityDashboardVisible)
                {
                    IsSecurityDashboardVisible = false;
                }

                // Refresh metrics before opening the full window
                UpdateWeakCredentials();
                RefreshSecurityDashboardMetrics();

                var window = new Views.SecurityDashboardWindow { DataContext = this };
                System.Diagnostics.Debug.WriteLine(">>> SecurityDashboardWindow created");

                // Show the window directly (don't use ShowDialog with owner to avoid visibility issues)
                window.Show();
                System.Diagnostics.Debug.WriteLine(">>> SecurityDashboardWindow shown");
                
                StatusMessage = "✓ Security Dashboard opened successfully";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! ERROR: {ex.Message}\n{ex.StackTrace}");
                StatusMessage = $"❌ ERROR: {ex.Message}";
            }
        }

        private void UpdateWeakCredentials()
        {
            WeakCredentials.Clear();

            foreach (var credVm in _credentials)
            {
                var cred = credVm.GetCredential();
                if (string.IsNullOrEmpty(cred.Password)) continue;

                // Simple password strength analysis
                var strength = AnalyzePasswordStrength(cred.Password);

                if (strength.Severity >= 1) // Medium, High, or Critical issues
                {
                    WeakCredentials.Add(new PhantomVault.UI.Desktop.Controls.WeakCredentialItem
                    {
                        Id = cred.Title, // Use Title as identifier
                        Title = cred.Title,
                        Username = cred.Username,
                        MaskedPassword = new string('\u2022', Math.Min(cred.Password.Length, 12)),
                        IssueLabel = strength.Label,
                        SeverityColor = strength.Color,
                        Severity = strength.Severity
                    });
                }
            }

            // Sort by severity (Critical first)
            var sorted = WeakCredentials.OrderByDescending(c => c.Severity).ToList();
            WeakCredentials.Clear();
            foreach (var item in sorted)
            {
                WeakCredentials.Add(item);
            }
        }

        /// <summary>
        /// Refreshes all security dashboard metrics: score, password issues, 2FA coverage, etc.
        /// Called when dashboard is opened or refreshed.
        /// </summary>
        private void RefreshSecurityDashboardMetrics()
        {
            try
            {
                IsRefreshingDashboard = true;

                // Calculate security score (0-100)
                int score = 100;
                int totalCredentials = _credentials.Count;

                if (totalCredentials == 0)
                {
                    SecurityScore = 50; // Default score for empty vault
                    BreachedPasswordCount = 0;
                    ReusedPasswordCount = 0;
                    ExpiringCredentialCount = 0;
                    TwoFactorEnabledCount = 0;
                    LastBreachCheckTime = null;
                    IsRefreshingDashboard = false;
                    FilteredCredentialsForDashboard.Clear();
                    return;
                }

                // Count weak/breached passwords
                int weakCount = WeakCredentials.Count;
                score -= (weakCount * 5); // 5 points per weak password

                // Count reused passwords (simple check: same password appears multiple times)
                var passwordFrequency = _credentials
                    .Where(c => !string.IsNullOrEmpty(c.Password))
                    .GroupBy(c => c.Password)
                    .Where(g => g.Count() > 1)
                    .ToList();

                int reusedCount = 0;
                foreach (var group in passwordFrequency)
                {
                    reusedCount += group.Count() - 1; // Count duplicates only
                }
                ReusedPasswordCount = reusedCount;
                score -= (reusedCount * 3); // 3 points per reused password

                // Count expiring credentials (next 30 days)
                var now = DateTimeOffset.UtcNow;
                var expiringThreshold = now.AddDays(30);
                int expiringCount = _credentials
                    .Where(c =>
                    {
                        var cred = c.GetCredential();
                        return cred.ExpiryUtc.HasValue &&
                               cred.ExpiryUtc.Value >= now &&
                               cred.ExpiryUtc.Value <= expiringThreshold;
                    })
                    .Count();
                ExpiringCredentialCount = expiringCount;
                score -= (expiringCount * 2); // 2 points per expiring credential

                // Count 2FA enabled credentials
                int twoFactorCount = _credentials
                    .Where(c => c.HasTotpSecret)
                    .Count();
                TwoFactorEnabledCount = twoFactorCount;

                // Award points for 2FA coverage
                if (twoFactorCount > 0)
                {
                    int twoFactorBonus = Math.Min((twoFactorCount * 2), 15);
                    score += twoFactorBonus;
                }

                // Breached-password count: keep last known value while a fresh
                // k-anonymous HIBP check runs asynchronously. The HIBP service
                // only transmits the first 5 chars of each SHA-1 hash, never
                // the password itself, so this is safe to fire-and-forget.
                int breachedCount = _breachedPasswordCount;
                score -= (breachedCount * 10); // 10 points per breached password

                // Clamp score to 0-100 range
                SecurityScore = Math.Clamp(score, 0, 100);

                // Update last breach check time
                LastBreachCheckTime = DateTime.UtcNow;

                // Populate dashboard credentials list with scores
                PopulateDashboardCredentials();

                // Kick off a background HIBP refresh. The result will update
                // BreachedPasswordCount and SecurityScore on the UI thread.
                _ = RefreshBreachCountAsync();
            }
            finally
            {
                IsRefreshingDashboard = false;
            }
        }

        // Lazily-constructed singleton HIBP client; disposed with the VM.
        private HaveIBeenPwnedService? _hibpService;
        // Guards against overlapping background breach scans.
        private int _breachCheckRunning;

        /// <summary>
        /// Runs a k-anonymous Have I Been Pwned check across all non-empty
        /// passwords currently loaded in the vault and updates
        /// <see cref="BreachedPasswordCount"/> + <see cref="SecurityScore"/>
        /// when finished. Fails closed: on network error the previous count
        /// is preserved.
        /// </summary>
        private async Task RefreshBreachCountAsync()
        {
            // Single-flight: skip if a scan is already in progress.
            if (Interlocked.Exchange(ref _breachCheckRunning, 1) == 1)
                return;

            try
            {
                var passwords = _credentials
                    .Select(c => c.GetCredential().Password)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (passwords.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        BreachedPasswordCount = 0;
                        LastBreachCheckTime = DateTime.UtcNow;
                    });
                    return;
                }

                var hibp = _hibpService ??= new HaveIBeenPwnedService();
                int breached = 0;
                foreach (var pw in passwords)
                {
                    int hits = await hibp.CheckPasswordBreachAsync(pw).ConfigureAwait(false);
                    if (hits > 0) breached++;
                    // CheckPasswordBreachAsync returns -1 on transport failure;
                    // treat as "unknown" (do not count) per fail-closed policy.
                }

                int finalBreached = breached;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    int oldBreached = BreachedPasswordCount;
                    BreachedPasswordCount = finalBreached;
                    // Adjust security score for the delta (10 pts per breached pw).
                    int delta = (oldBreached - finalBreached) * 10;
                    SecurityScore = Math.Clamp(SecurityScore + delta, 0, 100);
                    LastBreachCheckTime = DateTime.UtcNow;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultViewModel] HIBP background refresh failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _breachCheckRunning, 0);
            }
        }

        private void PopulateDashboardCredentials()
        {
            FilteredCredentialsForDashboard.Clear();

            foreach (var credVM in _credentials)
            {
                var cred = credVM.GetCredential();
                int credScore = CalculateCredentialSecurityScore(credVM, cred);

                var dashboardItem = CreateDashboardCredentialItem(credVM, cred, credScore);
                FilteredCredentialsForDashboard.Add(dashboardItem);
            }
        }

        private void ApplySecurityFilters()
        {
            FilteredCredentialsForDashboard.Clear();

            if (ShowAllFilter)
            {
                // Show all credentials
                PopulateDashboardCredentials();
                return;
            }

            // Apply selected filters
            foreach (var credVM in _credentials)
            {
                var cred = credVM.GetCredential();
                int credScore = CalculateCredentialSecurityScore(credVM, cred);

                // Check if this credential matches any selected filter
                bool matches = false;
                if (_showCriticalFilter && credScore >= 0 && credScore <= 40)
                    matches = true;
                else if (_showLowFilter && credScore >= 41 && credScore <= 60)
                    matches = true;
                else if (_showMediumFilter && credScore >= 61 && credScore <= 80)
                    matches = true;
                else if (_showGoodFilter && credScore >= 81 && credScore <= 100)
                    matches = true;

                if (matches)
                {
                    var dashboardItem = CreateDashboardCredentialItem(credVM, cred, credScore);
                    FilteredCredentialsForDashboard.Add(dashboardItem);
                }
            }
        }

        private DashboardCredentialItem CreateDashboardCredentialItem(
            CredentialViewModel credVM,
            PhantomVault.Core.Models.Credential cred,
            int credScore)
        {
            var iconBitmap = credVM.AutoDetectedIconBitmap;
            string categoryColor = ResolveDashboardCategoryColor(cred);
            return new DashboardCredentialItem(cred, credVM.HasTotpSecret, credScore, iconBitmap, categoryColor);
        }

        private string ResolveDashboardCategoryColor(PhantomVault.Core.Models.Credential cred)
        {
            string? categoryName = string.IsNullOrWhiteSpace(cred.Group) ? cred.Category : cred.Group;
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var categoryVm = _categories.FirstOrDefault(category =>
                    string.Equals(category.Name, categoryName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(categoryVm?.TileColor))
                {
                    return categoryVm.TileColor;
                }
            }

            if (!string.IsNullOrWhiteSpace(cred.IconColor))
            {
                return cred.IconColor;
            }

            return "#999999";
        }

        private int CalculateCredentialSecurityScore(CredentialViewModel credVM, PhantomVault.Core.Models.Credential cred)
        {
            int score = PasswordStrengthEvaluator.Evaluate(cred.Password).Score;

            // Check if password is reused
            int reusedCount = _credentials.Count(c => c.GetCredential().Password == cred.Password && c.GetCredential().Id != cred.Id);
            if (reusedCount > 0)
                score -= 25;

            // Check if expired or expiring soon
            if (cred.ExpiryUtc.HasValue)
            {
                var now = DateTimeOffset.UtcNow;
                if (cred.ExpiryUtc.Value < now)
                    score -= 50;
                else if (cred.ExpiryUtc.Value < now.AddDays(30))
                    score -= 15;
            }

            // Add bonus for 2FA
            if (credVM.HasTotpSecret)
                score += 12;

            return Math.Clamp(score, 0, 100);
        }

        private bool IsPasswordWeak(string password)
        {
            return PasswordStrengthEvaluator.Evaluate(password).IsWeak;
        }

        private (int Severity, string Label, Avalonia.Media.IBrush Color) AnalyzePasswordStrength(string password)
        {
            var assessment = PasswordStrengthEvaluator.Evaluate(password);
            string label = assessment.Severity switch
            {
                3 => "CRITICAL - Predictable",
                2 => "HIGH - Weak",
                1 => "MEDIUM - Improve Entropy",
                _ => "LOW"
            };

            var color = Avalonia.Media.Color.Parse(assessment.ColorHex);
            return (assessment.Severity, label, new Avalonia.Media.SolidColorBrush(color).ToImmutable());
        }

        #region Password Strength Tester

        private void UpdatePasswordTestResults()
        {
            if (string.IsNullOrEmpty(_testPassword))
            {
                TestPasswordScore = 0;
                TestPasswordStrengthLabel = "";
                TestPasswordStrengthColor = "#808080";
                SuggestedPassword = "";
                SimilarityPercentage = 0;
                return;
            }

            var assessment = PasswordStrengthEvaluator.Evaluate(_testPassword);
            TestPasswordScore = assessment.Score;
            TestPasswordStrengthLabel = assessment.Label;
            TestPasswordStrengthColor = assessment.ColorHex;
            GenerateSuggestedPassword();
        }

        private int CalculatePasswordScore(string password)
        {
            return PasswordStrengthEvaluator.Evaluate(password).Score;
        }

        private void GenerateSuggestedPassword()
        {
            if (string.IsNullOrEmpty(_testPassword))
                return;

            string suggested = PasswordStrengthEvaluator.GenerateSuggestedPassword(_testPassword);
            SuggestedPassword = suggested;
            SimilarityPercentage = string.IsNullOrEmpty(suggested)
                ? 0
                : CalculateSimilarity(_testPassword, suggested);
        }

        private int CalculateSimilarity(string original, string suggested)
        {
            if (string.IsNullOrEmpty(original)) return 0;

            // Simple similarity: count matching characters at same position
            int matches = 0;
            for (int i = 0; i < Math.Min(original.Length, suggested.Length); i++)
            {
                if (char.ToLower(original[i]) == char.ToLower(suggested[i]))
                    matches++;
            }

            return (matches * 100) / Math.Max(original.Length, suggested.Length);
        }

        private void CopySuggestedPassword()
        {
            if (string.IsNullOrEmpty(_suggestedPassword))
                return;

            try
            {
                if (_ownerWindow?.Clipboard is IClipboard clipboard)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await clipboard.SetTextAsync(_suggestedPassword);
                        StatusMessage = "✓ Suggested password copied to clipboard";
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to copy: {ex.Message}";
            }
        }

        #endregion

        private void ShowGeneralSettings()
        {
            EnsureSettingsPanelOpen("general");
            SelectedSettingsContent = new Views.Settings.GeneralSettingsView();
        }

        private void ShowSecuritySettings()
        {
            EnsureSettingsPanelOpen("security");
            var view = new Views.Settings.SecuritySettingsView();
            view.DataContext = new SecuritySettingsViewModel(null, _manifestPath);
            SelectedSettingsContent = view;
        }

        private void ShowThemeSettings()
        {
            EnsureSettingsPanelOpen("theme");
            SelectedSettingsContent = new Views.Settings.ThemeSettingsView();
        }

        private void ShowImportExportSettings()
        {
            EnsureSettingsPanelOpen("importexport");
            SelectedSettingsContent = new Views.Settings.ImportExportSettingsView();
        }

        private async Task OpenImportWindowAsync()
        {
            if (_ownerWindow == null) return;

            try
            {
                // Pass existing credentials to detect duplicates during import
                var existingCreds = _credentials.Select(c => c.GetCredential()).ToList();
                var importWindow = new ImportWindow(existingCreds);
                importWindow.ImportCompleted += (_, importedCreds) =>
                {
                    foreach (var cred in importedCreds)
                    {
                        AddCredentialFromImport(cred);
                    }
                };
                await importWindow.ShowDialog(_ownerWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Import Error", $"Failed to open import window: {ex.Message}", _ownerWindow);
            }
        }

        private async Task ExportVaultAsync(string format)
        {
            if (_ownerWindow == null) return;

            try
            {
                // Get all credentials for export
                var credentials = GetAllCredentials();
                var exportWindow = new ExportWindow(credentials);

                // Pre-select the format based on the button clicked
                if (exportWindow.DataContext is ExportViewModel exportVm)
                {
                    exportVm.SelectedFormat = format switch
                    {
                        "csv" => "CSV",
                        "keepass" => "KeePass XML",
                        _ => "JSON"
                    };
                }

                await exportWindow.ShowDialog(_ownerWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Export Error", $"Failed to open export window: {ex.Message}", _ownerWindow);
            }
        }

        private void ShowAutoFillSettings()
        {
            EnsureSettingsPanelOpen("autofill");
            SelectedSettingsContent = new Views.Settings.AutoFillSettingsView();
        }

        private void ShowPasswordHealth()
        {
            OpenPasswordHealthPanel();
        }

        private void ShowSyncSettings()
        {
            EnsureSettingsPanelOpen("sync");
            SelectedSettingsContent = new Views.Settings.SyncSettingsView
            {
                DataContext = new ViewModels.Settings.SyncSettingsViewModel()
            };
        }

        private void EnsureVaultVisible()
        {
            IsShowingCategoryLanding = false;
            IsShowingDashboard = false;
        }

        private void OpenPasswordHealthPanel()
        {
            CloseAllOverlays(); // Close any other open overlays
            EnsureVaultVisible();
            IsPasswordHealthPanelVisible = true;
            IsSidebarCollapsed = false;  // Expand sidebar

            var healthVm = new PasswordHealthViewModel(new PhantomVault.Core.Services.PasswordHealthService());
            foreach (var cred in _credentials)
            {
                healthVm.Credentials.Add(cred.GetCredential());
            }
            PasswordHealthPanelViewModel = healthVm;
        }

        private void ClosePasswordHealthPanel()
        {
            IsPasswordHealthPanelVisible = false;
        }

        private void OpenSecurityOptions()
        {
            OpenSettingsPanel();
            ShowSecuritySettings();
        }

        private void ShowBackupSettings()
        {
            EnsureSettingsPanelOpen("backup");
            var view = new Views.Settings.BackupSettingsView();
            Func<(string manifestPath, string? passphrase, string? keyfilePath)?> resolver = () =>
            {
                if (TryGetManifestContext(out var path, out var pass, out var keyfile))
                    return (path, pass, keyfile);
                return null;
            };

            var backupVm = new UI.ViewModels.Settings.BackupSettingsViewModel(
                secureBackupManager: ((App)Application.Current!).Services?.GetService(typeof(UI.Services.SecureBackupManager)) as UI.Services.SecureBackupManager,
                dialogService: new UI.Services.DialogService(),
                owner: _ownerWindow,
                manifestContextResolver: resolver);

            view.DataContext = backupVm;
            SelectedSettingsContent = view;
        }

        private void ShowAccessibilitySettings()
        {
            EnsureSettingsPanelOpen("accessibility");
            SelectedSettingsContent = new Views.Settings.AccessibilitySettingsView();
        }

        private void ShowAdvancedSettings()
        {
            EnsureSettingsPanelOpen("advanced");
            SelectedSettingsContent = new Views.Settings.AdvancedSettingsView();
        }

        private void ShowRubbishBinSettings()
        {
            EnsureSettingsPanelOpen("rubbishbin");
            SelectedSettingsContent = new Views.Settings.RubbishBinSettingsView();
        }

        private void OpenIconManager()
        {
            // Use the injected singleton IconManager
            IconManagerViewModel = new IconManagerViewModel(_iconManager);
            IsIconManagerPanelVisible = true;
            StatusMessage = "Icon Manager opened";
        }

        private void CloseIconManager()
        {
            IsIconManagerPanelVisible = false;
            IconManagerViewModel = null;
            StatusMessage = "Icon Manager closed";
        }

        private async void OnUsbRemoved(string path)
        {
            try
            {
                await _vaultLockDurationService.LockVaultAsync(LockReason.UsbRemoved);
                ClearSensitiveState();
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = "USB removed - vault locked";
                    _ownerWindow?.Close();
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to lock after USB removal: {ex.Message}";
            }
        }

        /// <summary>
        /// Checks if key rotation is required based on vault manifest LastKeyRotation timestamp.
        /// Updates IsRotationRequired and DaysSinceRotation properties to control the rotation banner visibility.
        /// Called automatically after vault LoadAsync completes.
        /// </summary>
        public void CheckRotationStatus()
        {
            if (_rekeyService == null)
            {
                IsRotationRequired = false;
                return;
            }

            if (TryGetManifestContext(out var manifestPath, out _, out _))
            {
                try
                {
                    var manifest = _manifestService.ReadManifest(manifestPath, _vaultPassword, _vaultKeyfilePath);
                    IsRotationRequired = _rekeyService.IsRotationRequired(manifest);
                    DaysSinceRotation = (int)(DateTimeOffset.UtcNow - manifest.LastKeyRotation).TotalDays;
                }
                catch
                {
                    IsRotationRequired = false;
                }
            }
        }

        private async Task RotateNowAsync()
        {
            if (_rekeyService == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Service Unavailable",
                    "Key rotation service is not available.",
                    _ownerWindow);
                return;
            }

            if (!TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                await _dialogService.ShowErrorAsync(
                    "Manifest Not Found",
                    "Cannot find vault manifest. Ensure the vault is properly mounted.",
                    _ownerWindow);
                return;
            }

            var confirm = await _dialogService.ShowConfirmationAsync(
                "Rotate Encryption Keys",
                $"This will generate new encryption keys for your vault.\n\nLast rotation: {DaysSinceRotation} days ago\n\nThis operation may take several minutes. Continue?",
                _ownerWindow);

            if (!confirm)
                return;

            IsBusy = true;
            StatusMessage = "Rotating encryption keys...";

            try
            {
                var vaultPath = _vaultFilePath ?? Path.Combine(_mountPath, "vault.pvault");
                var progress = new Progress<RekeyProgress>(p =>
                {
                    StatusMessage = p.Message;
                });

                var result = await _rekeyService.PerformRekeyAsync(
                    vaultPath,
                    manifestPath,
                    keyfilePath ?? string.Empty,
                    passphrase,
                    null, // usbSerial
                    progress,
                    CancellationToken.None);

                if (result.Success)
                {
                    // Update cached keyfile path if changed
                    if (!string.IsNullOrEmpty(result.NewKeyfilePath))
                    {
                        _vaultKeyfilePath = result.NewKeyfilePath;
                    }

                    IsRotationRequired = false;
                    IsRotationBannerDismissed = true;
                    DaysSinceRotation = 0;

                    await _dialogService.ShowSuccessAsync(
                        "Rotation Complete",
                        "Vault encryption keys have been successfully rotated.\n\nYour vault is now protected with fresh cryptographic keys.",
                        _ownerWindow);

                    StatusMessage = "Key rotation completed successfully";
                }
                else
                {
                    throw result.Error ?? new Exception("Key rotation failed");
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Rotation Failed",
                    $"Failed to rotate encryption keys: {ex.Message}",
                    _ownerWindow);
                StatusMessage = $"Rotation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void DismissRotationBanner()
        {
            IsRotationBannerDismissed = true;
        }

        #region Security Methods

        private void OnThreatLevelChanged(object? sender, ThreatLevelChangedEventArgs e)
        {
            CurrentThreatLevel = e.CurrentLevel;

            SecurityStatus = e.CurrentLevel switch
            {
                SecurityThreatLevel.None => "Secure",
                SecurityThreatLevel.Low => "Low Risk",
                SecurityThreatLevel.Medium => "Medium Risk",
                SecurityThreatLevel.High => "High Risk",
                SecurityThreatLevel.Critical => "Critical Threat",
                _ => "Unknown"
            };

            ShowSecurityAlert = e.CurrentLevel >= SecurityThreatLevel.Medium;

            // Log threat
            Debug.WriteLine($"[Security] Threat level changed: {e.PreviousLevel} → {e.CurrentLevel}");
            Debug.WriteLine($"[Security] Tamper: {e.Check.TamperCheckResult.GetDescription()}");
            Debug.WriteLine($"[Security] Keylogging: {e.Check.KeyloggingCheckResult.GetDescription()}");
        }

        private async void OnCriticalThreatDetected(object? sender, CriticalThreatEventArgs e)
        {
            if (_securityCoordinator == null)
                return;

            // Determine action
            var action = await _securityCoordinator.RespondToThreatAsync(e.Check);

            // Show alert
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _dialogService.ShowWarningAsync(
                    "Security Threat Detected",
                    action.Message,
                    _ownerWindow);
            });

            // Take action
            if (action.ShouldLockVault)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    StatusMessage = "Vault locked due to security threat";

                    // Lock vault using security lockdown
                    if (!string.IsNullOrEmpty(_mountPath))
                    {
                        try
                        {
                            var result = await _vaultLockDurationService.LockVaultAsync(LockReason.SecurityThreat);
                            if (result.Success)
                            {
                                _items.Clear();
                                _ownerWindow?.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Security] Failed to lock vault: {ex.Message}");
                        }
                    }
                });
            }

            if (action.ShouldExitApplication)
            {
                if (_isDeveloperBypassMode)
                {
                    Debug.WriteLine("[Security] Exit suppressed in developer bypass mode.");
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Environment.Exit(1);
                });
            }
        }

        /// <summary>
        /// Starts security monitoring when vault is unlocked.
        /// </summary>
        private void StartSecurityMonitoring()
        {
            if (_securityCoordinator == null)
                return;

            _securityCoordinator.EnableMaximumSecurity();
            SecurityStatus = "Secure";
            CurrentThreatLevel = SecurityThreatLevel.None;
            ShowSecurityAlert = false;

            Debug.WriteLine("[Security] Monitoring started");
        }

        /// <summary>
        /// Stops security monitoring when vault is locked.
        /// </summary>
        private void StopSecurityMonitoring()
        {
            if (_securityCoordinator == null)
                return;

            _securityCoordinator.StopMonitoring();
            SecurityStatus = "Inactive";
            CurrentThreatLevel = SecurityThreatLevel.None;
            ShowSecurityAlert = false;

            Debug.WriteLine("[Security] Monitoring stopped");
        }

        #endregion

        #region USB Auto-Inject

        /// <summary>
        /// Initializes the USB auto-inject service
        /// </summary>
        private void InitializeAutoInject()
        {
            if (_autoInjectService == null)
                return;

            try
            {
                // Set the factory that creates credential provider on-demand
                _autoInjectService.SetCredentialProviderFactory(() =>
                    new VaultViewModelCredentialProvider(this));

                // Subscribe to events
                _autoInjectService.PromptRequired += OnAutoInjectPromptRequired;
                _autoInjectService.PasskeyReady += OnPasskeyReady;

                // Start the service
                _ = _autoInjectService.StartAsync();

                Debug.WriteLine("[VaultViewModel] Auto-inject service initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultViewModel] Failed to initialize auto-inject: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles auto-inject prompt events when credentials are matched
        /// </summary>
        private void OnAutoInjectPromptRequired(object? sender, AutoInjectPromptEventArgs e)
        {
            // Show the prompt window on UI thread
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var owner = GetOwnerWindowForDialog();
                    if (owner == null)
                    {
                        Debug.WriteLine("[VaultViewModel] Cannot show auto-inject prompt: no owner window");
                        return;
                    }

                    var promptWindow = new AutoInjectPromptWindow
                    {
                        DataContext = e  // Window binds to Matches array
                    };

                    var result = await promptWindow.ShowDialog<bool?>(owner);

                    if (result == true && promptWindow.SelectedCredential != null)
                    {
                        await _autoInjectService!.AutoFillAsync(
                            promptWindow.SelectedCredential.CredentialId,
                            e.Policy.AutoSubmit);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VaultViewModel] Auto-inject prompt error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handles passkey authentication events (silent)
        /// </summary>
        private void OnPasskeyReady(object? sender, PasskeyReadyEventArgs e)
        {
            // Handle silent passkey authentication
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var services = TryGetServiceProvider();
                    var passkeyService = services?.GetService(typeof(IPasskeyService)) as IPasskeyService
                        ?? services?.GetService(typeof(PasskeyService)) as IPasskeyService;

                    if (passkeyService == null)
                    {
                        Debug.WriteLine("[VaultViewModel] PasskeyService not available");
                        return;
                    }

                    var credentialId = Convert.FromBase64String(e.CredentialId);
                    byte[] challenge = new byte[32];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

                    bool ok = await passkeyService.AuthenticateAsync(credentialId, e.Domain, challenge);
                    if (ok)
                    {
                        Debug.WriteLine($"[VaultViewModel] Passkey authenticated for {e.Domain}");
                        if (_autoInjectService != null)
                        {
                            await _autoInjectService.AutoFillAsync(e.CredentialId, autoSubmit: false);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[VaultViewModel] Passkey authentication failed for {e.Domain}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VaultViewModel] Passkey handler error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gets the owner window for dialogs
        /// </summary>
        private Window? GetOwnerWindowForDialog()
        {
            return _ownerWindow ??
                   (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }

        #region TOTP Synchronization

        /// <summary>
        /// Initializes the TOTP synchronization service for cross-app sync with PhantomAttestor
        /// </summary>
        private async Task InitializeTotpSyncAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_mountPath))
                {
                    return;
                }

                // Path to TOTP sync file on USB device
                var syncPath = Path.Combine(_mountPath, ".phantom", "vaults", "totp-sync.json");
                var syncDir = Path.GetDirectoryName(syncPath);

                // Create directory if it doesn't exist
                if (!string.IsNullOrEmpty(syncDir) && !Directory.Exists(syncDir))
                {
                    Directory.CreateDirectory(syncDir);
                }

                // Initialize sync service
                _totpSyncService = new PhantomVault.Core.Services.Sync.TotpSyncServiceObscura(syncPath);
                _totpSyncService.EntriesChanged += OnTotpEntriesChanged;
                await _totpSyncService.InitializeAsync();

                // Export Obscura's existing TOTP entries to sync file so Attestor can pick them up
                await ExportTotpToSyncAsync();

                StatusMessage = "TOTP sync initialized";
            }
            catch (Exception ex)
            {
                // Don't fail vault loading if TOTP sync fails
                System.Diagnostics.Debug.WriteLine($"Failed to initialize TOTP sync: {ex.Message}");
                StatusMessage = "TOTP sync unavailable";
            }
        }

        /// <summary>
        /// Exports all Obscura TOTP-enabled credentials to the shared sync file.
        /// Uses merge logic so Attestor entries are preserved.
        /// </summary>
        private async Task ExportTotpToSyncAsync()
        {
            try
            {
                if (_totpSyncService == null) return;

                // Extract TOTP entries from all credentials that have a TOTP secret
                var coreCredentials = _credentials
                    .Select(c => c.GetCredential())
                    .Where(c => !string.IsNullOrWhiteSpace(c.TotpSecret));

                var obscuraEntries = _totpSyncService.ExtractFromVault(coreCredentials);

                if (obscuraEntries.Count > 0)
                {
                    await _totpSyncService.ExportToSyncFileAsync(obscuraEntries);
                    System.Diagnostics.Debug.WriteLine($"Exported {obscuraEntries.Count} TOTP entries from Obscura to sync file");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export TOTP entries: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles TOTP entries changed event from PhantomAttestor
        /// </summary>
        private async void OnTotpEntriesChanged(object? sender, System.Collections.Generic.List<PhantomVault.Core.Models.SharedTotpEntry> entries)
        {
            try
            {
                // Update credentials with TOTP data from PhantomAttestor
                foreach (var entry in entries)
                {
                    var cred = _credentials.FirstOrDefault(c => c.Title == entry.LinkedPasswordEntryId);
                    if (cred != null)
                    {
                        var coreCred = cred.GetCredential();

                        // Update TOTP fields
                        coreCred.TotpSecret = entry.Secret;
                        coreCred.TotpDigits = entry.Digits;
                        coreCred.TotpTimeStep = entry.Period;
                        coreCred.TotpAlgorithm = entry.Algorithm;
                        coreCred.TotpIssuer = entry.Issuer;
                        coreCred.TotpAccountName = entry.AccountName;
                        coreCred.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        // Recreate ViewModel to refresh UI and reinitialize TOTP timer
                        var index = _credentials.IndexOf(cred);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCred);
                        _credentials.Insert(index, newCredentialVM);

                        // Update selected credential if it was the synced one
                        if (SelectedCredential?.Title == entry.LinkedPasswordEntryId)
                        {
                            SelectedCredential = newCredentialVM;
                        }
                    }
                }

                // Save updated credentials to vault
                await SaveVaultAsync();

                StatusMessage = $"Synced {entries.Count} TOTP entries from PhantomAttestor";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to sync TOTP entries: {ex.Message}");
            }
        }

        #endregion

        internal bool TryGetTierMigrationContext(out VaultTierMigrationContext? context)
        {
            context = null;
            if (string.IsNullOrWhiteSpace(_manifestPath) ||
                string.IsNullOrWhiteSpace(_vaultPassword) ||
                string.IsNullOrWhiteSpace(_transportLayoutRoot) ||
                string.IsNullOrWhiteSpace(_usbRootPath))
            {
                return false;
            }

            context = new VaultTierMigrationContext(
                _manifestPath,
                _vaultPassword,
                _vaultKeyfilePath,
                _usbRootPath,
                _transportLayoutRoot,
                _protectionTier,
                _effectiveStorageTransport,
                _requestedStorageTransport,
                _masterVolumePath);
            return true;
        }

        internal void ApplyTierMigrationResult(VaultTierMigrationResult result)
        {
            _usbRootPath = result.ActiveDevicePath;
            _masterVolumePath = result.MasterVolumePath;
            _protectionTier = result.ProtectionTier;
            _effectiveStorageTransport = result.EffectiveStorageTransport;
            _requestedStorageTransport = result.RequestedStorageTransport;
            if (!string.IsNullOrWhiteSpace(result.TransportLayoutRoot))
                _transportLayoutRoot = result.TransportLayoutRoot;

            StatusMessage = result.StatusMessage;
        }

        #endregion
    }
}

/// <summary>
/// Wrapper class for dashboard credential items with security score and status information.
/// </summary>
public class DashboardCredentialItem
{
    private readonly PhantomVault.Core.Models.Credential _credential;
    private readonly bool _hasTwoFactor;
    private readonly Avalonia.Media.Imaging.Bitmap? _icon;
    private readonly string _categoryColor;

    public DashboardCredentialItem(PhantomVault.Core.Models.Credential credential, bool hasTwoFactor, int securityScore, Avalonia.Media.Imaging.Bitmap? icon = null, string categoryColor = "#999999")
    {
        _credential = credential;
        _hasTwoFactor = hasTwoFactor;
        SecurityScore = securityScore;
        _icon = icon;
        _categoryColor = categoryColor;
    }

    public string CredentialTitle => _credential.Title ?? "Untitled";
    public string CredentialUsername => _credential.Username ?? "";
    public int SecurityScore { get; }
    public Avalonia.Media.Imaging.Bitmap? IconBitmap => _icon;
    public string CategoryColor => _categoryColor;
    public PhantomVault.Core.Models.Credential Credential => _credential;

    public string SecurityScoreColor => SecurityScore switch
    {
        >= 81 => "#4CAF50", // Green
        >= 61 => "#FFC107", // Amber
        >= 41 => "#FF9800", // Orange
        _ => "#F44336"       // Red
    };

    public ObservableCollection<StatusIndicator> StatusIndicators
    {
        get
        {
            var indicators = new ObservableCollection<StatusIndicator>();
            var passwordAssessment = PasswordStrengthEvaluator.Evaluate(_credential.Password);

            if (passwordAssessment.IsWeak)
                indicators.Add(new StatusIndicator { Label = "W", Tooltip = "Weak Password", Color = "#FF9800" });

            if (_credential.ExpiryUtc.HasValue && _credential.ExpiryUtc < DateTimeOffset.UtcNow)
                indicators.Add(new StatusIndicator { Label = "E", Tooltip = "Expired", Color = "#F44336" });
            else if (_credential.ExpiryUtc.HasValue && _credential.ExpiryUtc < DateTimeOffset.UtcNow.AddDays(30))
                indicators.Add(new StatusIndicator { Label = "X", Tooltip = "Expiring Soon", Color = "#FF9800" });

            if (_hasTwoFactor)
                indicators.Add(new StatusIndicator { Label = "2FA", Tooltip = "2FA Enabled", Color = "#2196F3" });

            return indicators;
        }
    }

    public bool IsTwoFactorEnabled => _hasTwoFactor;
    public string LastModifiedFormatted => _credential.ModifiedDate > DateTime.MinValue 
        ? _credential.ModifiedDate.ToString("yyyy-MM-dd") 
        : "N/A";
}

/// <summary>
/// Status indicator for dashboard display.
/// </summary>
public class StatusIndicator
{
    public string Label { get; set; } = "";
    public string Tooltip { get; set; } = "";
    public string Color { get; set; } = "#666";
}
