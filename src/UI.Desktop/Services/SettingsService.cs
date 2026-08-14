using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace PhantomVault.UI.Services
{

    public sealed class AutoFillAppPermission
    {
        public string AppName { get; set; } = string.Empty;
        public bool Allowed { get; set; } = true;
    }

    public sealed class UserSettings
    {
        public bool PrivacyModeEnabled { get; set; }
        public bool RedactDiagnosticLogs { get; set; } = true;
        public bool EnableDebugLogging { get; set; } = false;

        public bool UpdateAutoCheckEnabled { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool GlobalHotkeyEnabled { get; set; } = false;
        public bool SecureTrashEnabled { get; set; } = true;
        public bool SecureTrashAutoPurge { get; set; } = true;
        public int SecureTrashRetentionDays { get; set; } = 30;
        public int SecureTrashWipePasses { get; set; } = 3;
        public bool SecureTrashCreateSnapshotBeforePurge { get; set; } = true;
        public bool SecureTrashPromptBeforeDeletion { get; set; } = true;
        public bool SecureTrashAutoEmptyOnClose { get; set; } = false;
        public int SecureTrashErasureMethod { get; set; } = 2;
        public bool SecureTrashDuplicateDetectionEnabled { get; set; } = false;
        public bool SecureTrashAutoDeleteDuplicates { get; set; } = false;
        public int SecureTrashDuplicateScanFrequency { get; set; } = 3;
        public bool BackupAutomationEnabled { get; set; } = false;
        public int BackupFrequencyMode { get; set; } = 0;
        public string BackupLocation { get; set; } = string.Empty;
        public bool BackupUseEncryption { get; set; } = true;
        public int BackupRetentionCount { get; set; } = 2;
        public DateTimeOffset? LastAutomatedBackupUtc { get; set; }
        public double RenderScale { get; set; } = 1.0;

        public List<AutoFillAppPermission> AutoFillAppPermissions { get; set; } = new();

        public string AppFontFamily { get; set; } = "Segoe UI";
        public double AppFontSize { get; set; } = 14.0;
        public string AccentColorHex { get; set; } = "#2B4A7A";

        public int LanguageIndex { get; set; } = 0;

        public bool ShowEntryIcons { get; set; } = true;
        public bool ShowCategoryColors { get; set; } = true;
        public bool UseColouredCategoryBlur { get; set; } = false;
        public int AccessibilityFontSize { get; set; } = 1;
        public int AccessibilityFontFamily { get; set; } = 0;
        public bool EnableKeyboardShortcuts { get; set; } = true;
        public bool FocusSearchOnOpen { get; set; } = true;
        public bool EnableScreenReader { get; set; } = false;
        public bool LargeTooltips { get; set; } = false;
        public bool IsDarkTheme { get; set; } = true;
        public int ThemeSkin { get; set; } = 0;
        public bool EnableHighContrast { get; set; } = false;
        public bool ReduceAnimations { get; set; } = false;
        public bool ReduceTransparency { get; set; } = false;
        public bool UseFlatButtons { get; set; } = false;

        /// <summary>
        /// When true, liquid-glass button borders are replaced with a standard flat 1px
        /// border (the glass fill stays). Independent of UseFlatButtons — that one swaps
        /// the entire glass treatment for a solid accent rectangle. Together they cascade
        /// (UseFlatButtons wins when both are on).
        /// </summary>
        public bool UseFlatButtonBorders { get; set; } = false;

        /// <summary>
        /// When true, manifest writes use the reduced Argon2id parameter set
        /// (KdfParams.Fast — 64 MiB / 3 iterations) for faster unlock. Existing
        /// manifests stay at their stored parameters until the user runs the
        /// "Re-key for Fast Unlock" action in Security settings.
        /// </summary>
        public bool UseFastUnlock { get; set; } = false;

        public bool ShowCategoryColorBarOnly { get; set; } = false;

        public string SelectedThemeId { get; set; } = "GiblexGlassNavy";

        public int ClipboardClearTime { get; set; } = 1;

        public bool EnablePinLock { get; set; } = false;

        public bool UsePinLockForAutoLock { get; set; } = false;

        public string? PinSaltBase64 { get; set; }

        public string? PinHashBase64 { get; set; }

        public int PinPbkdf2Iterations { get; set; } = 150_000;

        public bool AutoCopyTotpWithPassword { get; set; } = false;

        public bool EnableScreenshotProtection { get; set; } = true;

        public bool RequireHardwareToken { get; set; } = false;

        public bool RequireKeyfile { get; set; } = false;

        public int IdleTimeoutMinutes { get; set; } = 15;

        public bool AutoLockOnMinimize { get; set; } = false;

        public bool AutoLockOnScreenLock { get; set; } = true;

        public bool ClearClipboardOnLock { get; set; } = true;

        public bool RequireUnlockToShow { get; set; } = false;

        public int? MaxFailedUnlockAttempts { get; set; } = 10;

        public int SessionTimeoutMinutes { get; set; } = 30;

        public bool BlockRemoteDebugging { get; set; } = true;

        public bool EnableDecoyVault { get; set; } = false;

        public int DecoyCredentialCount { get; set; } = 20;

        public bool DecoyReadOnlyMode { get; set; } = true;

        public bool DecoyLogActivations { get; set; } = true;

        public double? MainWindowX { get; set; }

        public double? MainWindowY { get; set; }

        public double? MainWindowWidth { get; set; }

        public double? MainWindowHeight { get; set; }

        public string? MainWindowState { get; set; }

        public bool PreferGridView { get; set; } = false;

        public int DefaultPasswordLength { get; set; } = 16;

        public bool PasswordGeneratorIncludeUppercase { get; set; } = true;
        public bool PasswordGeneratorIncludeLowercase { get; set; } = true;
        public bool PasswordGeneratorIncludeNumbers { get; set; } = true;
        public bool PasswordGeneratorIncludeSymbols { get; set; } = true;

        public bool DashboardEnabled { get; set; } = true;

        public string? LastIconLibraryPath { get; set; }

        public string? DefaultCategoryColor { get; set; }

        public string? LastActiveCategory { get; set; }

        public string? LastIconPack { get; set; }

        public string? IconDisplaySize { get; set; } = "Medium";

        public string PreferredEncryptionProfile { get; set; } = "Advanced";

        public string DefaultVaultProtectionTier { get; set; } = "StealthSecure";

        public bool DefaultRequireHardwareToken { get; set; } = false;

        public bool DefaultUseTotp { get; set; } = false;

        public bool PendingPostCreateAuthOnboarding { get; set; } = false;

        public bool PendingSetupWindowsHello { get; set; } = false;
        public bool PendingSetupPasskey { get; set; } = false;
        public bool PendingSetupTotp { get; set; } = false;

        public bool DefaultUsePasskey { get; set; } = false;

        public string? VaultUnlockPreference { get; set; }

        public string? LastAuthenticationMethod { get; set; }

        public List<string> KnownLocalVaultPaths { get; set; } = new();

        /// <summary>
        /// Shallow copy with the two collection members duplicated rather than shared.
        /// Every other member is a value type or an immutable string, so this is a full
        /// logical copy. Used by SettingsService.Load(), which caches one instance and
        /// must still hand each caller its own object — callers mutate the result and
        /// only some of them save it.
        /// </summary>
        internal UserSettings CreateCopy()
        {
            var copy = (UserSettings)MemberwiseClone();
            copy.AutoFillAppPermissions = AutoFillAppPermissions
                .Select(p => new AutoFillAppPermission { AppName = p.AppName, Allowed = p.Allowed })
                .ToList();
            copy.KnownLocalVaultPaths = new List<string>(KnownLocalVaultPaths);
            return copy;
        }

        public bool EnableAutoFill { get; set; } = false;

        public bool AutoFillInjectUsername { get; set; } = true;

        public bool AutoFillInjectPassword { get; set; } = true;

        public string AutoFillDomainWhitelist { get; set; } = string.Empty;

        public bool AutoFillAutoSubmit { get; set; } = false;

        public bool AutoFillShowIcon { get; set; } = true;

        public bool AutoFillDesktopApps { get; set; } = false;

        public bool AutoFillModeEnabled { get; set; } = false;

        // Privileged helper (broker) install consent — remembered across launches
        // so the "Enable privileged helper" dialog only ever fires on the very
        // first request. `AutoInstall = true` means "user consented, install
        // silently from now on"; `Declined = true` means "user said no, don't
        // ever ask again." Both are toggleable from Settings → Security.
        public bool PrivilegedBrokerAutoInstall { get; set; } = false;
        public bool PrivilegedBrokerDeclined { get; set; } = false;

        public bool AutoFillAutoInputTotp { get; set; } = true;

        public bool AutoFillShowNewEntryOnNoMatch { get; set; } = true;

        public int AutoFillTotpPollDelayMs { get; set; } = 1500;

        public int AutoFillTotpPollTimeoutMs { get; set; } = 8000;

        public bool UsbWriteProtectionEnabled { get; set; } = true;

        public bool UsbAutoScrubEnabled { get; set; } = true;

        public int UsbScrubQuarantineDays { get; set; } = 7;

        public bool UsbScrubPromptOnFirstFind { get; set; } = true;

        public bool UsbCompatibilityMode { get; set; } = true;

        public bool SyncEnabled { get; set; } = true;

        public bool SyncTheme { get; set; } = true;

        /// <summary>
        /// When enabled (and <see cref="SyncEnabled"/> is on), TOTP secrets are shared
        /// with Phantom Attestor through the per-vault sync file on the USB device.
        /// This is the only sync channel that moves secret material, so it is gated by
        /// its own opt-out and never runs when cross-app sync is disabled.
        /// </summary>
        public bool SyncTotp { get; set; } = true;

        /// <summary>
        /// When enabled, non-secret passkey identity metadata (label, relying-party id,
        /// credential id, public key) is shared with Phantom Attestor for cross-app
        /// awareness. Private key material is never exported — passkeys stay device-bound.
        /// </summary>
        public bool SyncPasskeys { get; set; } = false;

        /// <summary>
        /// When enabled, the PhantomKey bridge continuity/policy documents (non-secret,
        /// per <see cref="PhantomVault.Core.Models.PhantomKeyBridgeContract"/>) are shared
        /// with Phantom Attestor. Honours the bridge policy's PrivateMaterialExportAllowed
        /// flag and never exports raw key material.
        /// </summary>
        public bool SyncPhantomKey { get; set; } = false;

        public DateTimeOffset? LastSyncTime { get; set; }

        public TimeSpan? GetClipboardClearDelay()
        {
            return ClipboardClearTime switch
            {
                0 => TimeSpan.FromSeconds(30),
                1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(2),
                3 => TimeSpan.FromMinutes(5),
                4 => null,
                _ => TimeSpan.FromMinutes(1)
            };
        }
    }

    public sealed record SecuritySettingsSnapshot(
        bool EnablePinLock,
        bool UsePinLockForAutoLock,
        bool AutoCopyTotpWithPassword,
        bool RequireHardwareToken,
        bool RequireKeyfile,
        int IdleTimeoutMinutes,
        int ClipboardClearTime,
        bool EnableScreenshotProtection,
        bool EnableDecoyVault,
        int DecoyCredentialCount,
        bool DecoyReadOnlyMode,
        bool DecoyLogActivations);

    public sealed record AdvancedSettingsSnapshot(
        bool EnableDebugLogging,
        bool PrivacyModeEnabled,
        bool RedactDiagnosticLogs,
        bool BlockRemoteDebugging,
        int SessionTimeoutMinutes,
        bool AutoLockOnMinimize,
        bool AutoLockOnScreenLock,
        bool ClearClipboardOnLock,
        bool RequireUnlockToShow,
        int? MaxFailedUnlockAttempts);

    public sealed record VaultSettingsSnapshot(
        bool PrivacyModeEnabled,
        bool RedactDiagnosticLogs,
        bool SecureTrashEnabled,
        bool SecureTrashAutoPurge,
        int SecureTrashRetentionDays,
        int SecureTrashWipePasses,
        int ClipboardClearTime,
        bool IsDarkTheme,
        bool PreferGridView,
        bool DashboardEnabled,
        bool EnableAutoFill,
        int DefaultPasswordLength,
        bool PasswordGeneratorIncludeUppercase,
        bool PasswordGeneratorIncludeLowercase,
        bool PasswordGeneratorIncludeNumbers,
        bool PasswordGeneratorIncludeSymbols,
        bool EnableDebugLogging);

    public sealed record UserSettingsChangedEventArgs(UserSettings Settings);

    public static class SettingsService
    {
        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault");
        private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

        public static event EventHandler<UserSettingsChangedEventArgs>? SettingsChanged;

        public static int NormalizeClipboardClearTimeIndex(int index) => Math.Clamp(index, 0, 4);

        public static bool IsClipboardAutoClearEnabled(int index) => NormalizeClipboardClearTimeIndex(index) < 4;

        public static int GetAutoLockMinutesFromSelection(int index) => index switch
        {
            <= 0 => 1,
            1 => 5,
            2 => 15,
            3 => 30,
            _ => 60
        };

        public static int GetAutoLockSelectionFromMinutes(int minutes) => minutes switch
        {
            <= 1 => 0,
            <= 5 => 1,
            <= 15 => 2,
            <= 30 => 3,
            _ => 4
        };

        public static int? GetMaxFailedUnlockAttemptsFromSelection(int selection) => selection switch
        {
            0 => 3,
            1 => 5,
            2 => 10,
            3 => null,
            _ => 10
        };

        public static int GetFailedUnlockSelectionFromMaxAttempts(int? maxFailedAttempts) => maxFailedAttempts switch
        {
            3 => 0,
            5 => 1,
            10 => 2,
            null => 3,
            _ => 2
        };

        // Settings were previously written as plaintext, unauthenticated JSON. Two problems:
        //
        //  1. Deniability. The Decoy* keys are visible to anyone who opens the file, and
        //     their presence proves a decoy vault is configured — which in turn proves a
        //     real vault sits behind whichever one an observer was shown. That is exactly
        //     the inference the decoy feature exists to prevent.
        //  2. Integrity. Anyone with write access could silently downgrade the security
        //     posture: disable screenshot protection, set unlock attempts to unlimited,
        //     turn off lock-on-screen-lock. The app loaded tampered values without complaint.
        //
        // DPAPI (CurrentUser) is the right primitive here because settings must load before
        // the vault is unlocked, so no vault key is available. It is authenticated, so
        // Unprotect throws on tamper — no separate MAC is needed. Same approach already
        // used for the autofill origin allowlist.
        private static string SealedSettingsPath => Path.Combine(SettingsDir, "settings.dat");
        private static readonly byte[] SettingsEntropy =
            System.Text.Encoding.UTF8.GetBytes("PhantomVault.user-settings.v1");

        // Load() is called from ~130 sites, several of them hot: the idle watchdog resets
        // on every user input, and the session-policy timer polls on a tick. Now that the
        // settings blob is DPAPI-sealed, an uncached Load() would mean a file read plus a
        // decrypt per keystroke. The cache is invalidated by Save/Update, which are the
        // only writers, and by an external file change.
        private static readonly object _cacheLock = new();
        private static UserSettings? _cached;
        private static DateTime _cachedFileStampUtc;

        private static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cached = null;
            }
        }

        public static UserSettings Load()
        {
            lock (_cacheLock)
            {
                if (_cached != null)
                {
                    // Cheap staleness check so an external edit (or another process) is
                    // still picked up without re-reading and decrypting the file every call.
                    try
                    {
                        var stamp = File.Exists(SealedSettingsPath)
                            ? File.GetLastWriteTimeUtc(SealedSettingsPath)
                            : DateTime.MinValue;

                        if (stamp == _cachedFileStampUtc)
                        {
                            return Clone(_cached);
                        }
                    }
                    catch
                    {
                        return Clone(_cached);
                    }
                }
            }

            var loaded = LoadUncached();

            lock (_cacheLock)
            {
                _cached = loaded;
                try
                {
                    _cachedFileStampUtc = File.Exists(SealedSettingsPath)
                        ? File.GetLastWriteTimeUtc(SealedSettingsPath)
                        : DateTime.MinValue;
                }
                catch
                {
                    _cachedFileStampUtc = DateTime.MinValue;
                }

                return Clone(loaded);
            }
        }

        // Load() has always handed back a fresh object and callers rely on that — several
        // mutate the result and only some of them call Save(). Returning the cached
        // instance directly would turn a discarded edit into an applied one, so every
        // caller gets its own copy via Clone() (defined below).

        private static UserSettings LoadUncached()
        {
            try
            {
                if (File.Exists(SealedSettingsPath))
                {
                    var json = UnsealSettings(File.ReadAllBytes(SealedSettingsPath));
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }

                // Legacy plaintext file: read once, re-seal, remove the plaintext copy.
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                    TryMigratePlaintextSettings(settings);
                    return settings;
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                // Tampered, truncated, or sealed under a different Windows profile.
                // Defaults are the safe posture — they are the hardened values.
                Log.Error(ex, "User settings failed integrity check; falling back to defaults");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load user settings, returning defaults");
            }
            return new UserSettings();
        }

        public static void Save(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

                WriteAtomic(SealedSettingsPath, SealSettings(json));

                // Remove any legacy plaintext file so the old copy cannot be read or edited.
                TryDeletePlaintextSettings();

                InvalidateCache();
                RaiseSettingsChanged(settings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save user settings");
            }
        }

        /// <summary>
        /// Write via a temp file plus atomic replace. A direct WriteAllText that is
        /// interrupted mid-write leaves a truncated file, which now means a failed
        /// integrity check and a silent reset of every setting.
        /// </summary>
        private static void WriteAtomic(string path, byte[] contents)
        {
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, contents);

            if (File.Exists(path))
            {
                // Replace is atomic on NTFS and preserves the destination on failure.
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static byte[] SealSettings(string json)
        {
            var plain = System.Text.Encoding.UTF8.GetBytes(json);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return System.Security.Cryptography.ProtectedData.Protect(
                        plain, SettingsEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                }

                // Non-Windows has no DPAPI equivalent here. Returning the raw bytes keeps
                // the app functional; the file permissions are the only protection.
                return (byte[])plain.Clone();
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static string UnsealSettings(byte[] sealedBytes)
        {
            if (!OperatingSystem.IsWindows())
            {
                return System.Text.Encoding.UTF8.GetString(sealedBytes);
            }

            var plain = System.Security.Cryptography.ProtectedData.Unprotect(
                sealedBytes, SettingsEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            try
            {
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static void TryMigratePlaintextSettings(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                WriteAtomic(SealedSettingsPath, SealSettings(json));
                TryDeletePlaintextSettings();
                InvalidateCache();
                Log.Information("Migrated user settings to sealed storage");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to migrate plaintext user settings to sealed storage");
            }
        }

        private static void TryDeletePlaintextSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to remove legacy plaintext settings file");
            }
        }

        public static UserSettings Update(Action<UserSettings> update)
        {
            ArgumentNullException.ThrowIfNull(update);

            var settings = Load();
            update(settings);
            Save(settings);
            return settings;
        }

        public static T Update<T>(Func<UserSettings, T> update)
        {
            ArgumentNullException.ThrowIfNull(update);

            var settings = Load();
            var result = update(settings);
            Save(settings);
            return result;
        }

        public static SecuritySettingsSnapshot LoadSecuritySnapshot()
        {
            var settings = Load();
            return new SecuritySettingsSnapshot(
                settings.EnablePinLock,
                settings.UsePinLockForAutoLock,
                settings.AutoCopyTotpWithPassword,
                settings.RequireHardwareToken,
                settings.RequireKeyfile,
                settings.IdleTimeoutMinutes,
                settings.ClipboardClearTime,
                settings.EnableScreenshotProtection,
                settings.EnableDecoyVault,
                settings.DecoyCredentialCount,
                settings.DecoyReadOnlyMode,
                settings.DecoyLogActivations);
        }

        public static AdvancedSettingsSnapshot LoadAdvancedSnapshot()
        {
            var settings = Load();
            return new AdvancedSettingsSnapshot(
                settings.EnableDebugLogging,
                settings.PrivacyModeEnabled,
                settings.RedactDiagnosticLogs,
                settings.BlockRemoteDebugging,
                settings.SessionTimeoutMinutes,
                settings.AutoLockOnMinimize,
                settings.AutoLockOnScreenLock,
                settings.ClearClipboardOnLock,
                settings.RequireUnlockToShow,
                settings.MaxFailedUnlockAttempts);
        }

        public static VaultSettingsSnapshot LoadVaultSnapshot()
        {
            var settings = Load();
            return new VaultSettingsSnapshot(
                settings.PrivacyModeEnabled,
                settings.RedactDiagnosticLogs,
                settings.SecureTrashEnabled,
                settings.SecureTrashAutoPurge,
                settings.SecureTrashRetentionDays,
                settings.SecureTrashWipePasses,
                settings.ClipboardClearTime,
                settings.IsDarkTheme,
                settings.PreferGridView,
                settings.DashboardEnabled,
                settings.EnableAutoFill,
                settings.DefaultPasswordLength,
                settings.PasswordGeneratorIncludeUppercase,
                settings.PasswordGeneratorIncludeLowercase,
                settings.PasswordGeneratorIncludeNumbers,
                settings.PasswordGeneratorIncludeSymbols,
                settings.EnableDebugLogging);
        }

        private static void RaiseSettingsChanged(UserSettings settings)
        {
            try
            {
                SettingsChanged?.Invoke(null, new UserSettingsChangedEventArgs(Clone(settings)));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to raise settings changed event");
            }
        }

        // Was a JSON serialize/deserialize round-trip. That is correct but far too
        // expensive to sit on the Load() path now that Load() is cached and called on
        // every user-input event via the idle watchdog. MemberwiseClone plus explicit
        // copies of the two collection members is equivalent for this type — every other
        // member is a value type or an immutable string.
        private static UserSettings Clone(UserSettings settings) => settings.CreateCopy();
    }
}

