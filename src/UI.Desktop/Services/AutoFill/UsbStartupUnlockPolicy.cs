using System;
using System.Collections.Generic;
using PhantomVault.Core.Models;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Which single factor the user must satisfy when a vault's USB is plugged in and autofill
    /// opens it.
    /// </summary>
    public enum UsbStartupFactor
    {
        /// <summary>
        /// Use whatever the vault has configured, preferring the strongest available. The default,
        /// so a vault that later gains a PIN starts asking for it without the user revisiting
        /// settings.
        /// </summary>
        Automatic = 0,

        /// <summary>
        /// The keyfile on the stick is the only thing required. Only honoured when the vault has
        /// no other factor configured — see <see cref="Resolve"/>.
        /// </summary>
        KeyfileOnly = 1,

        WindowsHello = 2,
        Pin = 3,
        Passphrase = 4
    }

    /// <summary>
    /// Works out what the user must present when the vault's USB is inserted.
    ///
    /// The rule, in the user's words: with no PIN, password or Windows Hello configured, the
    /// keyfile on the stick is enough; if any of those exist, exactly one of them is required, and
    /// which one is their choice.
    ///
    /// The important half is what this refuses to do. A stored preference can never downgrade a
    /// vault below what it is actually protected by: if the vault has a PIN and the setting says
    /// "keyfile only", the PIN is still demanded. Otherwise flipping one dropdown would turn a
    /// protected vault into one that opens for anybody holding the stick — and the stick is the
    /// thing most likely to be lost or stolen.
    /// </summary>
    public static class UsbStartupUnlockPolicy
    {
        /// <summary>
        /// Whether the unlock path can authenticate with Windows Hello. False today: Hello can be
        /// enrolled, but no unlock branch consumes it.
        /// </summary>
        internal const bool HelloUnlockImplemented = false;

        /// <summary>
        /// The factors this vault could actually demand, strongest first. Empty means the vault has
        /// nothing beyond its keyfile.
        /// </summary>
        public static IReadOnlyList<UsbStartupFactor> AvailableFactors(
            VaultManifest? manifest,
            UserSettings? settings,
            bool windowsHelloEnrolled)
        {
            var available = new List<UsbStartupFactor>();

            // Windows Hello is enrollable today but the unlock path cannot yet authenticate with
            // it — VaultUnlockViewModel accepts a keyfile, an empty password or a passphrase, and
            // nothing else. Offering Hello as the required factor would name a check the app
            // cannot actually perform, which is worse than not offering it: the user would believe
            // the vault was gated on their fingerprint when it was not.
            //
            // Flip this to true in the same change that implements Hello-backed unlock.
            if (windowsHelloEnrolled && HelloUnlockImplemented)
                available.Add(UsbStartupFactor.WindowsHello);

            // A PIN counts whether it was set on the vault itself or as an app lock: either way the
            // user has one and expects to be asked for it.
            bool hasPin =
                !string.IsNullOrWhiteSpace(manifest?.PinHashBase64) ||
                (settings?.EnablePinLock == true && !string.IsNullOrWhiteSpace(settings.PinHashBase64));

            if (hasPin)
                available.Add(UsbStartupFactor.Pin);

            // A passphrase is always a possibility — the vault was created with one unless it is
            // explicitly keyfile-only, and asking for it is never a downgrade.
            available.Add(UsbStartupFactor.Passphrase);

            return available;
        }

        /// <summary>
        /// The factor to actually require, given the vault, the user's preference, and what is
        /// enrolled on this machine.
        /// </summary>
        /// <param name="manifest">The vault being opened.</param>
        /// <param name="settings">App settings, for the PIN lock and the stored preference.</param>
        /// <param name="windowsHelloEnrolled">Whether Hello is usable on this device right now.</param>
        /// <param name="keyfileOnlyVault">
        /// True when the vault genuinely has no passphrase — a keyfile-only vault. Only then can
        /// the result be <see cref="UsbStartupFactor.KeyfileOnly"/>.
        /// </param>
        public static UsbStartupFactor Resolve(
            VaultManifest? manifest,
            UserSettings? settings,
            bool windowsHelloEnrolled,
            bool keyfileOnlyVault)
        {
            var available = AvailableFactors(manifest, settings, windowsHelloEnrolled);

            // Nothing configured beyond the stick: the keyfile is the credential.
            if (keyfileOnlyVault && available.Count <= 1 && available.Contains(UsbStartupFactor.Passphrase))
            {
                return UsbStartupFactor.KeyfileOnly;
            }

            var preferred = settings?.AutoFillUsbStartupFactor ?? UsbStartupFactor.Automatic;

            if (preferred == UsbStartupFactor.Automatic)
                return available[0];

            // The preference is honoured only if that factor is actually available. Asking for a
            // PIN that does not exist would lock the user out of their own vault; silently
            // accepting "keyfile only" on a protected vault would do the opposite.
            if (available.Contains(preferred))
                return preferred;

            if (preferred == UsbStartupFactor.KeyfileOnly && !keyfileOnlyVault)
            {
                Log.Warning(
                    "[AutoFill] USB startup is set to keyfile-only, but this vault has {Count} factor(s) configured. " +
                    "Requiring {Factor} instead — a preference must not weaken a vault.",
                    available.Count, available[0]);
            }

            return available[0];
        }
    }
}
