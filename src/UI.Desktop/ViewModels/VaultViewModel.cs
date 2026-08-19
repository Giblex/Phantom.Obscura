using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
using Serilog;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.Core.Utils;
using PhantomVault.UI.Services;
using PhantomVault.UI.Models;
using PhantomVault.UI.Views;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Helpers;
using PhantomVault.UI.Desktop.Controls;

namespace PhantomVault.UI.ViewModels
{

    public sealed partial class VaultViewModel : ReactiveObject, PhantomVault.Core.Services.Sync.ITotpSyncBridge
    {
        private readonly VaultService _vaultService;
        private readonly PhantomVault.UI.Services.Entitlements.IEntitlementService? _entitlementService;
        private readonly ManifestService _manifestService;
        private readonly IdleLockService _idleLockService;
        private Phantom.Suite.Session.ISuiteSessionCoordinator? _suiteSession;
        private readonly IZkVaultService _zkVaultService;
        private readonly DialogService _dialogService;
        private readonly VaultLockDurationService _vaultLockDurationService;
        private readonly PhantomVault.UI.Services.SettingsDraftTracker _settingsDraftTracker;
        private readonly IUsbDetector _usbDetector;
        private readonly SecureTrashService _secureTrashService;
        private readonly IconManager _iconManager;
        private readonly IClipboardGuard? _clipboardGuard;
        private CancellationTokenSource? _clipboardClearCts;
        private CancellationTokenSource? _settingsSaveNotificationCts;
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

        private PhantomVault.Core.Services.Sync.TotpSyncServiceObscura? _totpSyncService;

        private string _mountPath = string.Empty;

        private string? _vaultFilePath;
        // Password is optional — vaults may use keyfile-only auth.
        // Stored as SecurePassword (pinned GC memory, zeroed on lock) so it
        // does not persist as a plain string for the lifetime of the session.
        private SecurePassword? _vaultPassword;
        private string? _vaultKeyfilePath;
        /// <summary>Extracts vault password as a transient string for APIs that require <c>string?</c>.
        /// The returned string is not stored — it is GC-eligible immediately after use.</summary>
        /// <summary>
        /// The keyfile this session was unlocked with, or a hard failure.
        ///
        /// Reading or rewriting a volume needs the keyfile — it is the mandatory factor, and
        /// there is no password-only path to fall back to. Throwing here rather than passing
        /// null means a bug that loses the keyfile surfaces as an immediate, obvious failure
        /// instead of a container written under the wrong key.
        /// </summary>
        private string RequireVaultKeyfile()
            => string.IsNullOrWhiteSpace(_vaultKeyfilePath)
                ? throw new InvalidOperationException("The vault keyfile is not available in this session.")
                : _vaultKeyfilePath;

        private string? GetVaultPasswordString()
            => _vaultPassword == null || _vaultPassword.IsEmpty ? null : new string(_vaultPassword.AsSpan());
        // Cached runtime manifest from the unlock flow — lets SetManifestContext + LoadAsync skip
        // their own ReadManifest calls (each one runs a fresh Argon2id KDF ≈ 0.5–2s).
        private VaultManifest? _cachedRuntimeManifest;
        // True while LoadAsync is running in the background after the vault window has been
        // shown. The UI can bind a loading overlay to this.
        private bool _isLoadingCredentials;
        public bool IsLoadingCredentials
        {
            get => _isLoadingCredentials;
            set => this.RaiseAndSetIfChanged(ref _isLoadingCredentials, value);
        }
        private bool _isPersistingCredentialIndex;
        // Monotonic vault save counter (persisted inside the encrypted payload) and the
        // host-local DPAPI anchor used to detect whole-volume rollback at unlock.
        private long _vaultSaveSequence;
        private readonly VolumeTrustAnchorStore _volumeTrustAnchorStore = new();
        private string? _volumeTrustVaultId;
        // Encrypted virtual-drive mount (WinFsp). Null until first use; disposed on lock.
        private PhantomVault.UI.Services.Mount.PhantomMountService? _mountService;
        // Tracks the standalone dashboard window so live credential changes can refresh it while open.
        private Window? _securityDashboardWindow;
        private ObservableCollection<string> _items = new();
        private ObservableCollection<CredentialViewModel> _credentials = new();
        private ObservableCollection<CredentialViewModel> _filteredCredentials = new();
        private ObservableCollection<ListItemWrapper> _groupedListItems = new();
        private ObservableCollection<CredentialViewModel> _passkeys = new();
        private ObservableCollection<CredentialViewModel> _flaggedCredentials = new();
        private ObservableCollection<CategoryViewModel> _categories = new();
        private ObservableCollection<SecureTrashItemViewModel> _filteredTrashItems = new();
        private CredentialViewModel? _selectedCredential;
        private SecureTrashItemViewModel? _selectedTrashItem;
        private bool _isBusy;
        private string _status = string.Empty;
        private string _vaultName = "My Vault";
        private string _searchText = string.Empty;
        private string _currentViewTitle = "All Items";
        private string _statusMessage = "Ready";
        private string _settingsSaveNotification = "Settings saved";
        private string _lastSyncTime = DateTime.Now.ToString("HH:mm");
        private int _sortOption = 0;
        private bool _isShowingAll = false;
        private bool _isShowingPasswords = false;
        private bool _isShowingFavorites = false;
        private bool _isShowingPasskeys = false;
        private bool _isShowingRecent = false;
        private bool _isShowingExpiringSoon = false;
        private bool _isShowingDashboard = false;
        private bool _isDashboardEnabled = true;
        private bool _startWithWindows;
        private bool _globalHotkeyEnabled;
        private bool _isSettingsSaveNotificationVisible;
        private bool _isInitializing = true;
        private bool _privacyModeEnabled;
        private bool _isPasswordHealthPanelVisible;
        private string _viewModeIcon = "☰";
        private string _gridViewIconPath = "Assets/SVG/Current/Grid.svg";
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
        private Core.Models.EntryType? _currentEntryType = null;

        private bool _isSidebarCollapsed = true;
        private bool _isSidebarAutoCollapsed = false;

        private bool _showCategoryColorBarOnly;
        private bool _showEntryIcons = true;
        private bool _showCategoryColors = true;

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

        private string? _usbRootPath;
        private string? _manifestPath;

        public string? ManifestPath => _manifestPath;

        internal string? CurrentUsbBindingId =>
            _cachedRuntimeManifest?.UsbBindingId ?? _cachedRuntimeManifest?.UsbBindingGuid;

        internal PhantomVault.UI.Services.Entitlements.IEntitlementService? EntitlementService => _entitlementService;

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

        private readonly Stack<(string ActionType, object Payload)> _undoStack = new();
        private readonly Stack<(string ActionType, object Payload)> _redoStack = new();

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
            PhantomVault.UI.Services.SettingsDraftTracker? settingsDraftTracker = null,
            PhantomVault.UI.Services.Entitlements.IEntitlementService? entitlementService = null)
        {
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
            _entitlementService = entitlementService
                ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.Entitlements.IEntitlementService)) as PhantomVault.UI.Services.Entitlements.IEntitlementService);
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _idleLockService = idleLockService ?? throw new ArgumentNullException(nameof(idleLockService));
            _zkVaultService = zkVaultService ?? throw new ArgumentNullException(nameof(zkVaultService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _vaultLockDurationService = vaultLockDurationService ?? throw new ArgumentNullException(nameof(vaultLockDurationService));

            _settingsDraftTracker = settingsDraftTracker
                ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.SettingsDraftTracker)) as PhantomVault.UI.Services.SettingsDraftTracker)
                ?? new PhantomVault.UI.Services.SettingsDraftTracker();

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
            // DI-resolved (registered as a singleton in App.Composition.cs) rather than
            // "new()" when not explicitly passed, so the switcher's tracked-process state is
            // shared with DashboardViewModel's instance instead of each holding its own.
            _integratedAttestorService = integratedAttestorService
                ?? ((Application.Current as App)?.Services?.GetService(typeof(IntegratedAttestorService)) as IntegratedAttestorService)
                ?? new IntegratedAttestorService();
            _integratedAttestorService.RunningStateChanged += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    this.RaisePropertyChanged(nameof(IsAttestorRunning));
                    this.RaisePropertyChanged(nameof(AttestorStatus));
                });
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

            if (_securityCoordinator != null)
            {
                _securityCoordinator.ThreatLevelChanged += OnThreatLevelChanged;
                _securityCoordinator.CriticalThreatDetected += OnCriticalThreatDetected;
            }

            if (_autoInjectService != null)
            {
                InitializeAutoInject();
            }

            InitializeCategories();

            _usbDetector.RemovableDriveRemoved += OnUsbRemoved;

            _idleLockService.IdleElapsed += OnIdleLockElapsed;

            // Keyless suite session: presence is reported on unlock/lock and suite-wide
            // lock is honoured via App (subscribed once at app scope) calling RequestSuiteLock.
            _suiteSession = TryResolve<Phantom.Suite.Session.ISuiteSessionCoordinator>();

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
            AddBlankEntryCommand = ReactiveCommand.CreateFromTask(async () => await AddCredentialWithTypeAsync("Blank"));
            DashboardViewModel = new DashboardViewModel();
            DashboardViewModel.NavigateToFilter = NavigateToVaultWithFilter;
            DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);
            DashboardViewModel.SetAttestorAvailability(_integratedAttestorService.IsAvailable, _integratedAttestorService.AvailabilityMessage);
            InitializeDuplicateAndSectionSupport();
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
            // Surface silently-swallowed ReactiveCommand exceptions so the user sees why a Recovery click did nothing.
            OpenPhantomRecoveryCommand.ThrownExceptions.Subscribe(ex =>
            {
                Log.Error(ex, "[Recovery] Failed to open Phantom Recovery.");
                StatusMessage = "Phantom Recovery could not be opened. Try again or review Recent Issues.";
            });
            ToggleRecoveryPanelCommand.ThrownExceptions.Subscribe(ex =>
            {
                Log.Error(ex, "[Recovery] Failed to toggle the recovery panel.");
                StatusMessage = "The recovery panel could not be opened. Try again.";
            });
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

            OpenCommandPaletteCommand = ReactiveCommand.Create(() => { });
            OpenSettingsPanelCommand = ReactiveCommand.Create(OpenSettingsPanel);

            CloseSettingsPanelCommand = ReactiveCommand.CreateFromTask(CloseSettingsPanelAsync);

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

                var credVM = _credentials.FirstOrDefault(c => c.GetCredential().Id == credential.Id);
                Views.EditPasswordWindow.ShowDialog(null, credential, credVM, this);
            });
            RefreshSecurityDashboard = ReactiveCommand.Create(() => RefreshSecurityDashboardMetrics());
            CopySuggestedPasswordCommand = ReactiveCommand.Create(CopySuggestedPassword);

            FilterShowAllCommand = ReactiveCommand.Create(() => { ShowAllFilter = true; ShowCriticalFilter = false; ShowLowFilter = false; ShowMediumFilter = false; ShowGoodFilter = false; });
            FilterCriticalCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowCriticalFilter = !ShowCriticalFilter; });
            FilterLowCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowLowFilter = !ShowLowFilter; });
            FilterMediumCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowMediumFilter = !ShowMediumFilter; });
            FilterGoodCommand = ReactiveCommand.Create(() => { ShowAllFilter = false; ShowGoodFilter = !ShowGoodFilter; });

            ShowGeneralSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowGeneralSettings));
            ShowSecuritySettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSecuritySettings));
            ShowThemeSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowThemeSettings));
            ShowSubscriptionSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSubscriptionSettings));
            ShowImportExportSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowImportExportSettings));
            ShowAutoFillSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAutoFillSettings));
            ShowBackupSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowBackupSettings));
            ShowAccessibilitySettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAccessibilitySettings));
            ShowAdvancedSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowAdvancedSettings));
            ShowRubbishBinSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowRubbishBinSettings));
            ShowRecentIssuesCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowRecentIssues));
            ShowSecurityActivityCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSecurityActivity));
            ShowPasswordHealthCommand = ReactiveCommand.Create(ShowPasswordHealth);
            ShowSyncSettingsCommand = ReactiveCommand.CreateFromTask(() => SwitchTabIfAllowedAsync(ShowSyncSettings));
            OpenIconManagerCommand = ReactiveCommand.Create(OpenIconManager);
            CloseIconManagerCommand = ReactiveCommand.Create(CloseIconManager);
            RotateNowCommand = ReactiveCommand.CreateFromTask(RotateNowAsync);
            ChangeMasterPasswordCommand = ReactiveCommand.CreateFromTask(ChangeMasterPasswordAsync);
            RegenerateKeyfileCommand = ReactiveCommand.CreateFromTask(RegenerateKeyfileAsync);
            ChangeKeyfileCommand = ReactiveCommand.CreateFromTask(ChangeKeyfileAsync);
            BackupKeyfileCommand = ReactiveCommand.CreateFromTask(BackupKeyfileAsync);
            DismissRotationBannerCommand = ReactiveCommand.Create(DismissRotationBanner);

            OpenImportWindowCommand = ReactiveCommand.CreateFromTask(OpenImportWindowAsync);
            ExportVaultCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("json"));
            ExportVaultCsvCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("csv"));
            ExportKeePassCommand = ReactiveCommand.CreateFromTask(() => ExportVaultAsync("keepass"));

            PrivacyModeEnabled = PrivacyShield.PrivacyModeEnabled;
            PrivacyShield.PrivacyModeChanged += OnPrivacyModeChanged;

            try
            {
                var s = SettingsService.Load();
                _showCategoryColorBarOnly = s.ShowCategoryColorBarOnly;
                _showEntryIcons = s.ShowEntryIcons;
                _showCategoryColors = s.ShowCategoryColors;
            }
            catch { _showCategoryColorBarOnly = false; }
            SettingsService.SettingsChanged += OnUserSettingsChanged;

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

            MountDriveReadOnlyCommand = ReactiveCommand.CreateFromTask(() => MountEncryptedDriveAsync(writable: false));
            MountDriveWritableCommand = ReactiveCommand.CreateFromTask(() => MountEncryptedDriveAsync(writable: true));
            UnmountDriveCommand = ReactiveCommand.Create(UnmountEncryptedDrive);

            UnlockWithPasswordCommand = ReactiveCommand.CreateFromTask(UnlockWithPasswordAsync);
            UnlockWithPinCommand = ReactiveCommand.CreateFromTask(UnlockWithPinAsync);
            ShowPassphraseFallbackCommand = ReactiveCommand.Create(ShowPassphraseFallback);
            DismissLockscreenCommand = ReactiveCommand.Create(DismissLockscreen);
            SetupPinLockCommand = ReactiveCommand.CreateFromTask(SetupPinLockAsync);

            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFilters());

            this.WhenAnyValue(x => x.SortOption)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFilters());

            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => _credentials.CollectionChanged += h,
                    h => _credentials.CollectionChanged -= h)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RefreshSecurityDashboardIfActive());

            CategoryLandingViewModel = new CategoryLandingViewModel(vaultService);

            MessageBus.Current.Listen<NavigateToVaultWithFilterMessage>()
                .Subscribe(msg => NavigateToVaultWithFilter(msg.FilterType));

            try
            {
                var userSettings = SettingsService.Load();
                _isDashboardEnabled = userSettings.DashboardEnabled;
                _startWithWindows = WindowsStartupRegistration.IsEnabled();
                _globalHotkeyEnabled = userSettings.GlobalHotkeyEnabled;
                _isGridView = userSettings.PreferGridView;
                _gridViewIconPath = _isGridView ? "Assets/SVG/Current/List.svg" : "Assets/SVG/Current/Grid.svg";
            }
            catch {  }

            if (_isDashboardEnabled)
            {
                CurrentViewTitle = "Passwords";
                _isShowingDashboard = false;
                _isShowingPasswords = true;
                IsSidebarCollapsed = false;
            }
            else
            {

                _isShowingDashboard = false;
                _isShowingPasswords = true;
                CurrentViewTitle = "Passwords";
                IsSidebarCollapsed = false;
            }

            if (IsShowingDashboard && _credentials.Count > 0)
            {
                DashboardViewModel.LoadQuickAccessCredentials(_credentials, _categories);
                DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);
            }

            _isInitializing = false;
        }

        public ReactiveCommand<Unit, Unit> OpenTrashManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> UndoCommand { get; }
        public ReactiveCommand<Unit, Unit> RedoCommand { get; }

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

            if (changed > 0)
            {
                _ = SaveVaultAsync();
            }

            return changed;
        }

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

                if (!_manifestService.TryReadManifest(manifestPath, null, null, out var manifest, out var manifestError))
                {

                    return null;
                }

                var deleted = manifest?.Categories?.FirstOrDefault(c => string.Equals(c.Name, "Deleted", StringComparison.OrdinalIgnoreCase));
                return deleted?.Name;
            }
            catch
            {
                return null;
            }
        }

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

        public void UpdateCategoriesFromModels(System.Collections.Generic.List<PhantomVault.Core.Models.CategoryModel> models)
        {
            if (models == null || models.Count == 0) return;
            var ordered = models.OrderBy(m => m.Order).ToList();
            var newList = new ObservableCollection<CategoryViewModel>();
            foreach (var m in ordered)
            {
                newList.Add(new CategoryViewModel { Name = m.Name, Icon = IconPathMigrator.Migrate(m.Icon), TileColor = m.TileColor });
            }

            Dispatcher.UIThread.Post(() =>
            {
                Categories = newList;
                UpdateCategoryActiveStates();
                UpdateCategoryCounts();
                ApplyFilters();
            });
        }

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

            if (entry.ActionType == "MoveToTrash")
            {
                var payload = ((string fromCategory, List<Credential> list))entry.Payload;

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
                this.RaisePropertyChanged(nameof(KeyfileUnlockAvailable));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
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

        /// <summary>
        /// The keyfile is the mandatory unlock factor and a passphrase is optional, so a
        /// vault with no passphrase must still be re-openable from the lockscreen. Without
        /// this the lockscreen offered only a passphrase box, rejected the empty input a
        /// keyfile-only vault legitimately has, and — with no PIN configured — left no way
        /// past the lock at all.
        /// </summary>
        public bool KeyfileUnlockAvailable =>
            IsLockscreenVisible
            && !string.IsNullOrWhiteSpace(_manifestPath)
            && !string.IsNullOrWhiteSpace(_reauthKeyfilePath);

        public bool IsDeveloperBypassMode => _isDeveloperBypassMode;

        public bool ShowLockscreenDismiss =>
            IsLockscreenVisible
            && IsDeveloperBypassMode
            && !PinUnlockAvailable
            && !KeyfileUnlockAvailable
            && !PasswordUnlockAvailable;

        public ReactiveCommand<Unit, Unit> DismissLockscreenCommand { get; }
        public ReactiveCommand<Unit, Unit> SetupPinLockCommand { get; }

        /// <summary>
        /// Whether the lockscreen already offers a way in without typing anything — a PIN
        /// pad or a keyfile. The passphrase is an optional additive factor, so it sits
        /// behind a link when one of these exists and is shown outright when neither does.
        /// </summary>
        private bool HasPrimaryUnlockFactor => PinUnlockAvailable || KeyfileUnlockAvailable;

        public bool ShowPassphraseFallbackSection
        {
            get
            {

                if (HasPrimaryUnlockFactor)
                    return _showPassphraseFallback && PasswordUnlockAvailable;

                return PasswordUnlockAvailable;
            }
        }

        public bool ShowPassphraseFallbackLink =>
            HasPrimaryUnlockFactor && PasswordUnlockAvailable && !_showPassphraseFallback;

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
            this.RaisePropertyChanged(nameof(KeyfileUnlockAvailable));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
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

        public string SettingsSaveNotification
        {
            get => _settingsSaveNotification;
            private set => this.RaiseAndSetIfChanged(ref _settingsSaveNotification, value);
        }

        public bool IsSettingsSaveNotificationVisible
        {
            get => _isSettingsSaveNotificationVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSettingsSaveNotificationVisible, value);
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

        public bool IsDashboardEnabled
        {
            get => _isDashboardEnabled;
            set
            {
                if (_isDashboardEnabled == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isDashboardEnabled, value);

                var previousPersisted = SafeLoadDashboardEnabled();
                if (value == previousPersisted)
                {
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
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "[Settings] DashboardEnabled commit failed.");
                                RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "Couldn't save setting", "The dashboard preference may not persist. Try saving it again.");
                            }
                        },
                        discard: () =>
                        {
                            this.RaiseAndSetIfChanged(ref _isDashboardEnabled, previousPersisted, nameof(IsDashboardEnabled));
                        });
                }

                if (!value && IsShowingDashboard)
                {
                    ShowPasswords();
                }
            }
        }

        private static bool SafeLoadDashboardEnabled()
        {
            try { return SettingsService.Load().DashboardEnabled; }
            catch { return true; }
        }

        public bool GlobalHotkeyEnabled
        {
            get => _globalHotkeyEnabled;
            set
            {
                if (_globalHotkeyEnabled == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _globalHotkeyEnabled, value);

                try
                {
                    var s = SettingsService.Load();
                    s.GlobalHotkeyEnabled = value;
                    SettingsService.Save(s);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Settings] GlobalHotkeyEnabled persist failed.");
                    RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "Couldn't save setting", "The global hotkey preference may not persist. Try saving it again.");
                }
            }
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (_startWithWindows == value)
                {
                    return;
                }

                var applied = WindowsStartupRegistration.Apply(value);
                if (!applied)
                {
                    this.RaisePropertyChanged(nameof(StartWithWindows));
                    return;
                }

                this.RaiseAndSetIfChanged(ref _startWithWindows, value);

                try
                {
                    var s = SettingsService.Load();
                    s.StartWithWindows = value;
                    SettingsService.Save(s);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Settings] StartWithWindows persist failed.");
                    RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "Couldn't save setting", "The start-with-Windows preference may not persist. Try saving it again.");
                }
            }
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

        public bool ShowCategoryColorBarOnly
        {
            get => _showCategoryColorBarOnly;
            private set => this.RaiseAndSetIfChanged(ref _showCategoryColorBarOnly, value);
        }

        public bool ShowEntryIcons
        {
            get => _showEntryIcons;
            private set => this.RaiseAndSetIfChanged(ref _showEntryIcons, value);
        }

        public bool ShowCategoryColors
        {
            get => _showCategoryColors;
            private set => this.RaiseAndSetIfChanged(ref _showCategoryColors, value);
        }

        private void OnUserSettingsChanged(object? sender, UserSettingsChangedEventArgs e)
        {
            try
            {

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ShowCategoryColorBarOnly = e.Settings.ShowCategoryColorBarOnly;
                    ShowEntryIcons = e.Settings.ShowEntryIcons;
                    ShowCategoryColors = e.Settings.ShowCategoryColors;
                });
            }
            catch {  }
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
            set
            {
                if (_privacyModeEnabled == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _privacyModeEnabled, value);

                // Master "go offline" switch. Drives the process-wide PrivacyShield,
                // which is wired to the internet gateway (revokes grants, refuses new
                // network consent) and persist the choice so it survives restarts.
                if (PrivacyShield.PrivacyModeEnabled != value)
                {
                    PrivacyShield.PrivacyModeEnabled = value;
                    try
                    {
                        SettingsService.Update(s => s.PrivacyModeEnabled = value);
                    }
                    catch
                    {
                        // Persistence is best-effort; the runtime switch already applied.
                    }
                }
            }
        }

        public ObservableCollection<CredentialViewModel> FilteredCredentials
        {
            get => _filteredCredentials;
            private set => this.RaiseAndSetIfChanged(ref _filteredCredentials, value);
        }

        public ObservableCollection<ListItemWrapper> GroupedListItems
        {
            get => _groupedListItems;
            private set => this.RaiseAndSetIfChanged(ref _groupedListItems, value);
        }

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
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCredential, value);
                this.RaisePropertyChanged(nameof(SelectedCredentialCategoryBrush));
            }
        }

        // Border brush for the detail card — resolves the selected credential's
        // Group to the owning CategoryViewModel.TileColor, so the detail card
        // wears the category's colour. Falls back to the accent brush when
        // there's no match (uncategorised, unknown group, bad hex) so the
        // border is always clearly visible.
        public Avalonia.Media.IBrush SelectedCredentialCategoryBrush
        {
            get
            {
                Avalonia.Media.IBrush ResolveFallback()
                {
                    var app = Avalonia.Application.Current;
                    if (app != null)
                    {
                        foreach (var key in new[] { "AccentBrush", "SystemAccentColorBrush", "ControlBorderBrush" })
                        {
                            if (app.Styles.TryGetResource(key, app.ActualThemeVariant, out var r) && r is Avalonia.Media.IBrush b)
                                return b;
                            if (app.Resources.TryGetResource(key, app.ActualThemeVariant, out var r2) && r2 is Avalonia.Media.IBrush b2)
                                return b2;
                        }
                    }
                    return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3B82F6"));
                }

                var group = _selectedCredential?.Group;
                if (string.IsNullOrWhiteSpace(group)) return ResolveFallback();

                var cat = _categories.FirstOrDefault(c =>
                    string.Equals(c.Name, group, StringComparison.OrdinalIgnoreCase));
                if (cat == null || string.IsNullOrWhiteSpace(cat.TileColor)) return ResolveFallback();

                try
                {
                    return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(cat.TileColor));
                }
                catch
                {
                    return ResolveFallback();
                }
            }
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

        public bool IsShowingCategoryFiltered => !string.IsNullOrEmpty(_activeCategory) && !IsShowingSecureTrash;

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

        public string AttestorStatus => _integratedAttestorService.IsRunning
            ? "Switch to PhantomAttestor"
            : _integratedAttestorService.AvailabilityMessage;

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

        private bool _showAllFilter = true;
        public bool ShowAllFilter
        {
            get => _showAllFilter;
            set
            {
                if (_showAllFilter != value)
                {
                    this.RaiseAndSetIfChanged(ref _showAllFilter, value);
                    ApplySecurityFilters();
                }
            }
        }

        private bool _showCriticalFilter = false;
        public bool ShowCriticalFilter
        {
            get => _showCriticalFilter;
            set
            {
                if (_showCriticalFilter != value)
                {
                    this.RaiseAndSetIfChanged(ref _showCriticalFilter, value);
                    ApplySecurityFilters();
                }
            }
        }

        private bool _showLowFilter = false;
        public bool ShowLowFilter
        {
            get => _showLowFilter;
            set
            {
                if (_showLowFilter != value)
                {
                    this.RaiseAndSetIfChanged(ref _showLowFilter, value);
                    ApplySecurityFilters();
                }
            }
        }

        private bool _showMediumFilter = false;
        public bool ShowMediumFilter
        {
            get => _showMediumFilter;
            set
            {
                if (_showMediumFilter != value)
                {
                    this.RaiseAndSetIfChanged(ref _showMediumFilter, value);
                    ApplySecurityFilters();
                }
            }
        }

        private bool _showGoodFilter = false;
        public bool ShowGoodFilter
        {
            get => _showGoodFilter;
            set
            {
                if (_showGoodFilter != value)
                {
                    this.RaiseAndSetIfChanged(ref _showGoodFilter, value);
                    ApplySecurityFilters();
                }
            }
        }

        private int _securityScore = 0;
        public int SecurityScore
        {
            get => _securityScore;
            private set
            {
                this.RaiseAndSetIfChanged(ref _securityScore, value);
                this.RaisePropertyChanged(nameof(SecurityScoreColor));
                this.RaisePropertyChanged(nameof(SecurityScoreLabel));
            }
        }

        public string SecurityScoreColor => _securityScore switch
        {
            >= 90 => "#22C55E",
            >= 75 => "#84CC16",
            >= 60 => "#EAB308",
            >= 40 => "#F97316",
            _ => "#EF4444"
        };

        public string SecurityScoreLabel => _securityScore switch
        {
            >= 90 => "Excellent",
            >= 75 => "Good",
            >= 60 => "Moderate",
            >= 40 => "Weak",
            _ => "Critical"
        };

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

        private double _averageEntropy = 0;
        public double AverageEntropy
        {
            get => _averageEntropy;
            private set
            {
                this.RaiseAndSetIfChanged(ref _averageEntropy, value);
                this.RaisePropertyChanged(nameof(AverageEntropyDisplay));
                this.RaisePropertyChanged(nameof(AverageEntropyLabel));
            }
        }

        // Rounded bits-of-entropy figure for the dashboard stat tile.
        public int AverageEntropyDisplay => (int)Math.Round(_averageEntropy);

        // Human-readable banding for the average entropy stat.
        public string AverageEntropyLabel => _averageEntropy switch
        {
            <= 0 => "—",
            < 40 => "Weak",
            < 60 => "Fair",
            < 80 => "Strong",
            _ => "Excellent"
        };

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

        private string _selectedSettingsTab = "general";
        public string SelectedSettingsTab
        {
            get => _selectedSettingsTab;
            private set => this.RaiseAndSetIfChanged(ref _selectedSettingsTab, value);
        }

        private string _settingsSearchQuery = string.Empty;
        public string SettingsSearchQuery
        {
            get => _settingsSearchQuery;
            set => this.RaiseAndSetIfChanged(ref _settingsSearchQuery, value ?? string.Empty);
        }

        public ReactiveCommand<string, Unit> OpenItemCommand { get; }
        public ReactiveCommand<Unit, Unit> LockCommand { get; }
        public ReactiveCommand<Unit, Unit> MountDriveReadOnlyCommand { get; }
        public ReactiveCommand<Unit, Unit> MountDriveWritableCommand { get; }
        public ReactiveCommand<Unit, Unit> UnmountDriveCommand { get; }
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
        public ReactiveCommand<Unit, Unit> AddBlankEntryCommand { get; }
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

        // True while we're tracking a live Attestor instance we launched — drives the
        // switcher chip in the header (shown instead of the "Open Attestor" launcher once
        // it's running, so clicking it re-focuses the existing window instead of relaunching).
        public bool IsAttestorRunning => _integratedAttestorService.IsRunning;

        private void OpenAttestor()
        {
            PixelBounds? ownerBounds = null;
            if (_ownerWindow != null)
            {
                try
                {
                    var scaling = _ownerWindow.RenderScaling;
                    ownerBounds = new PixelBounds(
                        _ownerWindow.Position.X,
                        _ownerWindow.Position.Y,
                        (int)(_ownerWindow.Width * scaling),
                        (int)(_ownerWindow.Height * scaling));
                }
                catch { }
            }

            if (_integratedAttestorService.TryLaunch(ownerBounds, out var errorMessage))
            {
                DashboardViewModel.SetAttestorAvailability(true, _integratedAttestorService.AvailabilityMessage);
                StatusMessage = _integratedAttestorService.IsRunning ? "Switched to PhantomAttestor." : "Opened PhantomAttestor.";
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

        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> DiscardSettingsCommand { get; }

        public bool HasUnsavedSettings => _settingsDraftTracker.HasUnsavedChanges;

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
        public ReactiveCommand<Unit, Unit> ShowSubscriptionSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowImportExportSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAutoFillSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowBackupSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAccessibilitySettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdvancedSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRubbishBinSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRecentIssuesCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSecurityActivityCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowPasswordHealthCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSyncSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenIconManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseIconManagerCommand { get; }
        public ReactiveCommand<Unit, Unit> RotateNowCommand { get; }
        public ReactiveCommand<Unit, Unit> ChangeMasterPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> RegenerateKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> ChangeKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> BackupKeyfileCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissRotationBannerCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenImportWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportVaultCsvCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportKeePassCommand { get; }

        public IReadOnlyList<CommandPaletteAction> BuildCommandPaletteActions()
        {

            static void Run(ReactiveCommand<Unit, Unit> cmd) => cmd.Execute().Subscribe(_ => { }, _ => { });

            var list = new List<CommandPaletteAction>
            {

                new("Lock vault now",            "Vault",    () => Run(LockCommand),
                    subtitle: "Clear keys and require keyfile + password again",
                    glyph: "🔒",
                    searchKeywords: "lock,logout,sign out,exit"),

                new("Mount encrypted drive (read-only)", "Vault", () => Run(MountDriveReadOnlyCommand),
                    subtitle: "Open the vault container as a safe, read-only Windows drive",
                    glyph: "💽",
                    searchKeywords: "mount,drive,winfsp,virtual,read only,explorer"),
                new("Mount encrypted drive (writable)", "Vault", () => Run(MountDriveWritableCommand),
                    subtitle: "Edit files on a virtual drive; changes are repacked on unmount",
                    glyph: "💽",
                    searchKeywords: "mount,drive,winfsp,virtual,writable,edit,explorer"),
                new("Unmount encrypted drive",   "Vault",    () => Run(UnmountDriveCommand),
                    subtitle: "Detach the virtual drive and commit any changes",
                    glyph: "⏏",
                    searchKeywords: "unmount,eject,detach,drive"),

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

                new("Open password health",      "Tools",    () => Run(OpenPasswordHealthPanelCommand),
                    glyph: "🩺", searchKeywords: "audit,strength,scan,duplicates"),
                new("Find and consolidate duplicates", "Tools", () => Run(OpenDuplicateScanCommand),
                    glyph: "🧬", searchKeywords: "duplicate,merge,consolidate,dedupe,clean up"),
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

                new("Toggle dark / light theme", "Theme",    () => Run(ToggleThemeCommand),
                    glyph: "🌓", searchKeywords: "appearance,dark mode,light mode"),
                new("Open theme settings",       "Theme",    () => Run(ShowThemeSettingsCommand),
                    glyph: "🎨", searchKeywords: "colour,style"),

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

                new("Import from file…",         "Import",   () => Run(OpenImportWindowCommand),
                    glyph: "📥", searchKeywords: "import,keepass,csv,1password,bitwarden"),
                new("Export vault (encrypted)",  "Export",   () => Run(ExportVaultCommand),
                    glyph: "📤", searchKeywords: "backup,export"),
                new("Export vault to CSV",       "Export",   () => Run(ExportVaultCsvCommand),
                    glyph: "📤", searchKeywords: "csv,spreadsheet"),
                new("Export to KeePass",         "Export",   () => Run(ExportKeePassCommand),
                    glyph: "📤", searchKeywords: "keepass,kdbx"),

                new("Open Phantom Recovery",     "Recovery", () => Run(OpenPhantomRecoveryCommand),
                    glyph: "🆘", searchKeywords: "recovery,phrase,backup"),
                new("Open trash manager",        "Recovery", () => Run(OpenTrashManagerCommand),
                    glyph: "🗑️", searchKeywords: "trash,undelete,restore"),
            };

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

            }
        }

        internal void LoadSampleCredentials()
        {

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
                },
                new Credential
                {
                    Title = "Garage Door Code",
                    Group = "PIN Codes",
                    EntryType = EntryType.PinCode,
                    PinLabel = "Garage Door",
                    PinValue = "4471",
                    PinCategory = "Home",
                    PinIssuer = "Chamberlain",
                    Notes = "Keypad on the side entrance",
                    CreatedUtc = DateTimeOffset.Now.AddMonths(-1)
                }
            };

            foreach (var cred in sampleCreds)
            {
                var vm = new CredentialViewModel(cred);

                if (_credentials.Count < 2)
                {
                    vm.IsFavorite = true;
                }
                _credentials.Add(vm);
            }

            UpdateCategoryCounts();

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

            var filtered = _credentials.Where(c => !c.GetCredential().IsPasskey).AsEnumerable();

            if (_currentEntryType.HasValue)
            {
                filtered = filtered.Where(c => c.GetCredential().EntryType == _currentEntryType.Value);
            }

            if (!string.IsNullOrEmpty(_activeCategory))
            {
                filtered = filtered.Where(c => string.Equals(c.Group, _activeCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchResults = new List<Services.SearchResult<CredentialViewModel>>();

                foreach (var credential in filtered)
                {

                    int titleScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Title);
                    int usernameScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Username);
                    int groupScore = Services.FuzzyMatcher.CalculateScore(SearchText, credential.Group);
                    int urlScore = !string.IsNullOrEmpty(credential.Url)
                        ? Services.FuzzyMatcher.CalculateScore(SearchText, credential.Url)
                        : 0;

                    var sectionText = credential.SectionSearchText;
                    int sectionScore = !string.IsNullOrEmpty(sectionText)
                        ? Services.FuzzyMatcher.CalculateScore(SearchText, sectionText)
                        : 0;

                    int maxScore = Math.Max(
                        Math.Max(Math.Max(titleScore, usernameScore), Math.Max(groupScore, urlScore)),
                        sectionScore);

                    if (maxScore >= 30)
                    {
                        searchResults.Add(new Services.SearchResult<CredentialViewModel>(credential)
                        {
                            Score = maxScore,
                            MatchedField = maxScore == titleScore ? "Title" :
                                          maxScore == usernameScore ? "Username" :
                                          maxScore == groupScore ? "Group" :
                                          maxScore == urlScore ? "URL" : "Section"
                        });
                    }
                }

                filtered = searchResults
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.Item.Title)
                    .Select(r => r.Item);
            }

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

            filtered = SortOption switch
            {
                0 => filtered.OrderBy(c => c.Title),
                1 => filtered.OrderByDescending(c => c.Title),
                2 => filtered.OrderByDescending(c => c.CreatedUtc),
                3 => filtered.OrderByDescending(c => c.LastUpdatedUtc),
                4 => filtered.OrderBy(c => c.Group).ThenBy(c => c.Title),
                _ => filtered
            };

            var materializedList = filtered.ToList();

            var groupedItems = new List<ListItemWrapper>();
            if (SortOption == 4)
            {
                var grouped = materializedList.GroupBy(c => c.Group ?? "Uncategorized");
                foreach (var group in grouped)
                {

                    var categoryVm = _categories.FirstOrDefault(cat =>
                        string.Equals(cat.Name, group.Key, StringComparison.OrdinalIgnoreCase));
                    var categoryColor = categoryVm?.TileColor;

                    groupedItems.Add(ListItemWrapper.CreateCategoryHeader(group.Key, categoryColor, group.Count()));

                    foreach (var cred in group)
                    {
                        groupedItems.Add(ListItemWrapper.CreateCredential(cred));
                    }
                }
            }
            else
            {

                foreach (var cred in materializedList)
                {
                    groupedItems.Add(ListItemWrapper.CreateCredential(cred));
                }
            }

            UpdateFlaggedCredentials();

            Dispatcher.UIThread.Post(() =>
            {

                FilteredCredentials = new ObservableCollection<CredentialViewModel>(materializedList);
                GroupedListItems = new ObservableCollection<ListItemWrapper>(groupedItems);

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

            await AddCredentialWithTypeAsync("0");
        }

        private async Task AddCredentialWithTypeAsync(string entryTypeValue)
        {
            Core.Models.EntryType entryType = Core.Models.EntryType.Password;
            var isSecureNote = false;

            if (!string.IsNullOrEmpty(entryTypeValue))
            {
                if (int.TryParse(entryTypeValue, out int typeInt) && Enum.IsDefined(typeof(Core.Models.EntryType), typeInt))
                {
                    entryType = (Core.Models.EntryType)typeInt;
                }
                else
                {

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
                            entryType = Core.Models.EntryType.Password;
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
                        case "blank":
                        case "blank entry":
                        case "custom":
                        case "custom entry":
                            entryType = Core.Models.EntryType.Blank;
                            break;
                        default:
                            entryType = Core.Models.EntryType.Password;
                            break;
                    }
                }
            }

            var newCredential = new Credential
            {
                EntryType = entryType
            };

            if (isSecureNote)
            {
                newCredential.Group = "Secure Notes";
            }

            CloseAllOverlays();
            EditViewModel = new AddEditCredentialViewModel(newCredential, (credential) =>
            {
                OnCredentialSaved(credential);
                CloseEditPanel();
            });
            EditViewModel.Categories = new ObservableCollection<CategoryViewModel>(Categories);

            IsEditPanelVisible = true;

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

            SelectedCredential = credentialVm;

            var editingTitle = credentialVm.Title;
            try
            {
                Debug.WriteLine($"[EDIT] Opening credential: {editingTitle}");
            }
            catch
            {

            }

            StatusMessage = string.IsNullOrWhiteSpace(editingTitle)
                ? "Editing credential"
                : $"Editing: {editingTitle}";

            CloseAllOverlays();
            EditViewModel = new AddEditCredentialViewModel(credentialVm.GetCredential(), (credential) =>
            {
                OnCredentialSaved(credential);
                CloseEditPanel();
            });
            EditViewModel.Categories = new ObservableCollection<CategoryViewModel>(Categories);

            IsEditPanelVisible = true;

            await Task.CompletedTask;
        }

        public void CloseEditPanel()
        {
            IsEditPanelVisible = false;
            EditViewModel = null;
        }

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

            AccountConsolidationService.Consolidate(
                credential,
                _credentials.Select(item => item.GetCredential()));

            var existing = _credentials.FirstOrDefault(c => c.GetCredential() == credential);

            if (existing != null)
            {

                var index = _credentials.IndexOf(existing);
                var updatedVm = new CredentialViewModel(credential);
                _credentials[index] = updatedVm;
                SelectedCredential = updatedVm;
                StatusMessage = $"Updated: {credential.Title}";
            }
            else
            {

                var newVm = new CredentialViewModel(credential);
                _credentials.Add(newVm);
                SelectedCredential = newVm;
                StatusMessage = $"Added: {credential.Title}";
            }

            UpdateCategoryCounts();
            ApplyFilters();

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

            await SaveVaultAsync();
        }

        /// <summary>
        /// Detects whole-volume rollback by comparing the monotonic save-sequence inside the
        /// decrypted vault payload against the host-local DPAPI trust anchor. Warn-and-allow:
        /// a rollback surfaces a prominent warning but never blocks the unlock, then the
        /// current state is re-baselined so the user is not nagged repeatedly.
        /// </summary>
        private async Task VerifyVolumeRollbackAsync(VaultDatabase database)
        {
            try
            {
                _vaultSaveSequence = database.SaveSequence;

                var manifest = _cachedRuntimeManifest;
                _volumeTrustVaultId = VolumeRollbackEvaluator.ComputeVaultId(
                    manifest?.DeviceId, manifest?.SaltBase64);

                if (_volumeTrustVaultId == null)
                    return;

                var knownSequence = _volumeTrustAnchorStore.TryReadSequence(_volumeTrustVaultId);
                var verdict = VolumeRollbackEvaluator.Evaluate(knownSequence, _vaultSaveSequence);

                if (verdict == VolumeIntegrityVerdict.Rollback)
                {
                    Serilog.Log.Warning(
                        "[VolumeIntegrity] Rollback detected: on-disk sequence {Current} < last-known {Known}",
                        _vaultSaveSequence, knownSequence);

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                        await _dialogService.ShowWarningAsync(
                            "Possible vault rollback detected",
                            "This vault appears to have been restored to an earlier state than this " +
                            "computer last saw. If you did not roll it back or restore a backup, the " +
                            "device may have been tampered with.\n\nThe vault will still open. Saving any " +
                            "change will accept the current contents as the new baseline.",
                            _ownerWindow)).ConfigureAwait(false);
                }

                // Re-baseline the anchor to the current state (advances on Ok, records on
                // first use, accepts the warned state on rollback).
                _volumeTrustAnchorStore.Write(_volumeTrustVaultId, _vaultSaveSequence);
            }
            catch (Exception ex)
            {
                // Rollback detection is a non-fatal enhancement — never block unlock on its failure.
                Debug.WriteLine($"[VolumeIntegrity] verification skipped: {ex.Message}");
            }
        }

        private async Task SaveVaultAsync()
        {
            try
            {

                if (_tamperDetectionService?.IsDecoyActive == true && _decoyVaultService?.IsDecoyActive == true)
                {

                    // Crypto RNG, not Random.Shared: this jitter masks the decoy
                    // save path's timing, so it must not be predictable.
                    await Task.Delay(System.Security.Cryptography.RandomNumberGenerator.GetInt32(50, 150));
                    return;
                }

                if (string.IsNullOrEmpty(_mountPath) || string.IsNullOrEmpty(_vaultFilePath))
                    return;

                _vaultSaveSequence++;
                var database = new VaultDatabase
                {
                    Version = "2.0",
                    EncryptionType = "ZeroKnowledge-VaultFileZk",
                    Created = System.DateTime.UtcNow,
                    VaultName = _vaultName,
                    Description = "Saved by PhantomVault",
                    SaveSequence = _vaultSaveSequence,
                    Groups = new System.Collections.Generic.List<VaultGroup>()
                };

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

                var json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = true });

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
                            GetVaultPasswordString(),
                            _vaultKeyfilePath).ConfigureAwait(false);
                    }
                    else
                    {
                        await _zkVaultService.EncryptStreamAsync(ms, _vaultFilePath!);
                    }

                    await PersistCredentialIndexAsync(false);
                    await FlushTransportArtifactsAsync();

                    if (_volumeTrustVaultId != null)
                    {
                        _volumeTrustAnchorStore.Write(_volumeTrustVaultId, _vaultSaveSequence);
                    }

                    StatusMessage = "Vault saved";
                }
                finally
                {

                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Vault save failed");
                await _dialogService.ShowErrorAsync("Save Failed", ErrorMessageService.GetUserSafeMessage(ex), _ownerWindow);
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
                if (!SettingsService.Load().EnableSuiteCredentialIndex)
                {
                    _obscuraCredentialIndexService.Delete(_mountPath);
                    return;
                }

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
                Log.Warning(ex, "[VaultViewModel] Failed to persist the Obscura credential index.");
                RecentIssuesLog.Instance.Record(IssueSeverity.Warning, "Credential index not updated", "Browser auto-fill data may be stale. Lock and reopen the vault to retry.");
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
                await obscuraVolumeService.CreateVolumeFromDirectoryAsync(
                    _masterVolumePath, _transportLayoutRoot, RequireVaultKeyfile()).ConfigureAwait(false);
            }
        }

        private async Task CopyPasswordAsync(CredentialViewModel credential)
        {
            try
            {
                _idleLockService.Reset();
                try { _suiteSession?.TouchActivity(); } catch { /* best-effort */ }

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

                    var settings = SettingsService.Load();
                    var autoCopyTotp = settings.AutoCopyTotpWithPassword;
                    var totpCopied = false;

                    if (autoCopyTotp && credential.EntryType == EntryType.Password && credential.HasTotpSecret && !string.IsNullOrWhiteSpace(credential.CurrentTotpCode))
                    {

                        secret = $"{secret}\nTOTP: {credential.CurrentTotpCode}";
                        totpCopied = true;
                    }

                    await clipboard.SetTextAsync(secret);
                    StatusMessage = totpCopied
                        ? $"Password and TOTP copied for: {credential.Title}"
                        : $"Secret copied for: {credential.Title}";

                    _clipboardGuard?.RegisterCopy(credential.Title);

                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

                    var clearDelay = settings.GetClipboardClearDelay();
                    if (clearDelay.HasValue)
                    {
                        var copiedSecret = secret;
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
                            catch (OperationCanceledException) {  }
                            catch {  }
                        });
                    }

                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Clipboard] Failed to copy a password.");
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    "The password could not be copied. Confirm clipboard access is allowed, then try again.",
                    _ownerWindow);
            }
        }

        private async Task CopyUsernameAsync(CredentialViewModel credential)
        {
            try
            {
                _idleLockService.Reset();
                try { _suiteSession?.TouchActivity(); } catch { /* best-effort */ }
                if (_clipboardGuard != null && !_clipboardGuard.CanCopy())
                {
                    await _dialogService.ShowWarningAsync(
                        "Clipboard Blocked",
                        "Too many clipboard operations detected. Please wait before copying again.",
                        _ownerWindow);
                    StatusMessage = "Clipboard temporarily blocked";
                    return;
                }

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

                    _clipboardGuard?.RegisterCopy(credential.Title);

                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

                    var settings = SettingsService.Load();
                    var clearDelay = settings.GetClipboardClearDelay();
                    if (clearDelay.HasValue)
                    {
                        var copiedUsername = credential.Username;
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
                            catch (OperationCanceledException) {  }
                            catch {  }
                        });
                    }

                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Clipboard] Failed to copy a username.");
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    "The username could not be copied. Confirm clipboard access is allowed, then try again.",
                    _ownerWindow);
            }
        }

        private async Task CopyTotpCodeAsync(CredentialViewModel credential)
        {
            try
            {

                if (_clipboardGuard != null && !_clipboardGuard.CanCopy())
                {
                    await _dialogService.ShowWarningAsync(
                        "Clipboard Blocked",
                        "Too many clipboard operations detected. Please wait before copying again.",
                        _ownerWindow);
                    StatusMessage = "Clipboard temporarily blocked";
                    return;
                }

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

                    _clipboardGuard?.RegisterCopy(credential.Title);

                    _clipboardClearCts?.Cancel();
                    _clipboardClearCts = new CancellationTokenSource();
                    var clearToken = _clipboardClearCts.Token;

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
                        catch (OperationCanceledException) {  }
                        catch {  }
                    });

                    await Task.Delay(2000);
                    StatusMessage = "Ready";
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Clipboard] Failed to copy a TOTP code.");
                await _dialogService.ShowErrorAsync(
                    "Copy Failed",
                    "The authentication code could not be copied. Confirm clipboard access is allowed, then try again.",
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

                    var credential = _credentials.FirstOrDefault(c => c.Title == SelectedCredential.Title);
                    if (credential != null)
                    {

                        var coreCredential = credential.GetCredential();

                        coreCredential.TotpSecret = result.Secret;
                        coreCredential.TotpDigits = result.Digits;
                        coreCredential.TotpTimeStep = result.Period;
                        coreCredential.TotpAlgorithm = result.Algorithm;
                        coreCredential.TotpIssuer = result.Issuer;
                        coreCredential.TotpAccountName = result.AccountName;
                        coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        await SaveVaultAsync();

                        var index = _credentials.IndexOf(credential);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCredential);
                        _credentials.Insert(index, newCredentialVM);

                        SelectedCredential = newCredentialVM;

                        StatusMessage = $"TOTP added to {SelectedCredential.Title}";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TOTP] Failed to add TOTP to the selected credential.");
                await _dialogService.ShowErrorAsync(
                    "Add TOTP Failed",
                    "TOTP could not be added. Verify the secret or QR code and try again.",
                    _ownerWindow);
            }
        }

        private async Task EditTotpAsync(CredentialViewModel credential)
        {
            try
            {

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

                    if (result.Deleted)
                    {
                        await RemoveTotpAsync();
                        return;
                    }

                    var credentialVM = _credentials.FirstOrDefault(c => c.Title == credential.Title);
                    if (credentialVM != null)
                    {

                        var coreCredential = credentialVM.GetCredential();

                        coreCredential.TotpSecret = result.Secret;
                        coreCredential.TotpDigits = result.Digits;
                        coreCredential.TotpTimeStep = result.Period;
                        coreCredential.TotpAlgorithm = result.Algorithm;
                        coreCredential.TotpIssuer = result.Issuer;
                        coreCredential.TotpAccountName = result.AccountName;
                        coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                        await SaveVaultAsync();

                        var index = _credentials.IndexOf(credentialVM);
                        _credentials.RemoveAt(index);
                        var newCredentialVM = new CredentialViewModel(coreCredential);
                        _credentials.Insert(index, newCredentialVM);

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
                Log.Error(ex, "[TOTP] Failed to edit TOTP for the selected credential.");
                await _dialogService.ShowErrorAsync(
                    "Edit TOTP Failed",
                    "The TOTP settings could not be updated. Verify the values and try again.",
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

                var validBase32 = System.Text.RegularExpressions.Regex.IsMatch(secret, @"^[A-Z2-7]+=*$");
                if (!validBase32)
                {
                    await _dialogService.ShowWarningAsync(
                        "Invalid Secret Key",
                        "The secret key must be a valid Base32 string (letters A-Z and digits 2-7).",
                        _ownerWindow);
                    return;
                }

                var credential = _credentials.FirstOrDefault(c => c == SelectedCredential);
                if (credential != null)
                {
                    var coreCredential = credential.GetCredential();

                    coreCredential.TotpSecret = secret;
                    coreCredential.TotpDigits = 6;
                    coreCredential.TotpTimeStep = 30;
                    coreCredential.TotpAlgorithm = "SHA1";
                    coreCredential.TotpIssuer = coreCredential.Title;
                    coreCredential.TotpAccountName = coreCredential.Username;
                    coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                    await SaveVaultAsync();

                    await ExportTotpToSyncAsync();

                    var index = _credentials.IndexOf(credential);
                    _credentials.RemoveAt(index);
                    var newCredentialVM = new CredentialViewModel(coreCredential);
                    _credentials.Insert(index, newCredentialVM);

                    SelectedCredential = newCredentialVM;

                    TotpSecretInput = string.Empty;

                    StatusMessage = $"TOTP added to {newCredentialVM.Title}";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TOTP] Failed to save a TOTP secret.");
                await _dialogService.ShowErrorAsync(
                    "Save TOTP Failed",
                    "The TOTP secret could not be saved. Verify it and try again.",
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

                    coreCredential.TotpSecret = string.Empty;
                    coreCredential.TotpIssuer = string.Empty;
                    coreCredential.TotpAccountName = string.Empty;
                    coreCredential.LastUpdatedUtc = DateTimeOffset.UtcNow;

                    await SaveVaultAsync();

                    await ExportTotpToSyncAsync();

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
                Log.Error(ex, "[TOTP] Failed to remove TOTP from the selected credential.");
                await _dialogService.ShowErrorAsync(
                    "Remove TOTP Failed",
                    "TOTP could not be removed. Try again.",
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
                Log.Warning(ex, "[Navigation] Failed to open a credential URL.");
                await _dialogService.ShowErrorAsync(
                    "Open Failed",
                    "The URL could not be opened. Verify it is a supported web address and try again.",
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

            if (!IsDashboardEnabled)
            {
                ShowPasswords();
                return;
            }

            IsShowingDashboard = false;

            System.Threading.Tasks.Task.Delay(1).Wait();

            DashboardViewModel.ClearDashboard();

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

            IsSidebarCollapsed = true;

            IsShowingDashboard = true;

            _ = DashboardViewModel.LoadDashboardDataAsync();
            DashboardViewModel.OwnerWindow = _ownerWindow;
            DashboardViewModel.ClipboardGuard = _clipboardGuard;
            DashboardViewModel.LoadQuickAccessCredentials(_credentials, _categories);
            DashboardViewModel.SetRecoveryAvailability(IsEmbeddedRecoveryAvailable, DashboardRecoveryStatus);

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
            IsSidebarCollapsed = false;

            FilterByEntryType("Password");
            IsShowingPasswords = true;
            IsShowingDashboard = false;
            CurrentViewTitle = "Passwords";
            ApplyFilters();

            this.RaisePropertyChanged(nameof(IsShowingPasswords));
            this.RaisePropertyChanged(nameof(IsShowingDashboard));
        }

        private void ShowAll()
        {

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

            IsShowingCategoryLanding = false;

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
                case "AddCredential":
                case "Generator":
                    IsShowingDashboard = false;
                    _ = AddCredentialAsync();
                    break;
                case "Import":
                    IsShowingDashboard = false;
                    _ = OpenImportWindowAsync();
                    break;
                case "Export":
                    IsShowingDashboard = false;
                    _ = ExportVaultAsync("json");
                    break;
                case "Settings":
                    IsShowingDashboard = false;
                    OpenSettingsPanel();
                    break;
                case "Backup":
                    IsShowingDashboard = false;
                    OpenSettingsPanel();
                    ShowBackupSettings();
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
            passphrase = GetVaultPasswordString();
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

        internal void SetManifestContext(string manifestPath, string? password, string? keyfilePath)
            => SetManifestContext(manifestPath, password, keyfilePath, preloadedManifest: null);

        internal void SetManifestContext(string manifestPath, string? password, string? keyfilePath, VaultManifest? preloadedManifest)
        {
            _vaultPassword?.Dispose();
            _vaultPassword = string.IsNullOrEmpty(password) ? null : SecurePassword.FromString(password);
            _vaultKeyfilePath = keyfilePath;

            _manifestPath = manifestPath;
            _reauthKeyfilePath = keyfilePath;

            // Null out the incoming string immediately — password is now owned by _vaultPassword.
            password = null!;

            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
            {
                _mountPath = manifestDir;
                _usbRootPath = manifestDir;
            }

            _transportLayoutRoot = TryResolveTransportLayoutRoot(manifestPath);

            try
            {
                // Reuse the manifest the unlock flow already validated (and the Argon2id KDF that
                // produced it) instead of running ReadManifest a second time.
                var manifest = preloadedManifest
                    ?? _manifestService.ReadManifestSecure(manifestPath, _vaultPassword ?? SecurePassword.Empty(), keyfilePath);
                _cachedRuntimeManifest = manifest;
                ApplyManifestTransportState(manifest, manifestPath);
                ApplyEntitlementsFromManifest(manifest);
            }
            catch
            {
                _masterVolumePath = null;
                _protectionTier = VaultProtectionTier.StandardSecure;
                _effectiveStorageTransport = VaultStorageTransport.FileSystem;
                _requestedStorageTransport = null;
            }

            password = null!;

            this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
            this.RaisePropertyChanged(nameof(KeyfileUnlockAvailable));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
            this.RaisePropertyChanged(nameof(PinUnlockAvailable));

            LoadCategoryColorsFromManifest();
        }

        private void ApplyEntitlementsFromManifest(VaultManifest? manifest)
        {
            if (_entitlementService is null) return;
            string? binding = manifest?.UsbBindingId ?? manifest?.UsbBindingGuid;
            _entitlementService.ApplyToken(manifest?.PremiumLicenseToken, binding);
            RaiseEntitlementProperties();
        }

        private void RaiseEntitlementProperties()
        {
            this.RaisePropertyChanged(nameof(IsPremium));
            this.RaisePropertyChanged(nameof(IsFreeTier));
            this.RaisePropertyChanged(nameof(CanUseCustomThemes));
            this.RaisePropertyChanged(nameof(CanUseAdvancedSettings));
            this.RaisePropertyChanged(nameof(CanUseFullSecurityDashboard));
            this.RaisePropertyChanged(nameof(CanUseAdvancedCategoryManager));
            this.RaisePropertyChanged(nameof(CanUseIconManager));
            this.RaisePropertyChanged(nameof(CanUseMultiVault));
            this.RaisePropertyChanged(nameof(CanUseEncryptedDriveMount));
        }

        public bool IsPremium => _entitlementService?.IsPremium ?? false;
        public bool IsFreeTier => !IsPremium;
        public bool CanUseCustomThemes =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.CustomThemes) ?? false;
        public bool CanUseAdvancedSettings =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.AdvancedSettings) ?? false;
        public bool CanUseFullSecurityDashboard =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.FullSecurityDashboard) ?? false;
        public bool CanUseAdvancedCategoryManager =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.AdvancedCategoryManager) ?? false;
        public bool CanUseIconManager =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.IconManager) ?? false;
        public bool CanUseMultiVault =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.MultiVault) ?? false;
        public bool CanUseEncryptedDriveMount =>
            _entitlementService?.IsUnlocked(PhantomVault.Core.Models.Licensing.PremiumFeature.EncryptedDriveMount) ?? false;

        // ---- Encrypted virtual-drive mount (WinFsp) ----

        private bool _isDriveMounted;
        public bool IsDriveMounted
        {
            get => _isDriveMounted;
            private set => this.RaiseAndSetIfChanged(ref _isDriveMounted, value);
        }

        private string? _mountedDriveLetter;
        public string? MountedDriveLetter
        {
            get => _mountedDriveLetter;
            private set => this.RaiseAndSetIfChanged(ref _mountedDriveLetter, value);
        }

        private bool _isMountReadOnly;
        public bool IsMountReadOnly
        {
            get => _isMountReadOnly;
            private set => this.RaiseAndSetIfChanged(ref _isMountReadOnly, value);
        }

        /// <summary>True when the WinFsp runtime is installed and the mount can be offered.</summary>
        public bool IsMountSupported =>
            PhantomVault.UI.Services.Mount.PhantomMountService.IsWinFspAvailable;

        private string? ResolveSystemVolumePath()
        {
            if (!string.IsNullOrWhiteSpace(_masterVolumePath))
            {
                if (File.Exists(_masterVolumePath))
                    return _masterVolumePath;

                // Older manifests may store the transport path relative to the
                // canonical .phantom layout rather than as an absolute path.
                if (!Path.IsPathRooted(_masterVolumePath) && !string.IsNullOrWhiteSpace(_mountPath))
                {
                    var resolved = Path.GetFullPath(Path.Combine(_mountPath, _masterVolumePath));
                    if (File.Exists(resolved))
                        return resolved;
                }
            }

            if (string.IsNullOrEmpty(_usbRootPath) && string.IsNullOrEmpty(_mountPath))
                return null;
            string driveRoot = !string.IsNullOrEmpty(_usbRootPath) ? _usbRootPath : _mountPath;
            // _mountPath points at the manifest directory (.phantom); walk up to the drive root.
            if (string.Equals(Path.GetFileName(driveRoot.TrimEnd(Path.DirectorySeparatorChar)),
                    PhantomVault.Core.Services.PhantomDeviceLayout.PhantomFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                driveRoot = Path.GetDirectoryName(driveRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? driveRoot;
            }
            var usbVolume = PhantomVault.Core.Services.PhantomDeviceLayout.GetSystemVolumePath(driveRoot);
            if (File.Exists(usbVolume))
                return usbVolume;

            // Local packed vaults are provisioned inside .phantom as obscura.vol.
            var manifestDirectory = !string.IsNullOrWhiteSpace(_mountPath) ? _mountPath : driveRoot;
            var localVolume = Path.Combine(manifestDirectory, "obscura.vol");
            return File.Exists(localVolume) ? localVolume : null;
        }

        /// <summary>
        /// Mounts the encrypted container as a Windows drive. <paramref name="writable"/>
        /// edits are repacked into the container atomically on unmount, and the save-sequence
        /// rollback anchor is advanced so the change is accepted as the new baseline.
        /// </summary>
        private async Task MountEncryptedDriveAsync(bool writable)
        {
            if (!CanUseEncryptedDriveMount)
            {
                EnsurePremiumOrPrompt(
                    PhantomVault.Core.Models.Licensing.PremiumFeature.EncryptedDriveMount,
                    "Mounting the vault as a secure virtual drive is a Premium feature.");
                return;
            }
            if (!IsMountSupported)
            {
                await _dialogService.ShowWarningAsync(
                    "WinFsp required",
                    "Mounting the encrypted drive needs the WinFsp runtime, which isn't installed " +
                    "on this computer. Install WinFsp (https://winfsp.dev) and try again.",
                    _ownerWindow);
                return;
            }

            var volumePath = ResolveSystemVolumePath();
            if (volumePath == null)
            {
                await _dialogService.ShowWarningAsync(
                    "No encrypted volume",
                    "This device does not have an encrypted container to mount yet.",
                    _ownerWindow);
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = writable ? "Mounting encrypted drive (writable)..." : "Mounting encrypted drive...";
                _mountService ??= new PhantomVault.UI.Services.Mount.PhantomMountService();

                string? mountPoint = writable
                    ? await _mountService.MountWritableAsync(volumePath, RequireVaultKeyfile(),
                        driveLetter: null,
                        onCommitted: OnMountCommitted)
                    : await _mountService.MountReadOnlyAsync(volumePath, RequireVaultKeyfile());

                IsDriveMounted = _mountService.IsMounted;
                MountedDriveLetter = mountPoint;
                IsMountReadOnly = _mountService.IsReadOnly;
                StatusMessage = mountPoint == null
                    ? "Mount failed."
                    : $"Encrypted drive mounted at {mountPoint}{(writable ? " (writable)" : " (read-only)")}.";

                if (mountPoint != null)
                    LogSecurityActivity("mount",
                        $"Encrypted drive mounted at {mountPoint} ({(writable ? "writable" : "read-only")}).");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Mount] Failed to mount the encrypted drive.");
                await _dialogService.ShowErrorAsync("Mount Failed",
                    "The encrypted drive could not be mounted. Verify the vault is available and try again.", _ownerWindow);
                StatusMessage = "Mount failed.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UnmountEncryptedDrive()
        {
            try
            {
                _mountService?.Unmount();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Mount] Unmount raised an error (ignored).");
            }
            finally
            {
                IsDriveMounted = _mountService?.IsMounted ?? false;
                MountedDriveLetter = _mountService?.MountPoint;
                IsMountReadOnly = _mountService?.IsReadOnly ?? false;
                if (!IsDriveMounted)
                {
                    StatusMessage = "Encrypted drive unmounted.";
                    LogSecurityActivity("unmount", "Encrypted drive unmounted.");
                }
            }
        }

        /// <summary>
        /// Fired after the writable mount repacks the container on unmount. Advances the
        /// host-local rollback anchor so the externally written changes are trusted on the
        /// next unlock instead of being flagged as a rollback.
        /// </summary>
        private void OnMountCommitted()
        {
            try
            {
                if (_volumeTrustVaultId != null)
                {
                    _vaultSaveSequence++;
                    _volumeTrustAnchorStore.Write(_volumeTrustVaultId, _vaultSaveSequence);
                    Serilog.Log.Information(
                        "[Mount] Container repacked; rollback anchor advanced to {Seq}.", _vaultSaveSequence);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Mount] Failed to advance rollback anchor after commit.");
            }
        }

        private async Task ManageCategoriesAsync()
        {
            try
            {
                EnsureVaultVisible();

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

                if (manifestPath != null)
                {

                    var passphrase = GetVaultPasswordString();
                    var keyfile = string.IsNullOrEmpty(_vaultKeyfilePath) ? null : _vaultKeyfilePath;

                    try
                    {
                        var manifest = _manifestService.ReadManifestSecure(manifestPath, _vaultPassword ?? SecurePassword.Empty(), keyfile);

                        CloseAllOverlays();
                        var categoryVm = new CategoryManagerViewModel(_manifestService, manifest, manifestPath, this, passphrase: passphrase, keyfilePath: keyfile ?? manifest.KeyfilePath);
                        AttachCategoryManagerViewModel(categoryVm);
                        IsCategoryManagerPanelVisible = true;
                        StatusMessage = "Managing categories";
                    }
                    catch (Exception innerEx)
                    {
                        Log.Error(innerEx, "[CategoryManager] Failed to read the manifest with cached credentials.");
                        await _dialogService.ShowErrorAsync("Category Manager", "The vault categories could not be read. Lock and reopen the vault, then try again.", _ownerWindow);
                        StatusMessage = "Category manager error";
                    }
                }
                else
                {

                    CloseAllOverlays();
                    var categoryVm = new CategoryManagerViewModel(this);
                    AttachCategoryManagerViewModel(categoryVm);
                    IsCategoryManagerPanelVisible = true;
                    StatusMessage = "Managing categories";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CategoryManager] Failed to open the category manager.");
                await _dialogService.ShowErrorAsync("Manage Categories", "The category manager could not be opened. Try again or review Recent Issues.", _ownerWindow);
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
            ApplyFilters();
        }

        private void ToggleSidebar()
        {

            if (IsShowingDashboard && IsSidebarCollapsed)
            {
                StatusMessage = "Sidebar cannot be expanded in dashboard view";
                return;
            }

            if (_isSidebarAutoCollapsed)
            {
                _isSidebarAutoCollapsed = false;
            }

            IsSidebarCollapsed = !IsSidebarCollapsed;
            StatusMessage = IsSidebarCollapsed ? "Sidebar collapsed" : "Sidebar expanded";
        }

        public void UpdateSidebarVisibility(double windowWidth)
        {

            if (windowWidth >= 1200)
            {

                _isSidebarAutoCollapsed = false;
            }
            else
            {

                if (!_isSidebarAutoCollapsed && !IsSidebarCollapsed)
                {

                    _isSidebarAutoCollapsed = true;
                    IsSidebarCollapsed = true;
                }

            }
        }

        private void ToggleTheme()
        {

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
                    Log.Error(ex, "[Lock] Sensitive-session cleanup did not complete.");
                    StatusMessage = "Vault cleanup did not complete. Close the vault window to ensure access is revoked.";
                })
            };

            _vaultLockDurationService.SetActiveSession(session);
        }

        private void ClearSensitiveState()
        {
            // SecurePassword.Dispose() does a multi-pass zeroization + GCHandle.Free()
            _vaultPassword?.Dispose();
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

                EnterLockedState(
                    title: "Vault Locked",
                    message: e.Reason == LockReason.AutoLock ? "Vault auto-locked due to inactivity." : "Vault locked.",
                    softLock: false);

                ClearVaultUiState();

                await Task.CompletedTask;
            });
        }

        private void OnIdleLockElapsed()
        {

            try
            {
                bool pinConfigured = PinLockService.SyncPinFlags(_manifestPath)
                    && SettingsService.Load().EnablePinLock;
                if (!pinConfigured)
                {
                    return;
                }
            }
            catch
            {

                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                await LockInAppAsync(LockReason.AutoLock);
            });
        }

        private void ClearVaultUiState()
        {
            // Security: tear down any mounted encrypted drive before clearing in-memory
            // state so the plaintext projection cannot outlive the unlocked session.
            try
            {
                if (_mountService is { IsMounted: true })
                    UnmountEncryptedDrive();
                _mountService?.Dispose();
                _mountService = null;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Mount] Failed to unmount during lock (ignored).");
            }

            _items.Clear();
            _credentials.Clear();
            _filteredCredentials.Clear();
            _groupedListItems.Clear();
            _passkeys.Clear();
            _flaggedCredentials.Clear();
            _filteredTrashItems.Clear();
            SelectedCredential = null;
            SelectedTrashItem = null;

            if (_totpSyncService != null)
            {
                _totpSyncService.EntriesChanged -= OnTotpEntriesChanged;
                _totpSyncService.Dispose();
                _totpSyncService = null;
            }

            try
            {
                var ctx = (Avalonia.Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.Sync.TotpSyncVaultContext))
                          as PhantomVault.UI.Services.Sync.TotpSyncVaultContext;
                ctx?.SetLocked();
            }
            catch { /* best-effort */ }

            // Suite presence: this app is now locked. Presence-only — does NOT lock other apps.
            try { _suiteSession?.ReportLocked(); } catch { /* best-effort */ }
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

            if (!_isDeveloperBypassMode
                && string.IsNullOrWhiteSpace(_manifestPath)
                && TryGetManifestContext(out var inferredManifestPath, out _, out _))
            {
                _manifestPath = inferredManifestPath;
                this.RaisePropertyChanged(nameof(PasswordUnlockAvailable));
                this.RaisePropertyChanged(nameof(KeyfileUnlockAvailable));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));
            }

            _showPassphraseFallback = false;
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
            this.RaisePropertyChanged(nameof(ShowPassphraseFallbackLink));

            IsSoftLocked = softLock;
            IsLockscreenVisible = true;

            // Locking must revoke the vault's capabilities, not just cover them with a
            // screen. Previously the autofill surface stayed live for the whole session
            // because VaultAutofillContext.SetLocked() was only reached when the vault
            // WINDOW closed — so an idle-locked machine would still hand full plaintext
            // passwords to the browser extension, and getVaultState even reported
            // locked=false. The lockscreen was security theatre for that surface.
            RevokeUnlockedCapabilities();
        }

        /// <summary>
        /// Drops every capability that should only exist while the vault is genuinely
        /// unlocked: the autofill pipe's credential provider, the reported unlock state,
        /// and the suite presence signal.
        ///
        /// Deliberately separate from <see cref="ClearSensitiveState"/>: this runs on
        /// every lock, whereas ClearSensitiveState also tears down subscriptions and is
        /// only appropriate when the session is ending.
        /// </summary>
        private void RevokeUnlockedCapabilities()
        {
            try
            {
                var services = (Avalonia.Application.Current as App)?.Services;
                if (services != null)
                {
                    (services.GetService(typeof(PhantomVault.UI.Services.AutoFill.VaultAutofillContext))
                        as PhantomVault.UI.Services.AutoFill.VaultAutofillContext)?.SetLocked();

                    (services.GetService(typeof(PhantomVault.UI.Services.AutoFill.INativeHostPipeServer))
                        as PhantomVault.UI.Services.AutoFill.INativeHostPipeServer)?.ClearCredentialProvider();

                    (services.GetService(typeof(PhantomVault.UI.Services.Sync.TotpSyncVaultContext))
                        as PhantomVault.UI.Services.Sync.TotpSyncVaultContext)?.SetLocked();
                }
            }
            catch (Exception ex)
            {
                // Never let a teardown failure leave the vault appearing locked while
                // still serving credentials — log loudly rather than swallowing.
                Debug.WriteLine($"[VaultViewModel] Failed to revoke unlocked capabilities: {ex.Message}");
                RecentIssuesLog.Instance.Record(IssueSeverity.Warning,
                    "Lock incomplete",
                    "Auto-fill access may not have been revoked on lock. Close the vault window to be certain.");
            }

            try { _suiteSession?.ReportLocked(); } catch { /* presence only */ }
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

            // Mirror of RevokeUnlockedCapabilities. Only reached after a successful
            // re-authentication, so restoring autofill here does not widen access.
            RestoreUnlockedCapabilities();

            // Re-entered the unlocked state (e.g. PIN/password dismiss of a soft lock):
            // re-assert suite presence so the suite session reflects this app as unlocked.
            try { _suiteSession?.ReportUnlocked(); } catch { /* best-effort */ }
        }

        /// <summary>
        /// Re-grants what <see cref="RevokeUnlockedCapabilities"/> withdrew.
        ///
        /// The credential provider is re-registered on the pipe so browser autofill
        /// works again, but only once the user has proved they can unlock.
        /// </summary>
        private void RestoreUnlockedCapabilities()
        {
            try
            {
                var services = (Avalonia.Application.Current as App)?.Services;
                if (services == null) return;

                // Without a manifest there is nothing to authorise against, so leave
                // autofill revoked rather than guessing.
                var manifest = _cachedRuntimeManifest;
                if (manifest == null) return;

                (services.GetService(typeof(PhantomVault.UI.Services.AutoFill.VaultAutofillContext))
                    as PhantomVault.UI.Services.AutoFill.VaultAutofillContext)?.SetUnlocked(manifest);

                (services.GetService(typeof(PhantomVault.UI.Services.AutoFill.INativeHostPipeServer))
                    as PhantomVault.UI.Services.AutoFill.INativeHostPipeServer)
                    ?.SetCredentialProvider(new VaultViewModelCredentialProvider(this), manifest);
            }
            catch (Exception ex)
            {
                // Failing closed here only costs autofill until the next full unlock.
                Debug.WriteLine($"[VaultViewModel] Failed to restore unlocked capabilities: {ex.Message}");
            }
        }

        /// <summary>
        /// Honour a suite-wide lock request (raised by another app via "Lock Suite",
        /// idle timeout, or session termination). No-op if already locked. Locks THIS
        /// app only in response to a genuine suite signal — never used for routine
        /// per-app locking (which stays isolated).
        /// </summary>
        public void RequestSuiteLock()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsLockscreenVisible) return;
                _ = LockInAppAsync(LockReason.ManualLock);
            });
        }

        private void DismissLockscreen()
        {
            // Push the user's auto-lock setting in before restarting the timer.
            // SetLockDuration previously had no callers at all, so this service ran on
            // its FiveMinutes field default and ignored the configured timeout.
            ApplyConfiguredLockDuration();

            _vaultLockDurationService.UnlockVault();
            ExitLockedState();
        }

        /// <summary>
        /// Maps the persisted idle timeout onto the nearest <see cref="LockDuration"/> and
        /// applies it. Must be called before every <c>UnlockVault()</c>/<c>ResetTimer()</c>,
        /// because those start the timer from <c>_currentDuration</c> — which defaulted to
        /// FiveMinutes and ignored the user's setting entirely.
        ///
        /// This deliberately does NOT touch <c>AutoLockEnabled</c>. Each call site owns that
        /// decision (the main unlock path gates it on whether a PIN is configured), and
        /// overriding it here would silently change who gets auto-locked. <c>SetLockDuration</c>
        /// updates <c>_currentDuration</c> before its own enabled-check, so calling this
        /// first and setting <c>AutoLockEnabled</c> afterwards is the correct order.
        /// </summary>
        private void ApplyConfiguredLockDuration()
        {
            try
            {
                var minutes = SettingsService.Load().IdleTimeoutMinutes;

                var duration = minutes switch
                {
                    <= 0 => LockDuration.Never,
                    1 => LockDuration.OneMinute,
                    2 => LockDuration.TwoMinutes,
                    <= 5 => LockDuration.FiveMinutes,
                    <= 10 => LockDuration.TenMinutes,
                    <= 15 => LockDuration.FifteenMinutes,
                    <= 30 => LockDuration.ThirtyMinutes,
                    _ => LockDuration.OneHour
                };

                _vaultLockDurationService.SetLockDuration(duration);
            }
            catch (Exception ex)
            {
                // Fully qualified: this file otherwise logs via Debug.WriteLine, but a
                // silently-failing auto-lock window is security-relevant and belongs in
                // the real log.
                Serilog.Log.Warning(ex, "Failed to apply configured lock duration; leaving previous value in force");
            }
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

                    var settings = SettingsService.Load();
                    settings.EnablePinLock = true;
                    SettingsService.Save(settings);

                    // A PIN was just configured, so auto-lock becomes available — the main
                    // unlock path gates AutoLockEnabled on exactly this condition.
                    ApplyConfiguredLockDuration();
                    _vaultLockDurationService.AutoLockEnabled = true;
                    _vaultLockDurationService.ResetTimer();

                    this.RaisePropertyChanged(nameof(PinUnlockAvailable));
                    this.RaisePropertyChanged(nameof(ShowPassphraseFallbackSection));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PIN] PIN setup failed.");
                RecentIssuesLog.Instance.Record(IssueSeverity.Error, "PIN setup failed", "Your PIN may not have been saved. Try setting it again.");
            }
        }

        private async Task LockInAppAsync(LockReason reason)
        {

            LogSecurityActivity("lock", reason == LockReason.AutoLock
                ? "Vault auto-locked due to inactivity."
                : "Vault locked by user.");

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

            if (string.IsNullOrEmpty(_mountPath))
            {
                // Nothing is mounted, so there is no volume to unmount — but the lock
                // still has to match the factors that actually exist. A soft lock IS the
                // PIN lock screen, and this branch entered one unconditionally, so a
                // vault with no PIN configured auto-locked into a PIN screen the user had
                // never set up. Without a PIN this is a full lock and re-authentication
                // goes through the normal unlock flow.
                EnterLockedState("Vault Locked", "Vault locked.", softLock: pinConfigured);
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

                // Hard lock: the only way back in is a full re-authentication, so the
                // master password has no further use and is zeroized now rather than
                // lingering until the window closes. ClearVaultUiState drops the
                // decrypted credentials; this drops the key material that could
                // regenerate them.
                //
                // NOTE: this is deliberately NOT done for a soft (PIN) lock. PIN unlock
                // verifies a stored hash and resumes the session without re-deriving
                // anything, so the password must survive for that path to work. Making
                // a soft lock zeroize as well requires the PIN to wrap the master key
                // instead of merely being compared — see RevokeUnlockedCapabilities.
                _vaultPassword?.Dispose();
                _vaultPassword = null;

                StatusMessage = "Vault locked.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Vault lock failed");
                var safeMessage = ErrorMessageService.GetUserSafeMessage(ex);
                await _dialogService.ShowErrorAsync(
                    "Lock Failed",
                    safeMessage,
                    _ownerWindow);
                StatusMessage = "Vault lock did not complete.";
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
                using var reauthPassphrase = SecurePassword.FromString(password);
                manifest = _manifestService.ReadManifestSecure(_manifestPath, reauthPassphrase, _reauthKeyfilePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lock-screen passphrase verification failed");
                LockscreenError = "The passphrase was not accepted or the vault could not be verified.";
                return false;
            }

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
                    Log.Warning(ex, "Lock-screen device-authenticator verification failed");
                    LockscreenError = "Device-authenticator verification could not be completed.";
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
            if (string.IsNullOrWhiteSpace(password) && !KeyfileUnlockAvailable)
            {
                LockscreenError = "Enter your passphrase.";
                return;
            }

            // A keyfile-only vault has no passphrase to type; the empty string is the
            // correct input and the keyfile carries the unlock.
            password ??= string.Empty;

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

                if (string.IsNullOrWhiteSpace(_containerAbsPath))
                {
                    LockscreenError = "Missing container path; cannot re-mount.";
                    return;
                }

                VaultManifest manifest;
                using (var remountPassphrase = SecurePassword.FromString(password))
                    manifest = _manifestService.ReadManifestSecure(_manifestPath!, remountPassphrase, _reauthKeyfilePath);
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
                Log.Warning(ex, "Lock-screen unlock attempt failed");
                RegisterFailedLockscreenAttempt("Unlock failed. Verify your credentials and connected security devices.");
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

        public async Task LoadAsync(string mountPath, string password, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            IsLoadingCredentials = true;
            try
            {
                _mountPath = mountPath;
                _vaultPassword?.Dispose();
                _vaultPassword = string.IsNullOrEmpty(password) ? null : SecurePassword.FromString(password);
                _vaultKeyfilePath = keyfilePath;

                if (!string.IsNullOrWhiteSpace(_manifestPath) && File.Exists(_manifestPath))
                {
                    try
                    {
                        // Reuse the cached manifest (already decrypted in SetManifestContext / unlock flow)
                        // instead of running another Argon2id-backed ReadManifest.
                        var runtimeManifest = _cachedRuntimeManifest
                            ?? _manifestService.ReadManifestSecure(_manifestPath, _vaultPassword ?? SecurePassword.Empty(), keyfilePath);
                        ApplyManifestTransportState(runtimeManifest, _manifestPath);
                    }
                    catch
                    {

                    }
                }

                password = null!;

                _vaultLockDurationService.SetActiveSession(null);

                Items.Clear();
                _credentials.Clear();

                if (!Directory.Exists(mountPath))
                {
                    StatusMessage = "Mount path does not exist";
                    return;
                }

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

                if (_tamperDetectionService?.IsDecoyActive == true && _decoyVaultService?.IsDecoyActive == true)
                {

                    database = _decoyVaultService.DecoyDatabase;

                    if (database == null)
                    {
                        StatusMessage = "Decoy vault not initialized";
                        return;
                    }
                }
                else
                {

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
                            GetVaultPasswordString(),
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

                        database = JsonSerializer.Deserialize<VaultDatabase>(json);
                    }

                    if (database == null)
                    {
                        StatusMessage = "Failed to parse vault database";
                        return;
                    }
                }

                bool isDecoyActive = _tamperDetectionService?.IsDecoyActive == true
                    && _decoyVaultService?.IsDecoyActive == true;
                if (!isDecoyActive)
                {
                    await VerifyVolumeRollbackAsync(database).ConfigureAwait(false);
                }

                if (database.Groups != null)
                {
                    var loadedAccounts = new List<Credential>();
                    foreach (var group in database.Groups)
                    {
                        if (group.Entries != null)
                        {
                            foreach (var credential in group.Entries)
                            {

                                credential.Group = group.Name;
                                // Apply the same service identity pass used for newly saved
                                // entries to the full existing vault. Entries stay separate
                                // tiles; only their canonical service title/icon is shared.
                                AccountConsolidationService.Consolidate(credential, loadedAccounts);
                                loadedAccounts.Add(credential);
                                _credentials.Add(new CredentialViewModel(credential));
                            }
                        }
                    }
                }

                UpdateCategoryCounts();

                LoadCategoryColorsFromManifest();

                SelectCategory("Logins");

                foreach (var entry in Directory.GetFileSystemEntries(mountPath))
                {
                    Items.Add(Path.GetFileName(entry));
                }

                StatusMessage = $"Loaded {_credentials.Count} credentials";

                RegisterAutoLockSession();

                // Push the configured window in before AutoLockEnabled and UnlockVault().
                // This is the primary unlock path; without it the lock service ran on its
                // FiveMinutes field default here, so the user's auto-lock setting was
                // honoured only when re-unlocking from the lockscreen.
                ApplyConfiguredLockDuration();

                try
                {
                    // Drops stale EnablePinLock/UsePinLockForAutoLock flags when no PIN
                    // was ever set, so auto-lock never demands a PIN that doesn't exist.
                    bool pinConfigured = PinLockService.SyncPinFlags(_manifestPath)
                        && SettingsService.Load().EnablePinLock;
                    _vaultLockDurationService.AutoLockEnabled = pinConfigured;
                }
                catch
                {

                    _vaultLockDurationService.AutoLockEnabled = false;
                }

                _vaultLockDurationService.UnlockVault();

                _idleLockService.Reset();
                try { _suiteSession?.TouchActivity(); } catch { /* best-effort */ }

                UpdateWeakCredentials();

                CheckRotationStatus();

                await InitializeTotpSyncAsync();

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
                    "The vault database could not be loaded. Verify the vault is available and unlock it again.",
                    _ownerWindow);
                StatusMessage = "Failed to load vault";
            }
            finally
            {
                IsLoadingCredentials = false;
            }
        }

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

        public List<Credential> GetAllCredentials()
        {
            return _credentials.Select(c => c.GetCredential()).ToList();
        }

        public void AddCredentialFromImport(Credential credential)
        {
            PrivacyShield.DebugSensitive("VaultVM", () => $"AddCredentialFromImport called for: {credential.Title}");
            PrivacyShield.DebugInfo("VaultVM", $"IsPasskey: {credential.IsPasskey}");
            PrivacyShield.DebugInfo("VaultVM", $"Current credentials count: {_credentials.Count}");
            PrivacyShield.DebugInfo("VaultVM", $"Current filtered count: {FilteredCredentials.Count}");

            var credentialVm = new CredentialViewModel(credential);

            _credentials.Add(credentialVm);
            PrivacyShield.DebugInfo("VaultVM", $"Added to _credentials. New count: {_credentials.Count}");

            UpdateFlaggedCredentials();

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

                this.RaisePropertyChanged(nameof(TotalCount));

                PrivacyShield.DebugInfo("VaultVM", "Raised all property notifications");
            });

            UpdateCategoryCounts();

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
            CloseAllOverlays();
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

        private async Task OpenPhantomRecoveryAsync()
        {
            Debug.WriteLine($"[Recovery] OpenPhantomRecoveryAsync entered. IsExternalRecoveryAvailable={IsExternalRecoveryAvailable}");
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

        private void ToggleRecoveryPanel()
        {
            Debug.WriteLine($"[Recovery] ToggleRecoveryPanel entered. IsEmbeddedRecoveryAvailable={IsEmbeddedRecoveryAvailable} IsExternalRecoveryAvailable={IsExternalRecoveryAvailable}");
            if (!IsEmbeddedRecoveryAvailable)
            {
                // No embedded recovery vault yet — route the user into the consolidated
                // Recovery & Backup settings tab so they always have a setup path forward,
                // instead of silently no-oping or launching an external app behind their back.
                ShowBackupSettings();
                IsRecoveryPanelVisible = false;
                return;
            }

            IsRecoveryPanelVisible = !IsRecoveryPanelVisible;
            if (IsRecoveryPanelVisible)
            {
                EnsureVaultVisible();

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
                VaultManifest manifest;
                using (var recoveryPassphrase = SecurePassword.FromString(passphrase))
                    manifest = _manifestService.ReadManifestSecure(manifestPath, recoveryPassphrase, keyfilePath);
                return _recoveryVaultPathResolver.Resolve(manifestPath, manifest, passphrase, keyfilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Recovery] Failed to resolve the Phantom Recovery workspace.");
                return new RecoveryVaultResolution(
                    false,
                    string.Empty,
                    false,
                    false,
                    "The Phantom Recovery workspace could not be resolved from this vault. Lock and reopen the vault, then try again.");
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
                VaultManifest manifest;
                using (var bootstrapPassphrase = SecurePassword.FromString(passphrase))
                    manifest = _manifestService.ReadManifestSecure(manifestPath, bootstrapPassphrase, keyfilePath);
                _recoverySuiteBootstrapService.StageBootstrapArtifacts(
                    manifestPath,
                    manifest,
                    passphrase,
                    keyfilePath,
                    recoveryVaultPath);
            }
            catch
            {

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

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.ToLower();
                filtered = filtered.Where(c =>
                    c.Title.ToLower().Contains(search) ||
                    c.Username.ToLower().Contains(search) ||
                    c.Group.ToLower().Contains(search) ||
                    (!string.IsNullOrEmpty(c.Url) && c.Url.ToLower().Contains(search)));
            }

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

            filtered = SortOption switch
            {
                0 => filtered.OrderBy(c => c.Title),
                1 => filtered.OrderByDescending(c => c.Title),
                2 => filtered.OrderByDescending(c => c.CreatedUtc),
                3 => filtered.OrderByDescending(c => c.LastUpdatedUtc),
                4 => filtered.OrderBy(c => c.Group).ThenBy(c => c.Title),
                _ => filtered
            };

            FilteredCredentials = new ObservableCollection<CredentialViewModel>(filtered);
        }

        private void OpenSettingsPanel()
        {

            ShowGeneralSettings();
            StatusMessage = "Settings opened";
        }

        private void EnsureSettingsPanelOpen(string tabKey)
        {
            if (!IsSettingsPanelVisible)
            {
                CloseAllOverlays();
                IsSettingsPanelVisible = true;
            }
            SelectedSettingsTab = tabKey;
        }

        private async Task CloseSettingsPanelAsync()
        {
            if (_settingsDraftTracker.HasUnsavedChanges)
            {

                bool save = await _dialogService.ShowConfirmationAsync(
                    "Some settings have changed",
                    "Save the staged changes before closing? Click Cancel to stay and review.",
                    confirmText: "Save & Close",
                    cancelText: "Cancel",
                    owner: _ownerWindow);
                if (!save) return;
                int saved = _settingsDraftTracker.CommitAll();
                AnnounceSettingsSaved(saved);
            }

            IsSettingsPanelVisible = false;
            SelectedSettingsContent = null;
            if (!_settingsDraftTracker.HasUnsavedChanges)
                StatusMessage = "Settings closed";
        }

        private void SaveStagedSettings()
        {

            int n = _settingsDraftTracker.HasUnsavedChanges ? _settingsDraftTracker.CommitAll() : 0;
            AnnounceSettingsSaved(n);
        }

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
                if (!save) return;
                int saved = _settingsDraftTracker.CommitAll();
                AnnounceSettingsSaved(saved);
            }
            switchAction();
        }

        private void DiscardStagedSettings()
        {
            if (!_settingsDraftTracker.HasUnsavedChanges) return;
            int n = _settingsDraftTracker.DiscardAll();
            StatusMessage = n == 1 ? "Setting discarded" : $"Discarded {n} pending settings";
        }

        private void AnnounceSettingsSaved(int savedCount)
        {
            SettingsSaveNotification = savedCount <= 0
                ? "Saved"
                : savedCount == 1
                    ? "Setting saved"
                    : $"Saved {savedCount} settings";
            StatusMessage = SettingsSaveNotification;

            _settingsSaveNotificationCts?.Cancel();
            _settingsSaveNotificationCts?.Dispose();
            _settingsSaveNotificationCts = new CancellationTokenSource();
            var token = _settingsSaveNotificationCts.Token;

            IsSettingsSaveNotificationVisible = true;
            _ = HideSettingsSaveNotificationAsync(token);
        }

        private async Task HideSettingsSaveNotificationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1800), cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    IsSettingsSaveNotificationVisible = false;
                }
            }
            catch (OperationCanceledException)
            {

            }
        }

        private void ToggleSecurityDashboard()
        {
            IsSecurityDashboardVisible = !IsSecurityDashboardVisible;

            if (IsSecurityDashboardVisible)
            {
                _ = CloseSettingsPanelAsync();

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

                if (IsSecurityDashboardVisible)
                {
                    IsSecurityDashboardVisible = false;
                }

                UpdateWeakCredentials();
                RefreshSecurityDashboardMetrics();

                var window = new Views.SecurityDashboardWindow { DataContext = this };
                System.Diagnostics.Debug.WriteLine(">>> SecurityDashboardWindow created");

                _securityDashboardWindow = window;
                window.Closed += (_, _) => _securityDashboardWindow = null;

                window.Show();
                System.Diagnostics.Debug.WriteLine(">>> SecurityDashboardWindow shown");

                StatusMessage = "✓ Security Dashboard opened successfully";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SecurityDashboard] Failed to open the dashboard window.");
                StatusMessage = "The Security Dashboard could not be opened. Try again.";
            }
        }

        // True while the inline dashboard overlay or the standalone dashboard window is on screen —
        // gates live refresh so credential edits elsewhere don't pay the HIBP/recompute cost for nothing.
        private bool IsSecurityDashboardActive => IsSecurityDashboardVisible || _securityDashboardWindow != null;

        private void RefreshSecurityDashboardIfActive()
        {
            if (!IsSecurityDashboardActive) return;

            UpdateWeakCredentials();
            RefreshSecurityDashboardMetrics();
        }

        private void UpdateWeakCredentials()
        {
            WeakCredentials.Clear();

            foreach (var credVm in _credentials)
            {
                var cred = credVm.GetCredential();
                if (string.IsNullOrEmpty(cred.Password)) continue;

                var strength = AnalyzePasswordStrength(cred.Password);

                if (strength.Severity >= 1)
                {
                    WeakCredentials.Add(new PhantomVault.UI.Desktop.Controls.WeakCredentialItem
                    {
                        Id = cred.Title,
                        Title = cred.Title,
                        Username = cred.Username,
                        MaskedPassword = new string('\u2022', Math.Min(cred.Password.Length, 12)),
                        IssueLabel = strength.Label,
                        SeverityColor = strength.Color,
                        Severity = strength.Severity
                    });
                }
            }

            var sorted = WeakCredentials.OrderByDescending(c => c.Severity).ToList();
            WeakCredentials.Clear();
            foreach (var item in sorted)
            {
                WeakCredentials.Add(item);
            }
        }

        private void RefreshSecurityDashboardMetrics()
        {
            try
            {
                IsRefreshingDashboard = true;

                int score = 100;
                int totalCredentials = _credentials.Count;

                if (totalCredentials == 0)
                {
                    SecurityScore = 50;
                    BreachedPasswordCount = 0;
                    ReusedPasswordCount = 0;
                    ExpiringCredentialCount = 0;
                    TwoFactorEnabledCount = 0;
                    AverageEntropy = 0;
                    LastBreachCheckTime = null;
                    IsRefreshingDashboard = false;
                    FilteredCredentialsForDashboard.Clear();
                    return;
                }

                int weakCount = WeakCredentials.Count;
                score -= (weakCount * 5);

                var passwordFrequency = _credentials
                    .Where(c => !string.IsNullOrEmpty(c.Password))
                    .GroupBy(c => c.Password)
                    .Where(g => g.Count() > 1)
                    .ToList();

                int reusedCount = 0;
                foreach (var group in passwordFrequency)
                {
                    reusedCount += group.Count() - 1;
                }
                ReusedPasswordCount = reusedCount;
                score -= (reusedCount * 3);

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
                score -= (expiringCount * 2);

                int twoFactorCount = _credentials
                    .Where(c => c.HasTotpSecret)
                    .Count();
                TwoFactorEnabledCount = twoFactorCount;

                if (twoFactorCount > 0)
                {
                    int twoFactorBonus = Math.Min((twoFactorCount * 2), 15);
                    score += twoFactorBonus;
                }

                int breachedCount = _breachedPasswordCount;
                score -= (breachedCount * 10);

                var entropyValues = _credentials
                    .Where(c => !string.IsNullOrEmpty(c.Password))
                    .Select(c => PhantomVault.Core.Services.PasswordHealthService.ComputeEntropy(c.Password))
                    .ToList();
                AverageEntropy = entropyValues.Count > 0 ? entropyValues.Average() : 0;

                SecurityScore = Math.Clamp(score, 0, 100);

                LastBreachCheckTime = DateTime.UtcNow;

                PopulateDashboardCredentials();

                _ = RefreshBreachCountAsync();
            }
            finally
            {
                IsRefreshingDashboard = false;
            }
        }

        private HaveIBeenPwnedService? _hibpService;

        private static HaveIBeenPwnedService? ResolveHibpService()
        {
            var app = Avalonia.Application.Current as PhantomVault.UI.App;
            return app?.Services?.GetService(typeof(HaveIBeenPwnedService)) as HaveIBeenPwnedService;
        }

        private int _breachCheckRunning;

        private async Task RefreshBreachCountAsync()
        {

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

                var hibp = _hibpService ??= ResolveHibpService();
                if (hibp is null) return;
                if (!await hibp.RequestInternetAccessAsync().ConfigureAwait(false))
                {
                    Debug.WriteLine("[VaultViewModel] HIBP background refresh skipped: internet access denied.");
                    return;
                }

                int breached = 0;
                try
                {
                    foreach (var pw in passwords)
                    {
                        int hits = await hibp.CheckPasswordBreachAsync(pw).ConfigureAwait(false);
                        if (hits > 0) breached++;
                    }
                }
                finally
                {
                    hibp.DisableInternet();
                }

                int finalBreached = breached;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    int oldBreached = BreachedPasswordCount;
                    BreachedPasswordCount = finalBreached;

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

                PopulateDashboardCredentials();
                return;
            }

            foreach (var credVM in _credentials)
            {
                var cred = credVM.GetCredential();
                int credScore = CalculateCredentialSecurityScore(credVM, cred);

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

            int reusedCount = _credentials.Count(c => c.GetCredential().Password == cred.Password && c.GetCredential().Id != cred.Id);
            if (reusedCount > 0)
                score -= 25;

            if (cred.ExpiryUtc.HasValue)
            {
                var now = DateTimeOffset.UtcNow;
                if (cred.ExpiryUtc.Value < now)
                    score -= 50;
                else if (cred.ExpiryUtc.Value < now.AddDays(30))
                    score -= 15;
            }

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




        private void ShowGeneralSettings()
        {
            EnsureSettingsPanelOpen("general");
            SelectedSettingsContent = new Views.Settings.GeneralSettingsView();
        }

        private void ShowRecentIssues()
        {
            EnsureSettingsPanelOpen("recentissues");
            SelectedSettingsContent = new Views.Settings.RecentIssuesView();
        }

        private void ShowSecurityActivity()
        {
            EnsureSettingsPanelOpen("securityactivity");
            var view = new Views.Settings.SecurityActivityView();
            view.DataContext = new ViewModels.Settings.SecurityActivityViewModel(
                ResolveAuditLogPath(),
                new PhantomVault.Core.Services.AuditService());
            SelectedSettingsContent = view;
        }

        /// <summary>
        /// Resolves the active vault's plaintext activity log. Prefers the canonical
        /// <c>.phantom\vault.audit</c> location, falling back to the legacy drive-root path
        /// so logs written by older builds are still surfaced.
        /// </summary>
        private string? ResolveAuditLogPath()
        {
            if (string.IsNullOrEmpty(_usbRootPath) && string.IsNullOrEmpty(_mountPath))
                return null;

            string driveRoot = !string.IsNullOrEmpty(_usbRootPath) ? _usbRootPath! : _mountPath;
            if (string.Equals(Path.GetFileName(driveRoot.TrimEnd(Path.DirectorySeparatorChar)),
                    PhantomVault.Core.Services.PhantomDeviceLayout.PhantomFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                driveRoot = Path.GetDirectoryName(driveRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? driveRoot;
            }

            var canonical = PhantomVault.Core.Services.PhantomDeviceLayout.GetAuditLogPath(driveRoot);
            if (File.Exists(canonical))
                return canonical;

            var legacy = Path.Combine(driveRoot, "vault.audit");
            if (File.Exists(legacy))
                return legacy;

            // Return the canonical path even if it doesn't exist yet so the viewer can show
            // a clean "no activity recorded" state rather than an error.
            return canonical;
        }

        /// <summary>
        /// Appends an entry to the device's hash-chained activity log. Best-effort and never
        /// throws — audit logging must never block or break a security action.
        /// </summary>
        private void LogSecurityActivity(string category, string message)
        {
            try
            {
                var path = ResolveAuditLogPath();
                if (string.IsNullOrWhiteSpace(path))
                    return;
                new PhantomVault.Core.Services.AuditService().LogEvent(path, category, message);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Activity-log write skipped");
            }
        }

        private void ShowSecuritySettings()
        {
            EnsureSettingsPanelOpen("security");
            var view = new Views.Settings.SecuritySettingsView();
            view.DataContext = new SecuritySettingsViewModel(
                defenceSettingsService: null,
                manifestPath: _manifestPath,
                hostViewModel: this);
            SelectedSettingsContent = view;
        }

        private void ShowThemeSettings()
        {
            EnsureSettingsPanelOpen("theme");
            SelectedSettingsContent = new Views.Settings.ThemeSettingsView();
        }

        private void ShowSubscriptionSettings()
        {
            EnsureSettingsPanelOpen("subscription");
            var view = new Views.Settings.SubscriptionSettingsView
            {
                DataContext = new ViewModels.Settings.SubscriptionSettingsViewModel(this)
            };
            SelectedSettingsContent = view;
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
                Log.Error(ex, "[Import] Failed to open the import window.");
                await _dialogService.ShowErrorAsync("Import Error", "The import window could not be opened. Try again.", _ownerWindow);
            }
        }

        private async Task ExportVaultAsync(string format)
        {
            if (_ownerWindow == null) return;

            try
            {

                var credentials = GetAllCredentials();
                var exportWindow = new ExportWindow(credentials);

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
                Log.Error(ex, "[Export] Failed to open the export window.");
                await _dialogService.ShowErrorAsync("Export Error", "The export window could not be opened. Try again.", _ownerWindow);
            }
        }

        private void ShowAutoFillSettings()
        {
            EnsureSettingsPanelOpen("autofill");
            var trayService = (Application.Current as App)?.Services?.GetService(typeof(PhantomVault.UI.Services.TrayBackground.ITrayBackgroundService))
                              as PhantomVault.UI.Services.TrayBackground.ITrayBackgroundService;
            SelectedSettingsContent = new Views.Settings.AutoFillSettingsView
            {
                DataContext = new AutoFillSettingsViewModel(trayService, _settingsDraftTracker)
            };
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
            CloseAllOverlays();
            EnsureVaultVisible();
            IsPasswordHealthPanelVisible = true;
            IsSidebarCollapsed = false;

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
                manifestContextResolver: resolver,
                integratedRecoveryService: _integratedRecoveryService,
                openPhantomRecoveryAsync: OpenPhantomRecoveryAsync,
                openRecoveryActivation: () =>
                {
                    EnsureSettingsPanelOpen("recoveryactivation");
                    var raVm = ((App)Application.Current!).Services?.GetService(typeof(UI.ViewModels.Settings.RecoveryActivationSettingsViewModel))
                        as UI.ViewModels.Settings.RecoveryActivationSettingsViewModel;
                    SelectedSettingsContent = new Views.Settings.RecoveryActivationSettingsView { DataContext = raVm };
                });

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
            // Security-first: Advanced Settings hosts safety controls (auto-lock/session
            // timeout, privacy mode, log redaction, debugger detection) and the privacy
            // transparency dashboard. Protective and transparency features must never sit
            // behind the paywall, so this view is available on every tier. Monetisation is
            // applied to genuinely cosmetic / power-user features (themes, icon manager,
            // multi-vault, encrypted drive mount) instead.
            EnsureSettingsPanelOpen("advanced");
            SelectedSettingsContent = new Views.Settings.AdvancedSettingsView();
        }

        /// <summary>
        /// Gate helper: returns true if the feature is unlocked. Otherwise opens the
        /// Subscription tab and surfaces a message, and returns false.
        /// </summary>
        private bool EnsurePremiumOrPrompt(PhantomVault.Core.Models.Licensing.PremiumFeature feature, string message)
        {
            if (_entitlementService?.IsUnlocked(feature) ?? false) return true;
            StatusMessage = message + " Upgrade to unlock.";
            ShowSubscriptionSettings();
            return false;
        }

        private void ShowRubbishBinSettings()
        {
            EnsureSettingsPanelOpen("rubbishbin");
            SelectedSettingsContent = new Views.Settings.RubbishBinSettingsView();
        }

        private void OpenIconManager()
        {
            if (!EnsurePremiumOrPrompt(PhantomVault.Core.Models.Licensing.PremiumFeature.IconManager,
                    "The Icon Manager is a Premium feature.")) return;

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
                Log.Fatal(ex, "[Lock] Failed to lock the vault after USB removal.");
                StatusMessage = "The vault could not confirm it locked after USB removal. Close the vault immediately.";
            }
        }

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
                    var manifest = _manifestService.ReadManifestSecure(manifestPath, _vaultPassword ?? SecurePassword.Empty(), _vaultKeyfilePath);
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
                    null,
                    progress,
                    CancellationToken.None);

                if (result.Success)
                {

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
                    "Encryption keys could not be rotated. The existing vault keys remain in use.",
                    _ownerWindow);
                Log.Error(ex, "Encryption-key rotation failed");
                StatusMessage = "Encryption-key rotation failed.";
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

        /// <summary>
        /// Returns a short label for the currently-active manifest KDF tier so the Security
        /// settings panel can show "Current: Standard (256 MiB / 6 iter)" vs "Current: Fast".
        /// </summary>
        public string CurrentManifestKdfDisplay
        {
            get
            {
                var p = _cachedRuntimeManifest?.RuntimeKdfParams;
                if (p == null) return "Unknown";
                if (p == PhantomVault.Core.Models.ManifestKdfParams.Fast) return $"Fast ({p.MemoryKb / 1024} MiB / {p.Iterations} iter)";
                if (p == PhantomVault.Core.Models.ManifestKdfParams.Standard) return $"Standard ({p.MemoryKb / 1024} MiB / {p.Iterations} iter)";
                return $"Custom ({p.MemoryKb / 1024} MiB / {p.Iterations} iter)";
            }
        }

        public bool IsManifestKdfFast
            => _cachedRuntimeManifest?.RuntimeKdfParams?.Equals(PhantomVault.Core.Models.ManifestKdfParams.Fast) == true;

        /// <summary>
        /// Re-encrypt the live manifest under the requested KDF tier. Touches only the manifest
        /// envelope — the vault database's encryption key is derived separately at fixed
        /// parameters and is left intact, so this is safe to run on a populated vault.
        /// </summary>
        public async Task<bool> RekeyManifestForFastUnlockAsync(bool useFast)
        {
            if (string.IsNullOrWhiteSpace(_manifestPath))
            {
                StatusMessage = "Re-key failed: no manifest path";
                return false;
            }
            if (_cachedRuntimeManifest == null)
            {
                StatusMessage = "Re-key failed: manifest not loaded";
                return false;
            }

            var target = useFast
                ? PhantomVault.Core.Models.ManifestKdfParams.Fast
                : PhantomVault.Core.Models.ManifestKdfParams.Standard;

            try
            {
                IsBusy = true;
                StatusMessage = useFast ? "Re-keying manifest for Fast Unlock…" : "Re-keying manifest for Standard security…";

                await Task.Run(() =>
                {
                    _manifestService.WriteManifestSecure(
                        _cachedRuntimeManifest,
                        _manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath,
                        usbSerial: null,
                        requireDualFactor: false,
                        overrideKdfParams: target);
                }).ConfigureAwait(false);

                this.RaisePropertyChanged(nameof(CurrentManifestKdfDisplay));
                this.RaisePropertyChanged(nameof(IsManifestKdfFast));
                StatusMessage = useFast
                    ? "Fast Unlock applied — next unlock will be faster"
                    : "Standard security restored";
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VaultViewModel] RekeyManifestForFastUnlockAsync failed");
                Log.Error(ex, "Vault manifest re-key failed");
                await _dialogService.ShowErrorAsync("Re-key Failed", ErrorMessageService.GetUserSafeMessage(ex), _ownerWindow);
                StatusMessage = "Re-key failed";
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Persists a verified premium license token into the live manifest and
        /// re-applies entitlements. The token is stored inside the encrypted,
        /// integrity-signed manifest so it can't be transplanted between vaults.
        /// </summary>
        public async Task<bool> PersistLicenseTokenAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(_manifestPath) || _cachedRuntimeManifest == null)
            {
                StatusMessage = "Activation failed: vault manifest not loaded";
                return false;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Applying subscription…";

                _cachedRuntimeManifest.PremiumLicenseToken = token;

                await Task.Run(() =>
                {
                    _manifestService.WriteManifestSecure(
                        _cachedRuntimeManifest,
                        _manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath,
                        usbSerial: null,
                        requireDualFactor: false);
                }).ConfigureAwait(false);

                ApplyEntitlementsFromManifest(_cachedRuntimeManifest);
                StatusMessage = IsPremium ? "Premium subscription active" : "Subscription updated";
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VaultViewModel] PersistLicenseTokenAsync failed");
                Log.Error(ex, "Subscription activation failed");
                await _dialogService.ShowErrorAsync("Activation Failed", ErrorMessageService.GetUserSafeMessage(ex), _ownerWindow);
                StatusMessage = "Activation failed";
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public string CurrentKeyfileDisplay
        {
            get
            {
                var kf = _vaultKeyfilePath;
                if (string.IsNullOrEmpty(kf)) return "No keyfile on record";
                try { return $"{Path.GetFileName(kf)}  —  {Path.GetDirectoryName(kf)}"; }
                catch { return kf; }
            }
        }

        private async Task RegenerateKeyfileAsync()
        {
            if (!TryGetKeyfileContext(out var manifestPath, out var passphrase, out var keyfilePath))
                return;

            var confirm = await _dialogService.ShowConfirmationAsync(
                "Regenerate Keyfile",
                "This generates a brand-new keyfile and re-encrypts your vault under it. " +
                "Your password is unchanged.\n\nBack up the new keyfile afterwards — without it " +
                "(and your password, if set) the vault cannot be opened. Continue?",
                _ownerWindow);
            if (!confirm) return;

            string? entropyTempPath = null;
            try
            {
                if (_ownerWindow != null)
                {
                    var entropyDialog = new Views.EntropyKeyfileGeneratorWindow();
                    var entropyResult = await entropyDialog.ShowDialog<Services.EntropyKeyfileGenerationResult?>(_ownerWindow);
                    if (entropyResult == null)
                    {
                        StatusMessage = "Keyfile regeneration cancelled.";
                        return;
                    }

                    var keyfileDir = Path.GetDirectoryName(keyfilePath!) ?? Path.GetTempPath();
                    entropyTempPath = Path.Combine(keyfileDir, $"vault.key.entropy-{Guid.NewGuid():N}.tmp");
                    try
                    {
                        await File.WriteAllBytesAsync(entropyTempPath, entropyResult.KeyMaterial);
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(entropyResult.KeyMaterial);
                    }
                }

                await RunKeyfileRekeyAsync(manifestPath, passphrase, keyfilePath,
                    providedNewKeyfile: entropyTempPath ?? keyfilePath,
                    successTitle: "Keyfile Regenerated",
                    successMessage: "A new keyfile was generated and your vault re-encrypted. " +
                                    "Use Backup Keyfile to save a copy somewhere safe.");
            }
            finally
            {
                if (entropyTempPath != null)
                {
                    try { if (File.Exists(entropyTempPath)) File.Delete(entropyTempPath); }
                    catch {  }
                }
            }
        }

        private async Task ChangeKeyfileAsync()
        {
            if (!TryGetKeyfileContext(out var manifestPath, out var passphrase, out var keyfilePath))
                return;

            if (_ownerWindow?.StorageProvider is not { } storage)
            {
                await _dialogService.ShowErrorAsync("Unavailable", "File picker is not available.", _ownerWindow);
                return;
            }

            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Keyfile",
                AllowMultiple = false
            });
            var picked = files is { Count: > 0 } ? files[0] : null;
            var newKeyfile = picked?.Path?.LocalPath;
            if (string.IsNullOrEmpty(newKeyfile)) return;

            var confirm = await _dialogService.ShowConfirmationAsync(
                "Change Keyfile",
                $"Re-encrypt your vault using the selected keyfile?\n\n{Path.GetFileName(newKeyfile)}\n\n" +
                "Keep that keyfile safe — it will be required (with your password, if set) to open the vault.",
                _ownerWindow);
            if (!confirm) return;

            await RunKeyfileRekeyAsync(manifestPath, passphrase, keyfilePath,
                providedNewKeyfile: newKeyfile,
                successTitle: "Keyfile Changed",
                successMessage: "Your vault is now protected by the selected keyfile.");
        }

        private async Task BackupKeyfileAsync()
        {
            if (string.IsNullOrEmpty(_vaultKeyfilePath) || !File.Exists(_vaultKeyfilePath))
            {
                await _dialogService.ShowErrorAsync("No Keyfile", "There is no keyfile to back up.", _ownerWindow);
                return;
            }

            if (_ownerWindow?.StorageProvider is not { } storage)
            {
                await _dialogService.ShowErrorAsync("Unavailable", "File picker is not available.", _ownerWindow);
                return;
            }

            var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Backup Keyfile",
                SuggestedFileName = "vault.key"
            });
            var dest = file?.Path?.LocalPath;
            if (string.IsNullOrEmpty(dest)) return;

            try
            {
                File.Copy(_vaultKeyfilePath!, dest, overwrite: true);
                await _dialogService.ShowSuccessAsync(
                    "Keyfile Backed Up",
                    $"A copy of your keyfile was saved to:\n{dest}\n\nStore it somewhere safe and separate from your vault.",
                    _ownerWindow);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Keyfile backup failed");
                await _dialogService.ShowErrorAsync("Backup Failed", "The keyfile backup could not be created. Your current keyfile was not changed.", _ownerWindow);
            }
        }

        private bool TryGetKeyfileContext(out string manifestPath, out string? passphrase, out string? keyfilePath)
        {
            manifestPath = string.Empty; passphrase = null; keyfilePath = null;
            if (_rekeyService == null)
            {
                _ = _dialogService.ShowErrorAsync("Service Unavailable", "The key rotation service is not available.", _ownerWindow);
                return false;
            }
            if (!TryGetManifestContext(out manifestPath, out passphrase, out keyfilePath))
            {
                _ = _dialogService.ShowErrorAsync("Manifest Not Found", "Ensure the vault is unlocked and mounted.", _ownerWindow);
                return false;
            }
            if (string.IsNullOrEmpty(keyfilePath))
            {
                _ = _dialogService.ShowErrorAsync("Keyfile Required", "This vault has no keyfile on record.", _ownerWindow);
                return false;
            }
            return true;
        }

        private async Task RunKeyfileRekeyAsync(string manifestPath, string? passphrase, string? keyfilePath,
            string? providedNewKeyfile, string successTitle, string successMessage)
        {
            IsBusy = true;
            StatusMessage = "Updating keyfile...";
            try
            {
                bool success = _rekeyService!.RekeyVault(
                    manifestPath,
                    passphrase ?? string.Empty,
                    passphrase ?? string.Empty,
                    keyfilePath,
                    providedNewKeyfile);

                if (!success)
                {
                    await _dialogService.ShowErrorAsync("Keyfile Update Failed",
                        "Failed to update the keyfile. Your existing keyfile remains in place.", _ownerWindow);
                    StatusMessage = "Keyfile update failed";
                    return;
                }

                var rotatedKeyfile = Path.Combine(Path.GetDirectoryName(keyfilePath!) ?? string.Empty, "vault.key.new");
                if (File.Exists(rotatedKeyfile))
                {
                    _vaultKeyfilePath = rotatedKeyfile;
                    _reauthKeyfilePath = rotatedKeyfile;
                    this.RaisePropertyChanged(nameof(CurrentKeyfileDisplay));
                }

                StatusMessage = successTitle;
                await _dialogService.ShowSuccessAsync(successTitle, successMessage, _ownerWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Keyfile Update Failed",
                    "The keyfile could not be updated. The existing keyfile remains required.", _ownerWindow);
                Log.Error(ex, "Keyfile update failed");
                StatusMessage = "Keyfile update failed.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChangeMasterPasswordAsync()
        {
            if (_rekeyService == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Service Unavailable",
                    "The key rotation service is not available.",
                    _ownerWindow);
                return;
            }

            if (!TryGetManifestContext(out var manifestPath, out var passphrase, out var keyfilePath))
            {
                await _dialogService.ShowErrorAsync(
                    "Manifest Not Found",
                    "Cannot find the vault manifest. Ensure the vault is unlocked and mounted.",
                    _ownerWindow);
                return;
            }

            if (string.IsNullOrEmpty(keyfilePath))
            {
                await _dialogService.ShowErrorAsync(
                    "Keyfile Required",
                    "This vault has no keyfile on record, which is required to change the master password.",
                    _ownerWindow);
                return;
            }

            bool hasCurrentPassword = !string.IsNullOrEmpty(passphrase);

            var dialog = new Views.ChangeMasterPasswordDialog(hasCurrentPassword);
            if (_ownerWindow != null)
                await dialog.ShowDialog(_ownerWindow);
            else
                dialog.Show();

            if (!dialog.Confirmed)
                return;

            if (hasCurrentPassword &&
                !string.Equals(dialog.CurrentPassword, passphrase, StringComparison.Ordinal))
            {
                await _dialogService.ShowErrorAsync(
                    "Incorrect Password",
                    "The current password you entered is incorrect. No changes were made.",
                    _ownerWindow);
                return;
            }

            var newPassword = dialog.NewPassword;

            IsBusy = true;
            StatusMessage = "Changing master password...";
            try
            {
                bool success = _rekeyService.RekeyVault(
                    manifestPath,
                    passphrase ?? string.Empty,
                    newPassword,
                    keyfilePath,
                    keyfilePath);

                if (!success)
                {
                    await _dialogService.ShowErrorAsync(
                        "Change Failed",
                        "Failed to change the master password. Your existing password remains in place.",
                        _ownerWindow);
                    StatusMessage = "Master password change failed";
                    return;
                }

                _vaultPassword?.Dispose();
                _vaultPassword = string.IsNullOrEmpty(newPassword) ? null : SecurePassword.FromString(newPassword);
                var rotatedKeyfile = Path.Combine(
                    Path.GetDirectoryName(keyfilePath) ?? string.Empty, "vault.key.new");
                if (File.Exists(rotatedKeyfile))
                {
                    _vaultKeyfilePath = rotatedKeyfile;
                    _reauthKeyfilePath = rotatedKeyfile;
                }

                StatusMessage = "Master password changed";
                await _dialogService.ShowSuccessAsync(
                    "Password Changed",
                    string.IsNullOrEmpty(newPassword)
                        ? "Your master password was removed. The vault is now protected by its keyfile alone."
                        : "Your master password has been changed. Use the new password next time you unlock.",
                    _ownerWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Change Failed",
                    "The master password could not be changed. Your previous password remains valid.",
                    _ownerWindow);
                Log.Error(ex, "Master-password change failed");
                StatusMessage = "Master-password change failed.";
            }
            finally
            {
                IsBusy = false;
            }
        }





        internal bool TryGetTierMigrationContext(out VaultTierMigrationContext? context)
        {
            context = null;
            if (string.IsNullOrWhiteSpace(_manifestPath) ||
                string.IsNullOrWhiteSpace(_transportLayoutRoot) ||
                string.IsNullOrWhiteSpace(_usbRootPath))
            {
                return false;
            }

            context = new VaultTierMigrationContext(
                _manifestPath,
                GetVaultPasswordString(),
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

    }
}


