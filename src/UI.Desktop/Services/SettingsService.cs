using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace PhantomVault.UI.Services
{
    public sealed class UserSettings
    {
        public string? VeraCryptPathOverride { get; set; }
        public string? VeraCryptInstallerUrl { get; set; }
        public bool PrivacyModeEnabled { get; set; }
        public bool RedactDiagnosticLogs { get; set; } = true;
        public bool SecureTrashEnabled { get; set; } = true;
        public bool SecureTrashAutoPurge { get; set; } = true;
        public int SecureTrashRetentionDays { get; set; } = 30;
        public int SecureTrashWipePasses { get; set; } = 3;
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
        /// Default setting for enabling Windows Hello/Passkey on new vaults.
        /// </summary>
        public bool DefaultUsePasskey { get; set; } = false;

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

    public static class SettingsService
    {
        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault");
        private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save user settings to {SettingsPath}", SettingsPath);
            }
        }
    }
}
