using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.AutoFill;
using Xunit;

namespace PhantomVault.UI.Tests
{
    /// <summary>
    /// What the user must present when a vault's USB is plugged in.
    ///
    /// The rule being protected here is the one that matters: a stored preference is allowed to
    /// choose *between* the factors a vault has, and is never allowed to require *less* than the
    /// vault is actually protected by. The USB is the component most likely to be lost or stolen,
    /// so "keyfile only" on a vault that has a PIN would hand the whole vault to whoever finds it.
    /// </summary>
    public sealed class UsbStartupUnlockPolicyTests
    {
        private static VaultManifest ManifestWithPin() =>
            new() { PinHashBase64 = "not-a-real-hash", PinSaltBase64 = "salt" };

        private static VaultManifest BareManifest() => new();

        private static UserSettings SettingsPreferring(UsbStartupFactor factor) =>
            new() { AutoFillUsbStartupFactor = factor };

        [Fact]
        public void A_vault_with_no_other_factor_opens_on_its_keyfile()
        {
            var resolved = UsbStartupUnlockPolicy.Resolve(
                BareManifest(),
                SettingsPreferring(UsbStartupFactor.Automatic),
                windowsHelloEnrolled: false,
                keyfileOnlyVault: true);

            Assert.Equal(UsbStartupFactor.KeyfileOnly, resolved);
        }

        [Fact]
        public void A_vault_with_a_passphrase_is_never_reduced_to_keyfile_only()
        {
            var resolved = UsbStartupUnlockPolicy.Resolve(
                BareManifest(),
                SettingsPreferring(UsbStartupFactor.KeyfileOnly),
                windowsHelloEnrolled: false,
                keyfileOnlyVault: false);

            Assert.NotEqual(UsbStartupFactor.KeyfileOnly, resolved);
            Assert.Equal(UsbStartupFactor.Passphrase, resolved);
        }

        [Fact]
        public void A_vault_with_a_pin_is_never_reduced_to_keyfile_only()
        {
            var resolved = UsbStartupUnlockPolicy.Resolve(
                ManifestWithPin(),
                SettingsPreferring(UsbStartupFactor.KeyfileOnly),
                windowsHelloEnrolled: false,
                keyfileOnlyVault: false);

            Assert.Equal(UsbStartupFactor.Pin, resolved);
        }

        [Fact]
        public void The_users_choice_is_honoured_when_the_vault_has_that_factor()
        {
            var resolved = UsbStartupUnlockPolicy.Resolve(
                ManifestWithPin(),
                SettingsPreferring(UsbStartupFactor.Passphrase),
                windowsHelloEnrolled: true,
                keyfileOnlyVault: false);

            Assert.Equal(UsbStartupFactor.Passphrase, resolved);
        }

        [Fact]
        public void A_choice_the_vault_cannot_satisfy_falls_back_rather_than_locking_the_user_out()
        {
            // Hello is preferred but not enrolled on this machine. Demanding it would make the
            // vault unopenable here, so the strongest available factor is used instead.
            var resolved = UsbStartupUnlockPolicy.Resolve(
                ManifestWithPin(),
                SettingsPreferring(UsbStartupFactor.WindowsHello),
                windowsHelloEnrolled: false,
                keyfileOnlyVault: false);

            Assert.Equal(UsbStartupFactor.Pin, resolved);
        }

        [Fact]
        public void Automatic_prefers_the_strongest_factor_the_app_can_actually_enforce()
        {
            // Hello is enrolled on this machine, but the unlock path cannot authenticate with it
            // yet, so the policy must not nominate it. Resolving to Hello would tell the user the
            // vault is gated on their fingerprint when nothing checks it.
            //
            // When Hello-backed unlock is implemented, flip HelloUnlockImplemented and this
            // expectation becomes WindowsHello.
            var resolved = UsbStartupUnlockPolicy.Resolve(
                ManifestWithPin(),
                SettingsPreferring(UsbStartupFactor.Automatic),
                windowsHelloEnrolled: true,
                keyfileOnlyVault: false);

            Assert.Equal(UsbStartupFactor.Pin, resolved);
        }

        [Fact]
        public void Available_factors_report_what_the_vault_actually_has()
        {
            var withPin = UsbStartupUnlockPolicy.AvailableFactors(
                ManifestWithPin(), new UserSettings(), windowsHelloEnrolled: true);

            Assert.Contains(UsbStartupFactor.Pin, withPin);
            Assert.Contains(UsbStartupFactor.Passphrase, withPin);

            // Enrolled, but not offered: no unlock branch consumes Hello yet. A factor the app
            // cannot check must never appear as one it will.
            Assert.DoesNotContain(UsbStartupFactor.WindowsHello, withPin);

            var bare = UsbStartupUnlockPolicy.AvailableFactors(
                BareManifest(), new UserSettings(), windowsHelloEnrolled: false);

            Assert.DoesNotContain(UsbStartupFactor.Pin, bare);
            Assert.DoesNotContain(UsbStartupFactor.WindowsHello, bare);
        }
    }
}
