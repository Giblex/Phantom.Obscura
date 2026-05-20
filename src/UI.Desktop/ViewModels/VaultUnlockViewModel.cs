using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Reactive;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Layout;
using ReactiveUI;
using Serilog;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using PhantomVault.UI.Services;
using PhantomVault.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for vault unlock flow. Handles vault discovery,
    /// validation, and navigation to the main vault window.
    /// Includes throttling for failed unlock attempts.
    /// </summary>
    public sealed class VaultUnlockViewModel : ReactiveObject
    {
        private readonly string _usbPath;
        private readonly DialogService _dialogService;
        private readonly VaultLockDurationService _vaultLockDurationService;
        private readonly SecureTrashService _secureTrashService;
        private readonly EncryptionService _encryptionService;
        private readonly UsbArtifactProtectionService _usbArtifactProtectionService;
        private readonly PhantomKeyBridgeValidator _phantomKeyBridgeValidator;
        private readonly IconManager _iconManager;
        private readonly UsbDetector _usbDetector;
        private readonly UnlockThrottleService _throttleService;
        private readonly BlackSecureRawVolumeService _blackSecureRawVolumeService;
        private readonly string? _preferredVaultPath;

        private bool _isBusy;
        private string _status = "Searching for vault...";
        private int _progressPercent;
        private Window? _ownerWindow;

        public VaultUnlockViewModel(
            string usbPath,
            DialogService dialogService,
            VaultLockDurationService vaultLockDurationService,
            SecureTrashService secureTrashService,
            EncryptionService encryptionService,
            IconManager iconManager,
            UsbDetector usbDetector,
            string? preferredVaultPath = null)
        {
            _usbPath = usbPath ?? throw new ArgumentNullException(nameof(usbPath));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _vaultLockDurationService = vaultLockDurationService ?? throw new ArgumentNullException(nameof(vaultLockDurationService));
            _secureTrashService = secureTrashService ?? throw new ArgumentNullException(nameof(secureTrashService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _usbArtifactProtectionService = new UsbArtifactProtectionService(_encryptionService);
            _phantomKeyBridgeValidator = new PhantomKeyBridgeValidator(_usbArtifactProtectionService);
            _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));
            _usbDetector = usbDetector ?? throw new ArgumentNullException(nameof(usbDetector));
            _throttleService = new UnlockThrottleService();
            _blackSecureRawVolumeService = new BlackSecureRawVolumeService();
            _preferredVaultPath = preferredVaultPath;
        }

        /// <summary>
        /// Searches for a keyfile (.key) on the USB drive.
        /// </summary>
        private string? FindKeyfileOnDrive(string drivePath)
        {
            if (_blackSecureRawVolumeService.IsRawSelection(drivePath))
                return null;

            var searchPaths = new[]
            {
            Path.Combine(drivePath, ".phantom", "vaults"),
            Path.Combine(drivePath, ".phantom"),
            drivePath,
            Path.Combine(drivePath, "keys")
        };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                var keyFiles = Directory.GetFiles(searchPath, "*.key", SearchOption.TopDirectoryOnly);
                if (keyFiles.Length > 0)
                {
                    var usbKeyfilePath = keyFiles[0];
                    var hostCompanionPath = TryResolveHostCompanionKeyfilePath(drivePath);
                    return string.IsNullOrWhiteSpace(hostCompanionPath)
                        ? usbKeyfilePath
                        : PhantomVault.Core.Utils.CompositeKeyfilePath.Compose(usbKeyfilePath, hostCompanionPath);
                }
            }

            return null;
        }

        private static string? TryResolveHostCompanionKeyfilePath(string drivePath)
        {
            try
            {
                var locatorPath = Path.Combine(drivePath, ".phantom", "host-key", "companion.locator");
                if (!File.Exists(locatorPath))
                    return null;

                var locator = JsonSerializer.Deserialize<HostCompanionLocator>(File.ReadAllText(locatorPath));
                if (locator == null || string.IsNullOrWhiteSpace(locator.HostCompanionKeyfilePath))
                    return null;

                return File.Exists(locator.HostCompanionKeyfilePath)
                    ? locator.HostCompanionKeyfilePath
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class HostCompanionLocator
        {
            public string HostCompanionKeyfilePath { get; set; } = string.Empty;
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string Status
        {
            get => _status;
            private set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        /// <summary>
        /// Unlock progress percentage (0-100) for visual progress bar.
        /// </summary>
        public int ProgressPercent
        {
            get => _progressPercent;
            private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        /// <summary>
        /// Discovers vault on USB path, validates it, and navigates to VaultWindow.
        /// </summary>
        public async Task UnlockVaultAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            string? extractedVolumeRoot = null;

            try
            {
                ProgressPercent = 5;
                Status = "Validating vault location...";

                string? selectedDriveRoot = _blackSecureRawVolumeService.IsRawSelection(_usbPath) ? null : _usbPath;
                string? selectedPhysicalDrivePath = _blackSecureRawVolumeService.IsRawSelection(_usbPath)
                    ? _blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromSelection(_usbPath)
                    : null;

                // Validate USB path
                if (string.IsNullOrWhiteSpace(selectedDriveRoot) && string.IsNullOrWhiteSpace(selectedPhysicalDrivePath))
                {
                    await _dialogService.ShowErrorAsync(
                        "Invalid Path",
                        "USB path is empty or invalid.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // Find the root authority container (.pvault) or extract the master Obscura volume.
                var rootPath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? string.Empty : Path.Combine(selectedDriveRoot, ".phantom", "root");
                var vaultsPath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? string.Empty : Path.Combine(selectedDriveRoot, ".phantom", "vaults");
                var manifestsPath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? string.Empty : Path.Combine(selectedDriveRoot, ".phantom", "manifests");
                var bindingRecordPath = Path.Combine(rootPath, "usb.binding.pmeta");
                string? manifestPath = null;
                var masterVolumePath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : ResolveMasterVolumePath(selectedDriveRoot);

                if (!string.IsNullOrWhiteSpace(_preferredVaultPath))
                {
                    if (_preferredVaultPath.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase) ||
                        _preferredVaultPath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        manifestPath = _preferredVaultPath;
                    }
                    else if (_preferredVaultPath.EndsWith("system.bin", StringComparison.OrdinalIgnoreCase) ||
                             _preferredVaultPath.EndsWith("obscura.vol", StringComparison.OrdinalIgnoreCase))
                    {
                        masterVolumePath = _preferredVaultPath;
                    }
                }

                if (masterVolumePath != null)
                {
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    var obscuraVolumeService = new ObscuraVolumeService();
                    await obscuraVolumeService.ExtractVolumeAsync(masterVolumePath, extractedVolumeRoot).ConfigureAwait(false);

                    rootPath = Path.Combine(extractedVolumeRoot, "root");
                    vaultsPath = Path.Combine(extractedVolumeRoot, "vaults");
                    manifestsPath = Path.Combine(extractedVolumeRoot, "manifests");
                    bindingRecordPath = Path.Combine(rootPath, "usb.binding.pmeta");
                }
                else if (!string.IsNullOrWhiteSpace(selectedPhysicalDrivePath) &&
                         await _blackSecureRawVolumeService.IsBlackSecureVolumeAsync(selectedPhysicalDrivePath).ConfigureAwait(false))
                {
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    await _blackSecureRawVolumeService.ExtractVolumeAsync(selectedPhysicalDrivePath, extractedVolumeRoot).ConfigureAwait(false);

                    rootPath = Path.Combine(extractedVolumeRoot, "root");
                    vaultsPath = Path.Combine(extractedVolumeRoot, "vaults");
                    manifestsPath = Path.Combine(extractedVolumeRoot, "manifests");
                    bindingRecordPath = Path.Combine(rootPath, "usb.binding.pmeta");
                }

                // Prefer the root authority container for the three-container layout.
                if (manifestPath == null && Directory.Exists(rootPath))
                {
                    var rootContainers = Directory.GetFiles(rootPath, "*.pvault");
                    if (rootContainers.Length > 0)
                        manifestPath = rootContainers[0];
                }

                // Fall back to legacy single-container layout.
                if (manifestPath == null && Directory.Exists(vaultsPath))
                {
                    var pvaultFiles = Directory.GetFiles(vaultsPath, "*.pvault");
                    if (pvaultFiles.Length > 0)
                        manifestPath = pvaultFiles[0];
                }

                if (manifestPath == null && Directory.Exists(manifestsPath))
                {
                    var legacyFiles = Directory.GetFiles(manifestsPath, "*.manifest");
                    if (legacyFiles.Length > 0)
                        manifestPath = legacyFiles[0];
                }

                if (manifestPath == null)
                {
                    await _dialogService.ShowErrorAsync(
                        "Vault Not Found",
                        "Could not find any vault files on this USB drive.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // Check for unlock throttling before prompting for password
                if (_throttleService.IsThrottled(manifestPath, out var remainingLockout))
                {
                    var minutes = (int)Math.Ceiling(remainingLockout.TotalMinutes);
                    await _dialogService.ShowErrorAsync(
                        "Too Many Failed Attempts",
                        $"This vault is temporarily locked due to multiple failed unlock attempts.\n\n" +
                        $"Please wait {minutes} minute(s) before trying again.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                // Check for keyfile-only authentication (auto-unlock)
                var keyfilePath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : FindKeyfileOnDrive(selectedDriveRoot);
                string? password = null;

                // Create services for vault window
                var encryptionService = new EncryptionService();
                var containerService = new PhantomContainerService(encryptionService);
                var manifestService = new ManifestService(encryptionService, containerService);

                if (!string.IsNullOrEmpty(keyfilePath))
                {
                    // Try keyfile-only authentication first (no password prompt)
                    ProgressPercent = 25;
                    Status = "Authenticating with keyfile...";

                    try
                    {
                        // Try to read manifest with keyfile only (empty password)
                        var keyfileTestManifest = manifestService.ReadManifest(manifestPath, string.Empty, keyfilePath);
                        if (keyfileTestManifest != null)
                        {
                            // Keyfile-only authentication successful!
                            password = string.Empty;
                            Status = "Authenticated with keyfile";
                        }
                    }
                    catch
                    {
                        // Keyfile-only failed, will fall back to password prompt
                    }
                }

                // If keyfile auth failed or no keyfile, try no-password unlock first
                if (password == null)
                {
                    ProgressPercent = 25;
                    Status = "Checking authentication requirements...";

                    try
                    {
                        // Try empty password — vault may not be password-protected
                        var noPassManifest = manifestService.ReadManifest(manifestPath, string.Empty, null);
                        if (noPassManifest != null)
                        {
                            password = string.Empty;
                        }
                    }
                    catch
                    {
                        // Empty password failed — vault requires authentication
                    }
                }

                // If still no password, prompt for one
                if (password == null)
                {
                    ProgressPercent = 25;
                    Status = "Authentication required...";
                    password = await PromptForPasswordAsync();
                    if (string.IsNullOrEmpty(password))
                    {
                        await _dialogService.ShowErrorAsync(
                            "Authentication Required",
                            "A passphrase is required to unlock the vault.",
                            _ownerWindow);
                        CloseAndReturnToWelcome();
                        return;
                    }
                }

                ProgressPercent = 40;
                Status = "Initializing vault services...";

                ProgressPercent = 55;
                Status = "Validating passphrase (deriving key)...";

                // CRITICAL: Validate passphrase by attempting to decrypt the manifest
                VaultManifest? testManifest = null;

                try
                {
                    testManifest = manifestService.ReadManifest(manifestPath, password, keyfilePath);
                    if (testManifest == null)
                    {
                        // Register failed attempt for throttling
                        _throttleService.RegisterFailedAttempt(manifestPath);
                        var failedCount = _throttleService.GetFailedAttemptCount(manifestPath);

                        await _dialogService.ShowErrorAsync(
                            "Invalid Passphrase",
                            $"The passphrase is incorrect or the vault is corrupted.\n\n" +
                            $"Failed attempts: {failedCount}",
                            _ownerWindow);
                        CloseAndReturnToWelcome();
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(extractedVolumeRoot))
                    {
                        if (!string.IsNullOrWhiteSpace(masterVolumePath))
                            ResolveRuntimePaths(testManifest, extractedVolumeRoot, masterVolumePath);
                        else
                            ResolveExtractedRuntimePaths(testManifest, extractedVolumeRoot);
                    }
                    else
                    {
                        ResolveDirectRuntimePaths(testManifest, manifestPath, rootPath, vaultsPath);
                    }

                    var resolvedBindingRecordPath = string.IsNullOrWhiteSpace(testManifest.BindingRecordPath)
                        ? bindingRecordPath
                        : testManifest.BindingRecordPath;

                    ValidateUsbTopology(testManifest, resolvedBindingRecordPath);
                    ValidateUsbBinding(selectedPhysicalDrivePath ?? selectedDriveRoot!, resolvedBindingRecordPath, testManifest, password, keyfilePath);
                    ValidateRecoveryArtifacts(testManifest, password, keyfilePath);
                    _phantomKeyBridgeValidator.Validate(testManifest, password, keyfilePath);

                    // Successful passphrase - continue to TOTP check

                    // Check for additional auth requirements from manifest
                    if (testManifest.RequiresTotp && !string.IsNullOrEmpty(testManifest.TotpSecret))
                    {
                        ProgressPercent = 70;
                        Status = "TOTP verification required...";
                        var totpCode = await PromptForTotpAsync();
                        if (string.IsNullOrEmpty(totpCode) || !ValidateTotpCode(testManifest.TotpSecret, totpCode))
                        {
                            // Register failed TOTP attempt
                            _throttleService.RegisterFailedAttempt(manifestPath);

                            await _dialogService.ShowErrorAsync(
                                "TOTP Verification Failed",
                                "The TOTP code is invalid or expired.",
                                _ownerWindow);
                            CloseAndReturnToWelcome();
                            return;
                        }
                    }

                    // Successful authentication - reset throttle counter
                    _throttleService.ResetAttempts(manifestPath);
                }
                catch (Exception decryptEx)
                {
                    // Register failed attempt for throttling
                    _throttleService.RegisterFailedAttempt(manifestPath);
                    var failedCount = _throttleService.GetFailedAttemptCount(manifestPath);

                    await _dialogService.ShowErrorAsync(
                        "Authentication Failed",
                        $"Failed to decrypt vault: {decryptEx.Message}\n\n" +
                        $"Failed attempts: {failedCount}",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

                var vaultOptions = new Core.Options.VaultOptions();
                var vaultService = new VaultService(vaultOptions, _encryptionService);
                var idleLockService = new IdleLockService(TimeSpan.FromMinutes(15));
                var zkVaultService = new Core.Services.ZeroKnowledge.ZkVaultService();

                ProgressPercent = 82;
                Status = "Unlocking zero-knowledge vault...";

                var deviceBindingId = !string.IsNullOrWhiteSpace(testManifest!.DeviceId)
                    ? testManifest.DeviceId
                    : null;
                byte[] vaultDatabaseKey = new VaultDatabaseKeyService(encryptionService)
                    .DeriveKey(testManifest, password, keyfilePath);
                bool zkUnlocked;
                try
                {
                    zkUnlocked = await zkVaultService.UnlockWithHybridKeyAsync(vaultDatabaseKey).ConfigureAwait(false);
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(vaultDatabaseKey);
                }

                if (!zkUnlocked)
                {
                    throw new InvalidOperationException("Failed to unlock the zero-knowledge vault service with the validated manifest credentials.");
                }

                var runtimeVaultRoot = DetermineRuntimeVaultRoot(testManifest, manifestPath);
                if (!Directory.Exists(runtimeVaultRoot))
                {
                    throw new DirectoryNotFoundException($"The resolved vault runtime path does not exist: {runtimeVaultRoot}");
                }

                var runtimeVaultDatabasePath = ResolveVaultDatabasePath(runtimeVaultRoot);
                if (runtimeVaultDatabasePath == null)
                {
                    throw new FileNotFoundException("The vault database could not be located in the resolved runtime path.", runtimeVaultRoot);
                }

                ProgressPercent = 90;
                Status = "Loading vault contents...";

                // Create and show vault window - pass the validated password
                var vaultViewModel = new VaultViewModel(
                    vaultService,
                    manifestService,
                    idleLockService,
                    zkVaultService,
                    _dialogService,
                    _vaultLockDurationService,
                    _usbDetector,
                    _secureTrashService,
                    _iconManager);

                vaultViewModel.SetManifestContext(manifestPath, password, keyfilePath);
                await vaultViewModel.LoadAsync(runtimeVaultRoot, password, keyfilePath).ConfigureAwait(false);

                ProgressPercent = 95;
                Status = "Preparing vault interface...";

                ProgressPercent = 100;
                Status = "Vault unlocked!";

                Window? openedVaultWindow = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var vaultWindow = new VaultWindow
                    {
                        DataContext = vaultViewModel
                    };

                    vaultViewModel.SetOwnerWindow(vaultWindow);
                    if (!string.IsNullOrWhiteSpace(extractedVolumeRoot))
                    {
                        var extractedRootForCleanup = extractedVolumeRoot;
                        vaultWindow.Closed += (_, _) => DeleteExtractedVolumeRoot(extractedRootForCleanup);
                        extractedVolumeRoot = null;
                    }

                    vaultWindow.Show();

                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt)
                    {
                        dt.MainWindow = vaultWindow;
                    }

                    _ownerWindow?.Close();
                    openedVaultWindow = vaultWindow;
                });

                if (openedVaultWindow == null)
                    throw new InvalidOperationException("Vault window could not be created on the UI thread.");

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await RevealGeneratedPasswordBootstrapAsync(selectedDriveRoot, testManifest!, password, keyfilePath);
                    await ApplyPendingImportsAsync(vaultViewModel, openedVaultWindow);
                    await ShowAuthenticationOnboardingAsync(openedVaultWindow);
                });

                // ── Wire AutoFill orchestrator with the unlocked vault ────────────
                try
                {
                    if (Avalonia.Application.Current is PhantomVault.UI.App app && app.Services is { } svc)
                    {
                        var credProvider = new PhantomVault.UI.Desktop.Services.VaultViewModelCredentialProvider(vaultViewModel);
                        svc.GetService<PhantomVault.UI.Services.AutoFill.IAutoFillOrchestrator>()
                           ?.SetVaultContext(credProvider, testManifest!);
                        var vaultCtx = svc.GetService<PhantomVault.UI.Services.AutoFill.VaultAutofillContext>();
                        vaultCtx?.SetUnlocked(testManifest!);

                        // Expose the unlocked vault to the named-pipe server so
                        // `PhantomVault.UI.exe --native-messaging` subprocesses
                        // (spawned by browsers) can answer credential queries.
                        svc.GetService<PhantomVault.UI.Services.AutoFill.INativeHostPipeServer>()
                           ?.SetCredentialProvider(credProvider, testManifest!);
                    }
                }
                catch (Exception wireEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[VaultUnlock] AutoFill wire failed: {wireEx.Message}");
                }

                // When the vault window closes (logout / auto-lock) clear the AutoFill context
                openedVaultWindow.Closed += (_, _) =>
                {
                    try
                    {
                        if (Avalonia.Application.Current is PhantomVault.UI.App appOnClose
                            && appOnClose.Services is { } svcOnClose)
                        {
                            svcOnClose.GetService<PhantomVault.UI.Services.AutoFill.VaultAutofillContext>()
                                      ?.SetLocked();
                            svcOnClose.GetService<PhantomVault.UI.Services.AutoFill.INativeHostPipeServer>()
                                      ?.ClearCredentialProvider();
                        }
                    }
                    catch { /* best effort */ }
                };
                // ─────────────────────────────────────────────────────────────────

                // Zero out the password after use
                password = null;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[VaultUnlock] ERROR: {ex.GetType().Name}: {ex.Message}");
                System.Console.Error.WriteLine(ex.StackTrace);
                Status = "Error during vault unlock";
                await _dialogService.ShowErrorAsync(
                    "Vault Unlock Failed",
                    $"An error occurred while unlocking the vault: {ex.Message}",
                    _ownerWindow);
                CloseAndReturnToWelcome();
            }
            finally
            {
                DeleteExtractedVolumeRoot(extractedVolumeRoot);
                IsBusy = false;
            }
        }

        private void CloseAndReturnToWelcome()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
            // Transfer MainWindow to WelcomePage BEFORE closing the unlock window,
            // otherwise Avalonia's OnMainWindowClose shutdown kills the app.
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    if (window is WelcomePage welcomePage)
                    {
                        desktop.MainWindow = welcomePage;
                        welcomePage.Show();
                        break;
                    }
                }
            }

            _ownerWindow?.Close();
            });
        }

        private static void ValidateUsbTopology(VaultManifest manifest, string bindingRecordPath)
        {
            if (string.IsNullOrWhiteSpace(manifest.RootContainerPath) ||
                string.IsNullOrWhiteSpace(manifest.ContainerPath) ||
                string.IsNullOrWhiteSpace(manifest.ObjectContainerPath))
            {
                throw new InvalidOperationException("This vault is missing the mandatory three-container metadata.");
            }

            if (!File.Exists(manifest.RootContainerPath) ||
                !File.Exists(manifest.ContainerPath) ||
                !File.Exists(manifest.ObjectContainerPath))
            {
                throw new FileNotFoundException("The USB does not contain the full root, vault, and object container layout.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.RecoveryContainerPath) &&
                !File.Exists(manifest.RecoveryContainerPath))
            {
                throw new FileNotFoundException("The USB recovery container is missing from the bound vault layout.");
            }

            if (!File.Exists(bindingRecordPath))
            {
                throw new FileNotFoundException("The USB binding record is missing from the root authority container path.");
            }
        }

        private static string? ResolveMasterVolumePath(string usbPath)
        {
            var candidates = new[]
            {
                Path.Combine(usbPath, "system.bin"),
                Path.Combine(usbPath, ".phantom", "obscura.vol")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string DetermineRuntimeVaultRoot(VaultManifest manifest, string manifestPath)
        {
            if (!string.IsNullOrWhiteSpace(manifest.ContainerPath))
            {
                var containerDirectory = Path.GetDirectoryName(manifest.ContainerPath);
                if (!string.IsNullOrWhiteSpace(containerDirectory))
                {
                    if (string.Equals(Path.GetFileName(containerDirectory), "vaults", StringComparison.OrdinalIgnoreCase))
                    {
                        return Directory.GetParent(containerDirectory)?.FullName ?? containerDirectory;
                    }

                    return containerDirectory;
                }
            }

            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestDirectory))
                throw new InvalidOperationException("Unable to determine the vault runtime root from the manifest path.");

            if (string.Equals(Path.GetFileName(manifestDirectory), "root", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(manifestDirectory)?.FullName ?? manifestDirectory;
            }

            return manifestDirectory;
        }

        private static string? ResolveVaultDatabasePath(string runtimeVaultRoot)
        {
            var candidates = new[]
            {
                Path.Combine(runtimeVaultRoot, "vault.pvault"),
                Path.Combine(runtimeVaultRoot, "vaults", "vault.pvault")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static void ResolveRuntimePaths(VaultManifest manifest, string extractedRoot, string masterVolumePath)
        {
            manifest.MasterVolumePath = masterVolumePath;
            ResolveExtractedRuntimePaths(manifest, extractedRoot);
        }

        private static void ResolveDirectRuntimePaths(VaultManifest manifest, string manifestPath, string rootPath, string vaultsPath)
        {
            var layoutRoot = TryResolveLayoutRoot(manifestPath, rootPath, vaultsPath);
            if (string.IsNullOrWhiteSpace(layoutRoot))
                return;

            ResolveExtractedRuntimePaths(manifest, layoutRoot);
        }

        private async Task RevealGeneratedPasswordBootstrapAsync(
            string? selectedDriveRoot,
            VaultManifest manifest,
            string? password,
            string? keyfilePath)
        {
            if (string.IsNullOrWhiteSpace(selectedDriveRoot))
                return;

            var bootstrapPath = Path.Combine(selectedDriveRoot, ".phantom", "bootstrap", "generated-password.pmeta");
            if (!File.Exists(bootstrapPath))
                return;

            try
            {
                var bootstrapRecord = _usbArtifactProtectionService.ReadEncryptedJson<GeneratedPasswordBootstrapRecord>(
                    bootstrapPath,
                    manifest,
                    password,
                    keyfilePath,
                    "generated-password-bootstrap");

                await _dialogService.ShowInfoAsync(
                    "Generated Password",
                    $"{bootstrapRecord.Prompt}\n\n{bootstrapRecord.Password}",
                    _ownerWindow);

                File.Delete(bootstrapPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultUnlock] Failed to reveal generated password bootstrap: {ex.Message}");
            }
        }

        private async Task ApplyPendingImportsAsync(VaultViewModel vaultViewModel, Window vaultWindow)
        {
            if (!PendingImportStagingService.HasPendingImports())
                return;

            try
            {
                await Task.Delay(400).ConfigureAwait(false);
                var pendingImports = PendingImportStagingService.LoadPendingImports();
                if (pendingImports.Count == 0)
                    return;

                foreach (var credential in pendingImports)
                {
                    vaultViewModel.AddCredentialFromImport(credential);
                }

                PendingImportStagingService.Clear();

                await _dialogService.ShowInfoAsync(
                    "Imported Credentials Added",
                    $"{pendingImports.Count} staged credential(s) were imported into this vault.",
                    vaultWindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultUnlock] Failed to apply pending imports: {ex.Message}");
            }
        }

        private async Task ShowAuthenticationOnboardingAsync(Window vaultWindow)
        {
            try
            {
                var settings = SettingsService.Load();
                if (!settings.PendingPostCreateAuthOnboarding)
                    return;

                await _dialogService.ShowInfoAsync(
                    "Authentication Onboarding",
                    "Next, Phantom Obscura will walk through Windows Hello, passkey, and TOTP setup pages for this new vault.",
                    vaultWindow);

                if (settings.PendingSetupWindowsHello)
                {
                    var helloVm = new WindowsHelloSettingsViewModel();
                    var helloWindow = new WindowsHelloSettingsWindow
                    {
                        DataContext = helloVm
                    };
                    helloVm.SetOwnerWindow(helloWindow);
                    await helloWindow.ShowDialog(vaultWindow);
                }

                if (settings.PendingSetupPasskey)
                {
                    var passkeyVm = new PasskeySettingsViewModel();
                    var passkeyWindow = new PasskeySettingsWindow
                    {
                        DataContext = passkeyVm
                    };
                    passkeyVm.SetOwnerWindow(passkeyWindow);
                    await passkeyWindow.ShowDialog(vaultWindow);
                }

                if (settings.PendingSetupTotp)
                {
                    var totpVm = new TotpSettingsViewModel();
                    var totpWindow = new TotpSettingsWindow
                    {
                        DataContext = totpVm
                    };
                    totpVm.SetOwnerWindow(totpWindow);
                    await totpWindow.ShowDialog(vaultWindow);
                }

                SettingsService.Update(updated =>
                {
                    updated.PendingPostCreateAuthOnboarding = false;
                    updated.PendingSetupWindowsHello = false;
                    updated.PendingSetupPasskey = false;
                    updated.PendingSetupTotp = false;
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Post-create authentication onboarding failed");
            }
        }

        private static void ResolveExtractedRuntimePaths(VaultManifest manifest, string extractedRoot)
        {
            manifest.RootContainerPath = ResolveExtractedPath(extractedRoot, manifest.RootContainerPath);
            manifest.ContainerPath = ResolveExtractedPath(extractedRoot, manifest.ContainerPath) ?? manifest.ContainerPath;
            manifest.ObjectContainerPath = ResolveExtractedPath(extractedRoot, manifest.ObjectContainerPath);
            manifest.RecoveryContainerPath = ResolveExtractedPath(extractedRoot, manifest.RecoveryContainerPath);
            manifest.BindingRecordPath = ResolveExtractedPath(extractedRoot, manifest.BindingRecordPath);
            manifest.RecoveryRecordPath = ResolveExtractedPath(extractedRoot, manifest.RecoveryRecordPath);
            manifest.DecoyDatabasePath = ResolveExtractedPath(extractedRoot, manifest.DecoyDatabasePath);
            PhantomKeyBridgeValidator.ResolveRuntimePaths(manifest, extractedRoot);
        }

        private static string? TryResolveLayoutRoot(string manifestPath, string rootPath, string vaultsPath)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var rootParent = Directory.GetParent(rootPath);
                if (rootParent != null)
                    return rootParent.FullName;
            }

            if (!string.IsNullOrWhiteSpace(vaultsPath))
            {
                var vaultsParent = Directory.GetParent(vaultsPath);
                if (vaultsParent != null)
                    return vaultsParent.FullName;
            }

            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestDirectory))
                return null;

            return string.Equals(Path.GetFileName(manifestDirectory), "root", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetFileName(manifestDirectory), "vaults", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(manifestDirectory)?.FullName
                : manifestDirectory;
        }

        private static string? ResolveExtractedPath(string extractedRoot, string? manifestPathValue)
        {
            if (string.IsNullOrWhiteSpace(manifestPathValue))
                return manifestPathValue;

            return Path.IsPathRooted(manifestPathValue)
                ? manifestPathValue
                : Path.Combine(extractedRoot, manifestPathValue.Replace('/', Path.DirectorySeparatorChar));
        }

        private void ValidateUsbBinding(string usbPath, string bindingRecordPath, VaultManifest manifest, string? password, string? keyfilePath)
        {
            var record = _usbArtifactProtectionService.ReadEncryptedJson<UsbBindingRecord>(
                bindingRecordPath,
                manifest,
                password,
                keyfilePath,
                "usb-binding");

            if (!string.Equals(record.BindingId, manifest.UsbBindingId, StringComparison.Ordinal) ||
                !string.Equals(record.BindingGuid, manifest.UsbBindingGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The USB binding identity does not match the vault manifest.");
            }

            if (!string.IsNullOrWhiteSpace(record.Guuid) &&
                !string.IsNullOrWhiteSpace(manifest.Guuid) &&
                !string.Equals(record.Guuid, manifest.Guuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The bound hardware GUUID does not match the vault manifest.");
            }

            var usbBindingService = new UsbBindingService();
            var currentDeviceId = ComputeCurrentBindingDeviceId(usbBindingService, usbPath, manifest);
            if (!string.IsNullOrEmpty(manifest.DeviceId) &&
                !string.Equals(currentDeviceId, manifest.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(record.DeviceId, manifest.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This vault is not bound to the currently attached USB device.");
            }
        }

        private void ValidateRecoveryArtifacts(VaultManifest manifest, string? password, string? keyfilePath)
        {
            if (string.IsNullOrWhiteSpace(manifest.RecoveryContainerPath))
                return;

            if (!File.Exists(manifest.RecoveryContainerPath))
                throw new FileNotFoundException("The encrypted recovery container is missing from the USB layout.");

            if (string.IsNullOrWhiteSpace(manifest.RecoveryRecordPath))
                throw new InvalidOperationException("The recovery record path is missing from the vault manifest.");

            if (!File.Exists(manifest.RecoveryRecordPath))
                throw new FileNotFoundException("The encrypted recovery record is missing from the USB layout.");

            var recoveryRecord = _usbArtifactProtectionService.ReadEncryptedJson<RecoveryVaultRecord>(
                manifest.RecoveryRecordPath,
                manifest,
                password,
                keyfilePath,
                "recovery-record");

            if (!string.Equals(recoveryRecord.RecoveryContainerPath, manifest.RecoveryContainerPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The recovery record does not match the vault manifest.");
        }

        /// <summary>
        /// Prompts the user for the vault passphrase using a secure password dialog.
        /// </summary>
        private async Task<string?> PromptForPasswordAsync()
        {
            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
            var dialog = new Window
            {
                Title = "Vault Passphrase Required",
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C3B4B"))
            };

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

            var label = new TextBlock
            {
                Text = "Enter your vault passphrase to unlock:",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            // Use TextBox with PasswordChar for secure input
            var passwordBox = new TextBox
            {
                PasswordChar = '●',
                Watermark = "Passphrase",
                Width = 360,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Classes = { "SecureInput" }
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var okButton = new Button
            {
                Content = "Unlock",
                IsDefault = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                IsCancel = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(passwordBox);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, _) => { result = passwordBox.Text; dialog.Close(); };
            cancelButton.Click += (_, _) => { dialog.Close(); };

            if (_ownerWindow != null)
            {
                await dialog.ShowDialog(_ownerWindow);
            }
            else
            {
                dialog.Show();
                await Task.Delay(100); // Wait for dialog to show
            }

            // Clear the password box after reading
            passwordBox.Text = string.Empty;

            return result;
            });
        }

        /// <summary>
        /// Prompts the user for a TOTP code.
        /// </summary>
        private async Task<string?> PromptForTotpAsync()
        {
            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
            var dialog = new Window
            {
                Title = "Two-Factor Authentication",
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C3B4B"))
            };

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

            var label = new TextBlock
            {
                Text = "Enter the 6-digit code from your authenticator app:",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            var codeBox = new TextBox
            {
                Watermark = "000000",
                Width = 120,
                MaxLength = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83"))
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var okButton = new Button
            {
                Content = "Verify",
                IsDefault = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556C83")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                IsCancel = true,
                Width = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A5C6E")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(codeBox);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            string? result = null;
            okButton.Click += (_, _) => { result = codeBox.Text; dialog.Close(); };
            cancelButton.Click += (_, _) => { dialog.Close(); };

            if (_ownerWindow != null)
            {
                await dialog.ShowDialog(_ownerWindow);
            }

            return result;
            });
        }

        /// <summary>
        /// Validates a TOTP code against the stored secret.
        /// </summary>
        private static bool ValidateTotpCode(string totpSecret, string code)
        {
            if (string.IsNullOrEmpty(totpSecret) || string.IsNullOrEmpty(code))
                return false;

            try
            {
                var totpService = new TotpService();
                var expectedCode = totpService.GenerateCode(totpSecret);

                // Use constant-time comparison to prevent timing attacks
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(code.PadLeft(6, '0')),
                    System.Text.Encoding.UTF8.GetBytes(expectedCode.PadLeft(6, '0')));
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeCurrentBindingDeviceId(UsbBindingService usbBindingService, string usbPath, VaultManifest manifest)
        {
            string currentDeviceId;
            var salt = string.IsNullOrWhiteSpace(manifest.SaltBase64)
                ? null
                : Convert.FromBase64String(manifest.SaltBase64);

            bool useHighAssuranceBinding = !usbPath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase) &&
                (manifest.RequiresHardwareToken || usbBindingService.HasHiddenDeviceId(usbPath));

            if (useHighAssuranceBinding && salt is { Length: > 0 })
            {
                currentDeviceId = usbBindingService.ComputeHighAssuranceDeviceId(usbPath, salt);
            }
            else
            {
                currentDeviceId = usbBindingService.ComputeDeviceId(usbPath);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Guuid))
            {
                var currentGuuid = DetectSystemGuuid();
                if (!string.IsNullOrWhiteSpace(currentGuuid))
                {
                    if (!string.Equals(currentGuuid, manifest.Guuid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("This vault is bound to different hardware.");

                    string combined = $"{currentDeviceId}|GUUID:{currentGuuid}";
                    byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
                    currentDeviceId = Convert.ToHexString(hash);
                }
                else
                {
                    // GUUID detection failed but vault was created with GUUID binding.
                    // Fall back to device-only binding to allow recovery.
                    // Log warning so user is aware binding was downgraded.
                    Log.Warning("GUUID binding was set during vault creation but could not be detected on this system. Falling back to device-only binding for vault access.");
                }
            }

            return currentDeviceId;
        }

        [SupportedOSPlatform("windows")]
        private static string? DetectSystemGuuid()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var uuid = mo["UUID"]?.ToString();
                    if (!string.IsNullOrEmpty(uuid) &&
                        uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" &&
                        uuid != "00000000-0000-0000-0000-000000000000")
                    {
                        return uuid;
                    }
                }
            }
            catch
            {
                // Treat detection failure as unavailable and let the caller decide whether that is fatal.
            }

            return null;
        }

        private static void DeleteExtractedVolumeRoot(string? extractedVolumeRoot)
        {
            if (string.IsNullOrWhiteSpace(extractedVolumeRoot) || !Directory.Exists(extractedVolumeRoot))
                return;

            try
            {
                Directory.Delete(extractedVolumeRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
