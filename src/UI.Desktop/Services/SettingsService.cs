using System;
using System.Collections.Generic;
using System.IO;
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

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load user settings from {SettingsPath}, returning defaults", SettingsPath);
            }
            return new UserSettings();
        }

        public static void Save(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                RaiseSettingsChanged(settings);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Settings saved to {SettingsPath}");
                System.Diagnostics.Debug.WriteLine($"[SettingsService] EnableScreenshotProtection in file: {settings.EnableScreenshotProtection}");
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save user settings to {SettingsPath}", SettingsPath);
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

        private static UserSettings Clone(UserSettings settings)
        {
            var json = JsonSerializer.Serialize(settings);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
    }
}

