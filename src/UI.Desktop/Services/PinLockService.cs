using System;
using System.IO;
using PhantomVault.Core.Models;
using PhantomVault.Core.Security;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// UI-side glue for the vault's soft-lock PIN: keeping the settings toggles honest and
    /// retiring the stores older builds used.
    ///
    /// The PIN record itself lives inside the encrypted vault manifest and is checked against
    /// the copy decrypted at unlock; see <see cref="VaultPinLock"/>. It used to be a
    /// DPAPI-sealed sidecar (&lt;manifest&gt;.pin.json) or a settings.json fallback, which any
    /// program running as the same Windows user could replace with a PIN of its own choosing.
    /// </summary>
    internal static class PinLockService
    {
        public const int MinVaultPinLength = VaultPinLock.MinLength;
        public const int MaxVaultPinLength = VaultPinLock.MaxLength;

        private const string LegacySidecarSuffix = ".pin.json";

        public static bool HasPinConfigured(VaultManifest? manifest) => VaultPinLock.IsConfigured(manifest);

        /// <summary>
        /// Clears the EnablePinLock / UsePinLockForAutoLock flags when the loaded manifest holds
        /// no PIN, so auto-lock never demands a PIN that does not exist. Returns whether a PIN is
        /// configured. With no manifest loaded nothing is known yet, so the flags are left alone.
        /// </summary>
        public static bool SyncPinFlags(VaultManifest? manifest)
        {
            if (manifest == null) return false;
            if (VaultPinLock.IsConfigured(manifest)) return true;

            UserSettings settings;
            try
            {
                settings = SettingsService.Load();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[PinLock] Could not load settings to sync the PIN flags");
                return false;
            }

            if (settings.EnablePinLock || settings.UsePinLockForAutoLock)
            {
                settings.EnablePinLock = false;
                settings.UsePinLockForAutoLock = false;
                try
                {
                    SettingsService.Save(settings);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[PinLock] Could not clear stale PIN flags");
                }
            }

            return false;
        }

        /// <summary>
        /// Deletes the PIN stores older builds used: the &lt;manifest&gt;.pin.json sidecar and the
        /// settings.json fallback. Nothing in them is trusted any more; a record found there could
        /// have been planted by another program, so it is removed, never migrated.
        /// </summary>
        /// <returns>True if anything was removed, so the user can be told to set the PIN again.</returns>
        public static bool DiscardLegacyPinStores(string? manifestPath)
        {
            bool discarded = false;

            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                string sidecar = manifestPath + LegacySidecarSuffix;
                try
                {
                    if (File.Exists(sidecar))
                    {
                        File.Delete(sidecar);
                        discarded = true;
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[PinLock] Could not delete the legacy PIN sidecar");
                }
            }

            try
            {
                var settings = SettingsService.Load();
                if (settings.PinSaltBase64 != null || settings.PinHashBase64 != null)
                {
                    settings.PinSaltBase64 = null;
                    settings.PinHashBase64 = null;
                    SettingsService.Save(settings);
                    discarded = true;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[PinLock] Could not clear the legacy settings PIN");
            }

            if (discarded)
                Serilog.Log.Information("[PinLock] Removed a PIN stored outside the encrypted manifest");

            return discarded;
        }
    }
}
