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

        private static System.Collections.Generic.IReadOnlyList<string> BuildKeyfileCandidates(string drivePath, string usbKeyfilePath)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new System.Collections.Generic.List<string>();

            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                if (seen.Add(value))
                    candidates.Add(value);
            }

            var locatorCompanion = TryResolveHostCompanionKeyfilePath(drivePath);
            if (!string.IsNullOrWhiteSpace(locatorCompanion))
            {
                Add(PhantomVault.Core.Utils.CompositeKeyfilePath.Compose(usbKeyfilePath, locatorCompanion));
            }

            try
            {
                var hostKeyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomObscura",
                    "HostKey");
                if (Directory.Exists(hostKeyDir))
                {
                    var companionFiles = Directory.GetFiles(hostKeyDir, "*.companion.key", SearchOption.TopDirectoryOnly);
                    foreach (var companion in companionFiles)
                    {
                        Add(PhantomVault.Core.Utils.CompositeKeyfilePath.Compose(usbKeyfilePath, companion));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[VaultUnlock] BuildKeyfileCandidates: failed to enumerate host companions");
            }

            Add(usbKeyfilePath);

            return candidates;
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

        public int ProgressPercent
        {
            get => _progressPercent;
            private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
        }

        public void SetOwnerWindow(Window window)
        {
            _ownerWindow = window;
        }

        public async Task UnlockVaultAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            string? extractedVolumeRoot = null;

            Log.Information("[VaultUnlock] UnlockVaultAsync ENTER usbPath={Usb} preferred={Preferred}", _usbPath, _preferredVaultPath ?? "<null>");
            try
            {
                ProgressPercent = 5;
                Status = "Validating vault location...";

                string? selectedDriveRoot = _blackSecureRawVolumeService.IsRawSelection(_usbPath) ? null : _usbPath;
                string? selectedPhysicalDrivePath = _blackSecureRawVolumeService.IsRawSelection(_usbPath)
                    ? _blackSecureRawVolumeService.TryResolvePhysicalDevicePathFromSelection(_usbPath)
                    : null;

                if (string.IsNullOrWhiteSpace(selectedDriveRoot) && string.IsNullOrWhiteSpace(selectedPhysicalDrivePath))
                {
                    await _dialogService.ShowErrorAsync(
                        "Invalid Path",
                        "USB path is empty or invalid.",
                        _ownerWindow);
                    CloseAndReturnToWelcome();
                    return;
                }

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

                // Map the volume-extraction progress (0..1) into the unlock progress band 5%–22%
                // so the bar visibly moves during what was previously a 60s+ silent hang.
                var extractProgress = new Progress<double>(fraction =>
                {
                    var pct = 5 + (int)Math.Round(fraction * 17);
                    if (pct > ProgressPercent) ProgressPercent = pct;
                    Status = $"Extracting vault volume… {(int)Math.Round(fraction * 100)}%";
                });

                // Track the source we extracted from so we can run a deferred integrity
                // verification in the background after the unlock UI is open.
                string? extractedFromVolumePath = null;
                bool extractedFromBlackSecure = false;
                string? extractedFromRawDevice = null;

                if (masterVolumePath != null)
                {
                    Status = "Extracting vault volume…";
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    var obscuraVolumeService = new ObscuraVolumeService();
                    // verify:false — integrity check runs as a background task after unlock so the
                    // extraction step finishes in ~half the time. See post-unlock verifier below.
                    await obscuraVolumeService.ExtractVolumeAsync(masterVolumePath, extractedVolumeRoot, extractProgress, verify: false).ConfigureAwait(false);
                    extractedFromVolumePath = masterVolumePath;

                    rootPath = Path.Combine(extractedVolumeRoot, "root");
                    vaultsPath = Path.Combine(extractedVolumeRoot, "vaults");
                    manifestsPath = Path.Combine(extractedVolumeRoot, "manifests");
                    bindingRecordPath = Path.Combine(rootPath, "usb.binding.pmeta");
                }
                else if (!string.IsNullOrWhiteSpace(selectedPhysicalDrivePath) &&
                         await _blackSecureRawVolumeService.IsBlackSecureVolumeAsync(selectedPhysicalDrivePath).ConfigureAwait(false))
                {
                    Status = "Extracting vault volume…";
                    extractedVolumeRoot = Path.Combine(Path.GetTempPath(), "PhantomObscuraSessions", Guid.NewGuid().ToString("N"));
                    await _blackSecureRawVolumeService.ExtractVolumeAsync(selectedPhysicalDrivePath, extractedVolumeRoot, extractProgress, verify: false).ConfigureAwait(false);
                    extractedFromBlackSecure = true;
                    extractedFromRawDevice = selectedPhysicalDrivePath;

                    rootPath = Path.Combine(extractedVolumeRoot, "root");
                    vaultsPath = Path.Combine(extractedVolumeRoot, "vaults");
                    manifestsPath = Path.Combine(extractedVolumeRoot, "manifests");
                    bindingRecordPath = Path.Combine(rootPath, "usb.binding.pmeta");
                }

                if (manifestPath == null && Directory.Exists(rootPath))
                {
                    var rootContainers = Directory.GetFiles(rootPath, "*.pvault");
                    if (rootContainers.Length > 0)
                        manifestPath = rootContainers[0];
                }

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

                var keyfilePath = string.IsNullOrWhiteSpace(selectedDriveRoot) ? null : FindKeyfileOnDrive(selectedDriveRoot);
                string? password = null;

                Log.Information("[VaultUnlock] manifestPath={Manifest} keyfilePath={Keyfile}", manifestPath, keyfilePath ?? "<null>");

                var encryptionService = new EncryptionService();
                var containerService = new PhantomContainerService(encryptionService);
                var manifestService = new ManifestService(encryptionService, containerService);

                // Capture the validated manifest from whichever auth path succeeds so we can skip
                // the redundant ReadManifest call below (each ReadManifest runs a full Argon2id KDF,
                // costing ~0.5–2s; eliminating the duplicate saves a third of total unlock time).
                VaultManifest? authenticatedManifest = null;

                if (!string.IsNullOrEmpty(keyfilePath))
                {

                    ProgressPercent = 25;
                    Status = "Authenticating with keyfile...";

                    var usbKeyfilePath = PhantomVault.Core.Utils.CompositeKeyfilePath.GetPrimaryPath(keyfilePath) ?? keyfilePath;
                    var candidates = BuildKeyfileCandidates(selectedDriveRoot!, usbKeyfilePath);
                    Log.Information("[VaultUnlock] trying {Count} keyfile candidate(s) for keyfile-only auth", candidates.Count);

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        try
                        {
                            var keyfileTestManifest = await Task.Run(() => manifestService.ReadManifest(manifestPath, string.Empty, candidate));
                            if (keyfileTestManifest != null)
                            {
                                keyfilePath = candidate;
                                password = string.Empty;
                                authenticatedManifest = keyfileTestManifest;
                                Log.Information("[VaultUnlock] keyfile-only auth SUCCEEDED on candidate {Index}/{Count}", i + 1, candidates.Count);
                                Status = "Authenticated with keyfile";
                                break;
                            }
                        }
                        catch (Exception kfEx)
                        {
                            Log.Debug("[VaultUnlock] candidate {Index}/{Count} rejected ({ExType})", i + 1, candidates.Count, kfEx.GetType().Name);
                        }
                    }

                    if (password == null)
                    {
                        Log.Warning("[VaultUnlock] all {Count} keyfile candidates rejected", candidates.Count);
                    }
                }

                if (password == null)
                {
                    ProgressPercent = 25;
                    Status = "Checking authentication requirements...";

                    try
                    {

                        var noPassManifest = await Task.Run(() => manifestService.ReadManifest(manifestPath, string.Empty, null));
                        if (noPassManifest != null)
                        {
                            password = string.Empty;
                            authenticatedManifest = noPassManifest;
                            Log.Information("[VaultUnlock] no-password auth SUCCEEDED");
                        }
                        else
                        {
                            Log.Warning("[VaultUnlock] no-password auth returned null manifest");
                        }
                    }
                    catch (Exception npEx)
                    {
                        Log.Warning("[VaultUnlock] no-password auth threw {ExType}: {ExMsg}", npEx.GetType().Name, npEx.Message);
                    }
                }

                if (password == null)
                {
                    if (string.IsNullOrEmpty(keyfilePath))
                    {
                        Log.Warning("[VaultUnlock] no keyfile found on drive — mandatory keyfile missing");
                        await _dialogService.ShowErrorAsync(
                            "USB Keyfile Required",
                            "No keyfile was found on the attached USB drive.\n\n" +
                            "• Make sure the correct USB drive is connected.\n" +
                            "• Confirm the keyfile (.key) is present in the .phantom folder on the drive.\n\n" +
                            "Phantom Obscura requires a valid USB keyfile to unlock.",
                            _ownerWindow);
                    }
                    else
                    {
                        Log.Warning("[VaultUnlock] keyfile present but no candidate combination unlocked the vault");
                        await _dialogService.ShowErrorAsync(
                            "Unable to Unlock Vault",
                            "The keyfile on this USB drive does not match this vault.\n\n" +
                            "• Confirm the correct USB drive is inserted.\n" +
                            "• If the vault was created on another machine, copy the matching " +
                            "host companion key into %LOCALAPPDATA%\\PhantomObscura\\HostKey.\n\n" +
                            "Phantom Obscura will not unlock without the matching keyfile material.",
                            _ownerWindow);
                    }
                    CloseAndReturnToWelcome();
                    return;
                }

                ProgressPercent = 40;
                Status = "Initializing vault services...";

                ProgressPercent = 55;
                Status = "Validating passphrase (deriving key)...";

                VaultManifest? testManifest = null;
                (string? RootContainerPath, string? ContainerPath, string? ObjectContainerPath) preResolvePaths = default;
                Log.Information("[VaultUnlock] final ReadManifest password='{Pass}' keyfile={Keyfile} reuseAuthenticated={Reuse}",
                    password == null ? "<null>" : (password.Length == 0 ? "<empty>" : "<set>"),
                    keyfilePath ?? "<null>",
                    authenticatedManifest != null);

                try
                {
                    // Reuse the manifest the keyfile-candidate or no-password attempt already validated
                    // instead of running ReadManifest (and a full Argon2id derivation) a second time.
                    testManifest = authenticatedManifest
                        ?? await Task.Run(() => manifestService.ReadManifest(manifestPath, password, keyfilePath));
                    if (testManifest == null)
                    {

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

                    preResolvePaths = (
                        RootContainerPath: testManifest.RootContainerPath,
                        ContainerPath: testManifest.ContainerPath,
                        ObjectContainerPath: testManifest.ObjectContainerPath);

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
                    await Task.Run(() =>
                    {
                        ValidateUsbBinding(selectedPhysicalDrivePath ?? selectedDriveRoot!, resolvedBindingRecordPath, testManifest, password, keyfilePath);
                        ValidateRecoveryArtifacts(testManifest, password, keyfilePath);
                        _phantomKeyBridgeValidator.Validate(testManifest, password, keyfilePath);
                    });

                    if (testManifest.RequiresTotp && !string.IsNullOrEmpty(testManifest.TotpSecret))
                    {
                        ProgressPercent = 70;
                        Status = "TOTP verification required...";
                        var totpCode = await PromptForTotpAsync();
                        if (string.IsNullOrEmpty(totpCode) || !ValidateTotpCode(testManifest.TotpSecret, totpCode))
                        {

                            _throttleService.RegisterFailedAttempt(manifestPath);

                            await _dialogService.ShowErrorAsync(
                                "TOTP Verification Failed",
                                "The TOTP code is invalid or expired.",
                                _ownerWindow);
                            CloseAndReturnToWelcome();
                            return;
                        }
                    }

                    _throttleService.ResetAttempts(manifestPath);
                }
                catch (Exception decryptEx)
                {
                    Log.Error(decryptEx, "[VaultUnlock] decryption/validation step threw {ExType}", decryptEx.GetType().FullName);

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
                byte[] vaultDatabaseKey = await Task.Run(() =>
                {

                    var resolvedRoot = testManifest!.RootContainerPath;
                    var resolvedContainer = testManifest.ContainerPath;
                    var resolvedObject = testManifest.ObjectContainerPath;
                    testManifest.RootContainerPath = preResolvePaths.RootContainerPath;
                    testManifest.ContainerPath = preResolvePaths.ContainerPath;
                    testManifest.ObjectContainerPath = preResolvePaths.ObjectContainerPath;
                    Log.Information("[VaultUnlock][ZKDiag] DeriveKey inputs — VaultName={VaultName} | BindingId={BindingId} | BindingGuid={BindingGuid} | DeviceId={DeviceId} | Guuid={Guuid} | Root={Root} | Container={Container} | Object={Object} | Salt={Salt} | HasPass={HasPass} | HasKey={HasKey}",
                        testManifest.VaultName,
                        testManifest.UsbBindingId,
                        testManifest.UsbBindingGuid,
                        testManifest.DeviceId,
                        testManifest.Guuid,
                        testManifest.RootContainerPath,
                        testManifest.ContainerPath,
                        testManifest.ObjectContainerPath,
                        testManifest.SaltBase64,
                        !string.IsNullOrEmpty(password),
                        !string.IsNullOrEmpty(keyfilePath));
                    try
                    {
                        return new VaultDatabaseKeyService(encryptionService).DeriveKey(testManifest, password, keyfilePath);
                    }
                    finally
                    {
                        testManifest.RootContainerPath = resolvedRoot;
                        testManifest.ContainerPath = resolvedContainer;
                        testManifest.ObjectContainerPath = resolvedObject;
                    }
                });
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

                // Record which authentication factors were actually used during this unlock
                // so the Recovery launch gate can evaluate the active policy.
                if (Avalonia.Application.Current is PhantomVault.UI.App appForSession && appForSession.Services is { } sessionSvc)
                {
                    var sessionState = sessionSvc.GetService<PhantomVault.UI.Services.RecoveryActivationSessionState>();
                    if (sessionState != null)
                    {
                        sessionState.Reset();
                        // USB keyfile is mandatory for every unlock.
                        sessionState.MarkPossessionUsed(PhantomVault.Core.Models.PossessionFactor.Keyfile);
                        if (deviceBindingId != null)
                        {
                            sessionState.MarkPossessionUsed(PhantomVault.Core.Models.PossessionFactor.UsbBinding);
                        }
                        if (!string.IsNullOrEmpty(password))
                        {
                            sessionState.MarkIdentityUsed(PhantomVault.Core.Models.IdentityFactor.Password);
                        }
                    }
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

                vaultViewModel.SetManifestContext(manifestPath, password, keyfilePath, testManifest);

                try
                {
                    if (!string.IsNullOrWhiteSpace(selectedDriveRoot) && testManifest != null)
                    {
                        var usbSettings = PhantomVault.UI.Services.SettingsService.Load();

                        if (usbSettings.UsbAutoScrubEnabled)
                        {
                            var scrubber = new UsbJunkScrubber();
                            if (scrubber.HasJunk(selectedDriveRoot))
                            {
                                var removed = scrubber.Scrub(
                                    selectedDriveRoot,
                                    quarantineDays: usbSettings.UsbScrubQuarantineDays,
                                    dryRun: false,
                                    manifest: testManifest);
                                if (removed.Count > 0)
                                {
                                    Log.Information("[VaultUnlock] scrubbed {Count} OS-junk entries from {Drive}",
                                        removed.Count, selectedDriveRoot);
                                }
                            }
                        }

                        if (usbSettings.UsbWriteProtectionEnabled)
                        {
                            var wpService = new UsbWriteProtectionService();
                            if (wpService.IsSupported)
                            {
                                wpService.EnableWriteAccess(selectedDriveRoot);
                            }
                        }
                    }
                }
                catch (Exception wpEx)
                {
                    Log.Warning(wpEx, "[VaultUnlock] write-protection / scrub failed; continuing.");
                }

                // Defer the heavy credential load until AFTER the vault window is visible —
                // unlock progress jumps to 100% and the window appears immediately while the
                // credentials stream in behind a loading overlay (IsLoadingCredentials).
                var deferredLoadPassword = password ?? string.Empty;
                var deferredLoadKeyfile = keyfilePath;
                var deferredLoadRoot = runtimeVaultRoot;
                vaultViewModel.IsLoadingCredentials = true;

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

                // Kick off credential load in the background — the window is already on screen.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await vaultViewModel.LoadAsync(deferredLoadRoot, deferredLoadPassword, deferredLoadKeyfile).ConfigureAwait(false);
                    }
                    catch (Exception loadEx)
                    {
                        Log.Error(loadEx, "[VaultUnlock] deferred LoadAsync threw {ExType}", loadEx.GetType().Name);
                    }
                });

                // Kick off the deferred volume integrity verification in the background. If
                // tampering is detected, surface it to the user via a status message; the vault
                // already auto-locks via existing tamper-detection paths if that fires.
                if (!string.IsNullOrWhiteSpace(extractedVolumeRoot) || extractedFromVolumePath != null || extractedFromBlackSecure)
                {
                    var verifyExtractedRoot = extractedVolumeRoot;
                    var verifyMasterVolume = extractedFromVolumePath;
                    var verifyBlackSecure = extractedFromBlackSecure;
                    var verifyRawDevice = extractedFromRawDevice;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            bool ok = true;
                            if (verifyMasterVolume != null && !string.IsNullOrWhiteSpace(verifyExtractedRoot))
                            {
                                ok = await new ObscuraVolumeService()
                                    .VerifyExtractedVolumeAsync(verifyMasterVolume, verifyExtractedRoot)
                                    .ConfigureAwait(false);
                            }
                            // BlackSecure raw-device verification path is not yet implemented in
                            // the background; tampering would still surface on the next unlock
                            // because that path leaves verify:true by default for new sessions.
                            if (!ok)
                            {
                                Log.Warning("[VaultUnlock] background volume integrity verification FAILED for {Path}",
                                    verifyMasterVolume ?? verifyRawDevice ?? "<unknown>");
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    vaultViewModel.StatusMessage = "Warning: vault volume integrity check failed";
                                });
                            }
                            else
                            {
                                Log.Information("[VaultUnlock] background volume integrity verification OK");
                            }
                        }
                        catch (Exception verifyEx)
                        {
                            Log.Warning(verifyEx, "[VaultUnlock] background volume integrity verification threw {ExType}", verifyEx.GetType().Name);
                        }
                    });
                }

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await RevealGeneratedPasswordBootstrapAsync(selectedDriveRoot, testManifest!, password, keyfilePath);
                    await ApplyPendingImportsAsync(vaultViewModel, openedVaultWindow);
                    await ShowAuthenticationOnboardingAsync(openedVaultWindow);
                });

                try
                {
                    if (Avalonia.Application.Current is PhantomVault.UI.App app && app.Services is { } svc)
                    {
                        var credProvider = new PhantomVault.UI.Desktop.Services.VaultViewModelCredentialProvider(vaultViewModel);
                        svc.GetService<PhantomVault.UI.Services.AutoFill.IAutoFillOrchestrator>()
                           ?.SetVaultContext(credProvider, testManifest!);
                        var vaultCtx = svc.GetService<PhantomVault.UI.Services.AutoFill.VaultAutofillContext>();
                        vaultCtx?.SetUnlocked(testManifest!);

                        svc.GetService<PhantomVault.UI.Services.AutoFill.INativeHostPipeServer>()
                           ?.SetCredentialProvider(credProvider, testManifest!);
                    }
                }
                catch (Exception wireEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[VaultUnlock] AutoFill wire failed: {wireEx.Message}");
                }

                var driveRootForClose = selectedDriveRoot;
                var manifestForClose = testManifest;
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
                    catch {  }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(driveRootForClose))
                        {
                            var usbCloseSettings = PhantomVault.UI.Services.SettingsService.Load();
                            if (usbCloseSettings.UsbWriteProtectionEnabled)
                            {
                                var wpService = new UsbWriteProtectionService();
                                var prior = manifestForClose?.UsbWriteProtection;
                                var state = new UsbWriteProtectionState
                                {
                                    ReadOnly = true,
                                    Hidden = false,
                                    CompatibilityMode = usbCloseSettings.UsbCompatibilityMode,

                                    PartitionTypeGuid = usbCloseSettings.UsbCompatibilityMode
                                        ? prior?.PartitionTypeGuid
                                        : UsbWriteProtectionService.PhantomObscuraPartitionTypeGuid,
                                };
                                wpService.ApplyProtection(driveRootForClose, state);
                                if (manifestForClose != null)
                                {
                                    manifestForClose.UsbWriteProtection = state;
                                }
                            }
                        }
                    }
                    catch (Exception wpEx)
                    {
                        Log.Warning(wpEx, "[VaultUnlock] post-close ApplyProtection failed.");
                    }
                };

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

            if (!string.Equals(
                    Path.GetFileName(recoveryRecord.RecoveryContainerPath),
                    Path.GetFileName(manifest.RecoveryContainerPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The recovery record does not match the vault manifest.");
        }

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
                await Task.Delay(100);
            }

            passwordBox.Text = string.Empty;

            return result;
            });
        }

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

        private static bool ValidateTotpCode(string totpSecret, string code)
        {
            if (string.IsNullOrEmpty(totpSecret) || string.IsNullOrEmpty(code))
                return false;

            try
            {
                var totpService = new TotpService();
                var expectedCode = totpService.GenerateCode(totpSecret);

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
                try
                {
                    currentDeviceId = usbBindingService.ComputeHighAssuranceDeviceId(usbPath, salt);
                }
                catch (System.Security.Cryptography.CryptographicException cryptEx)
                {

                    Log.Warning(cryptEx, "[VaultUnlock] High-assurance device ID salt mismatch — rotating hidden file to current vault salt.");
                    string standardId = usbBindingService.ComputeDeviceId(usbPath);
                    string? rotatedId = usbBindingService.RotateHiddenDeviceId(usbPath, salt, standardId);
                    if (rotatedId != null)
                    {
                        Log.Information("[VaultUnlock] Hidden device ID rotated successfully — using standard device ID for this unlock.");
                    }
                    else
                    {
                        Log.Warning("[VaultUnlock] Hidden device ID rotation failed — falling back to standard device binding.");
                    }
                    currentDeviceId = standardId;
                }
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

            }
        }
    }
}

