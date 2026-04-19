using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace PhantomVault.UI.Services
{
    public sealed class UserSettings
    {
        public bool PrivacyModeEnabled { get; set; }
        public bool RedactDiagnosticLogs { get; set; } = true;
        public bool EnableDebugLogging { get; set; } = false;
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
        public bool IsDarkTheme { get; set; } = true;
        public int ThemeSkin { get; set; } = 0;
        public bool EnableHighContrast { get; set; } = false;
        public bool ReduceAnimations { get; set; } = false;
        public bool ReduceTransparency { get; set; } = false;
        /// <summary>
        /// Selected runtime theme ID (e.g., "ClassicDark", "GiblexGlassNavy").
        /// </summary>
        public string SelectedThemeId { get; set; } = "ClassicDark";
        /// <summary>
        /// Clipboard clear time index: 0=30s, 1=1min, 2=2min, 3=5min, 4=never.
        /// </summary>
        public int ClipboardClearTime { get; set; } = 1;

        // ===== Lock Screen / Re-auth Settings =====

        /// <summary>
        /// Enables a local PIN lock to re-authenticate without exiting the app.
        /// This PIN is a local UI lock and is not the vault encryption passphrase.
        /// </summary>
        public bool EnablePinLock { get; set; } = false;

        /// <summary>
        /// If enabled, auto-lock uses the in-app PIN lockscreen (soft lock) instead of dismounting.
        /// </summary>
        public bool UsePinLockForAutoLock { get; set; } = false;

        /// <summary>
        /// Base64-encoded random salt used for PIN PBKDF2 hashing.
        /// </summary>
        public string? PinSaltBase64 { get; set; }

        /// <summary>
        /// Base64-encoded PBKDF2 hash for the PIN.
        /// </summary>
        public string? PinHashBase64 { get; set; }

        /// <summary>
        /// PBKDF2 iteration count used for the PIN hash.
        /// </summary>
        public int PinPbkdf2Iterations { get; set; } = 150_000;

        /// <summary>
        /// Auto-copy TOTP code when copying passwords for entries with TOTP enabled.
        /// </summary>
        public bool AutoCopyTotpWithPassword { get; set; } = false;

        /// <summary>
        /// Enable screenshot protection (window appears black in screenshots/recordings).
        /// </summary>
        public bool EnableScreenshotProtection { get; set; } = true;

        // ===== Security Settings =====

        /// <summary>
        /// Require hardware token for vault authentication.
        /// </summary>
        public bool RequireHardwareToken { get; set; } = false;

        /// <summary>
        /// Require keyfile for vault authentication.
        /// </summary>
        public bool RequireKeyfile { get; set; } = false;

        /// <summary>
        /// Idle timeout in minutes before auto-lock.
        /// </summary>
        public int IdleTimeoutMinutes { get; set; } = 15;

        /// <summary>
        /// When true, lock the vault when the window is minimized.
        /// </summary>
        public bool AutoLockOnMinimize { get; set; } = false;

        /// <summary>
        /// When true, lock the vault when the Windows session is locked.
        /// </summary>
        public bool AutoLockOnScreenLock { get; set; } = true;

        /// <summary>
        /// When true, clear the clipboard when the vault enters its lockscreen state.
        /// </summary>
        public bool ClearClipboardOnLock { get; set; } = true;

        /// <summary>
        /// When true, show the vault window in a locked state until the user re-authenticates.
        /// </summary>
        public bool RequireUnlockToShow { get; set; } = false;

        /// <summary>
        /// Maximum failed unlock attempts before lockout. Null means unlimited.
        /// </summary>
        public int? MaxFailedUnlockAttempts { get; set; } = 10;

        /// <summary>
        /// Session timeout in minutes used by advanced policy preferences.
        /// </summary>
        public int SessionTimeoutMinutes { get; set; } = 30;

        /// <summary>
        /// Saved preference for startup hardening against remote debugging.
        /// </summary>
        public bool BlockRemoteDebugging { get; set; } = true;

        /// <summary>
        /// Enable decoy vault protection.
        /// </summary>
        public bool EnableDecoyVault { get; set; } = false;

        /// <summary>
        /// Number of fake credentials to generate for decoy vault.
        /// </summary>
        public int DecoyCredentialCount { get; set; } = 20;

        /// <summary>
        /// Enable read-only mode when decoy vault is active.
        /// </summary>
        public bool DecoyReadOnlyMode { get; set; } = true;

        /// <summary>
        /// Log decoy vault activation events.
        /// </summary>
        public bool DecoyLogActivations { get; set; } = true;

        // ===== Window State Settings =====

        /// <summary>
        /// MainWindow X position. Null means center on screen.
        /// </summary>
        public double? MainWindowX { get; set; }

        /// <summary>
        /// MainWindow Y position. Null means center on screen.
        /// </summary>
        public double? MainWindowY { get; set; }

        /// <summary>
        /// MainWindow width.
        /// </summary>
        public double? MainWindowWidth { get; set; }

        /// <summary>
        /// MainWindow height.
        /// </summary>
        public double? MainWindowHeight { get; set; }

        /// <summary>
        /// MainWindow state (Normal, Minimized, Maximized).
        /// </summary>
        public string? MainWindowState { get; set; }

        // ===== View Preferences =====

        /// <summary>
        /// Default vault view mode: true for grid view, false for list view.
        /// </summary>
        public bool PreferGridView { get; set; } = false;

        /// <summary>
        /// Preferred default password generator length.
        /// </summary>
        public int DefaultPasswordLength { get; set; } = 16;

        /// <summary>
        /// Preferred password generator character set toggles.
        /// </summary>
        public bool PasswordGeneratorIncludeUppercase { get; set; } = true;
        public bool PasswordGeneratorIncludeLowercase { get; set; } = true;
        public bool PasswordGeneratorIncludeNumbers { get; set; } = true;
        public bool PasswordGeneratorIncludeSymbols { get; set; } = true;

        /// <summary>
        /// When false, the Dashboard view is disabled and the app starts on the Passwords view.
        /// The sidebar Dashboard button is hidden.
        /// </summary>
        public bool DashboardEnabled { get; set; } = true;

        // ===== Category Manager Preferences =====

        /// <summary>
        /// Last selected icon library path for category icons.
        /// </summary>
        public string? LastIconLibraryPath { get; set; }

        /// <summary>
        /// Default category tile color (hex format, e.g., "#2196F3").
        /// </summary>
        public string? DefaultCategoryColor { get; set; }

        /// <summary>
        /// Remember last opened category in vault.
        /// </summary>
        public string? LastActiveCategory { get; set; }

        // ===== Icon Manager Preferences =====

        /// <summary>
        /// Last selected icon pack or folder.
        /// </summary>
        public string? LastIconPack { get; set; }

        /// <summary>
        /// Icon size preference for icon manager (Small, Medium, Large).
        /// </summary>
        public string? IconDisplaySize { get; set; } = "Medium";

        // ===== Encryption Preferences =====

        /// <summary>
        /// Preferred encryption profile for new vaults (Basic, Advanced, Paranoid).
        /// </summary>
        public string PreferredEncryptionProfile { get; set; } = "Advanced";

        /// <summary>
        /// Default storage/privacy tier to preselect during new-vault setup.
        /// </summary>
        public string DefaultVaultProtectionTier { get; set; } = "StealthSecure";

        // ===== Authentication Preferences =====

        /// <summary>
        /// Default setting for requiring hardware token on new vaults.
        /// </summary>
        public bool DefaultRequireHardwareToken { get; set; } = false;

        /// <summary>
        /// Default setting for enabling TOTP on new vaults.
        /// </summary>
        public bool DefaultUseTotp { get; set; } = false;

        /// <summary>
        /// Shows post-create authentication onboarding on the next successful unlock.
        /// </summary>
        public bool PendingPostCreateAuthOnboarding { get; set; } = false;

        public bool PendingSetupWindowsHello { get; set; } = false;
        public bool PendingSetupPasskey { get; set; } = false;
        public bool PendingSetupTotp { get; set; } = false;

        /// <summary>
        /// Default setting for enabling Windows Hello/Passkey on new vaults.
        /// </summary>
        public bool DefaultUsePasskey { get; set; } = false;

        /// <summary>
        /// Vault unlock preference chosen during first-time setup.
        /// Values: "Pin", "WindowsHello", "Automatic", or null if not yet configured.
        /// </summary>
        public string? VaultUnlockPreference { get; set; }

        /// <summary>
        /// Remember last authentication method used (YubiKey, Passkey, TOTP, etc.).
        /// </summary>
        public string? LastAuthenticationMethod { get; set; }

        // ===== AutoFill Preferences =====

        /// <summary>
        /// Enable browser auto-fill for credentials.
        /// </summary>
        public bool EnableAutoFill { get; set; } = false;

        /// <summary>
        /// Auto-inject username field during autofill.
        /// </summary>
        public bool AutoFillInjectUsername { get; set; } = true;

        /// <summary>
        /// Auto-inject password field during autofill.
        /// </summary>
        public bool AutoFillInjectPassword { get; set; } = true;

        /// <summary>
        /// Comma-separated whitelist of domains for autofill (empty = all domains).
        /// </summary>
        public string AutoFillDomainWhitelist { get; set; } = string.Empty;

        // ===== AutoFill Mode (USB-Triggered) =====

        /// <summary>
        /// When true, the app runs in system tray and auto-fills credentials on USB insertion
        /// for both browser and native Windows login portals. Passkeys are prioritised
        /// when Attestor is linked.
        /// </summary>
        public bool AutoFillModeEnabled { get; set; } = false;

        /// <summary>
        /// When true, TOTP codes are automatically detected and input after the password
        /// fill step completes.
        /// </summary>
        public bool AutoFillAutoInputTotp { get; set; } = true;

        /// <summary>
        /// When true, the no-match dialog is shown if no stored credential matches
        /// the active login portal. Passkey handoff is only exposed when Attestor
        /// integration is linked.
        /// </summary>
        public bool AutoFillShowNewEntryOnNoMatch { get; set; } = true;

        /// <summary>
        /// Milliseconds to wait after password fill before starting to poll for a TOTP field.
        /// </summary>
        public int AutoFillTotpPollDelayMs { get; set; } = 1500;

        /// <summary>
        /// Maximum milliseconds to spend polling for a TOTP field before giving up.
        /// </summary>
        public int AutoFillTotpPollTimeoutMs { get; set; } = 8000;

        // ===== Cross-App Sync Settings =====

        /// <summary>
        /// Enable cross-app sync with other Phantom apps.
        /// </summary>
        public bool SyncEnabled { get; set; } = true;

        /// <summary>
        /// Sync theme selection across Phantom apps.
        /// </summary>
        public bool SyncTheme { get; set; } = true;

        /// <summary>
        /// Last time settings were synced from another app.
        /// </summary>
        public DateTimeOffset? LastSyncTime { get; set; }

        /// <summary>
        /// Gets the clipboard clear delay as a TimeSpan based on ClipboardClearTime index.
        /// Returns null if "Never" is selected.
        /// </summary>
        public TimeSpan? GetClipboardClearDelay()
        {
            return ClipboardClearTime switch
            {
                0 => TimeSpan.FromSeconds(30),
                1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(2),
                3 => TimeSpan.FromMinutes(5),
                4 => null, // Never
                _ => TimeSpan.FromMinutes(1) // Default fallback
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
