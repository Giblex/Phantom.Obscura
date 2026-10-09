using System;
using System.IO;
using System.Security.Cryptography;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.BootRom;
using PhantomVault.Core.Utils;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Boot ROM key binding at the manifest derivation chokepoint.
    ///
    /// The property that protects existing vaults: with nothing registered in
    /// <see cref="BootRomSession"/>, derivation is exactly what it was before binding existed.
    /// The property that makes binding worth having: once a vault is written with a
    /// contribution, it does not open without it.
    /// </summary>
    [Collection(BootRomSessionCollection.Name)]
    public sealed class BootRomBindingTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PhantomVault_BootRom_{Guid.NewGuid():N}");
        private readonly ManifestService _service = new(new EncryptionService());

        private string ManifestPath => Path.Combine(_root, "vault.manifest");
        private string KeyfilePath => Path.Combine(_root, "vault.key");

        public BootRomBindingTests()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(KeyfilePath, RandomNumberGenerator.GetBytes(64));
        }

        public void Dispose()
        {
            BootRomSession.ClearAll();
            try { Directory.Delete(_root, true); } catch { /* best-effort cleanup */ }
        }

        private static VaultManifest NewManifest() => new()
        {
            VaultName = "Boot ROM test vault",
            ContainerPath = "container",
        };

        private void Write(string path) =>
            _service.WriteManifestSecure(NewManifest(), path, SecurePassword.FromString("correct horse"), KeyfilePath);

        private VaultManifest Read(string path) =>
            _service.ReadManifestSecure(path, SecurePassword.FromString("correct horse"), KeyfilePath);

        [Fact]
        public void A_vault_with_no_contribution_registered_behaves_exactly_as_before()
        {
            Write(ManifestPath);
            var manifest = Read(ManifestPath);

            Assert.Equal("Boot ROM test vault", manifest.VaultName);
            Assert.False(BootRomSession.IsBound(ManifestPath));
        }

        [Fact]
        public void A_bound_vault_round_trips_with_its_contribution()
        {
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));

            Write(ManifestPath);
            var manifest = Read(ManifestPath);

            Assert.Equal("Boot ROM test vault", manifest.VaultName);
        }

        [Fact]
        public void A_bound_vault_does_not_open_without_the_contribution()
        {
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));
            Write(ManifestPath);

            // Boot ROM missing at the next unlock: the derived key is simply wrong.
            BootRomSession.Clear(ManifestPath);

            Assert.ThrowsAny<Exception>(() => Read(ManifestPath));
        }

        [Fact]
        public void A_bound_vault_does_not_open_with_the_wrong_contribution()
        {
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));
            Write(ManifestPath);

            // A substituted or forged ROM produces different material.
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));

            Assert.ThrowsAny<Exception>(() => Read(ManifestPath));
        }

        [Fact]
        public void An_unbound_vault_does_not_open_once_a_contribution_is_registered()
        {
            // The mirror case: binding genuinely changes the key, so a vault written without a
            // ROM cannot be read as though it had one.
            Write(ManifestPath);
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));

            Assert.ThrowsAny<Exception>(() => Read(ManifestPath));
        }

        [Fact]
        public void Contributions_are_per_vault()
        {
            string otherPath = Path.Combine(_root, "other.manifest");

            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));
            Write(ManifestPath);   // bound
            BootRomSession.Clear(ManifestPath);

            Write(otherPath);      // unbound, written while the first vault's entry is absent
            Assert.Equal("Boot ROM test vault", Read(otherPath).VaultName);

            // Registering a contribution for one vault must not affect the other.
            BootRomSession.Set(ManifestPath, RandomNumberGenerator.GetBytes(32));
            Assert.Equal("Boot ROM test vault", Read(otherPath).VaultName);
        }

        [Fact]
        public void Session_rejects_a_contribution_that_is_not_32_bytes()
        {
            Assert.Throws<ArgumentException>(() => BootRomSession.Set(ManifestPath, new byte[16]));
        }
    }
}
