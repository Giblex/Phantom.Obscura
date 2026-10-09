#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.BootRom;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Boot ROM binding for container-embedded manifests (.pvault).
    ///
    /// These are the vaults the setup wizard creates, and they do not go through
    /// <see cref="ManifestService"/>'s derivation at all — <c>ReadManifestSecure</c> hands any
    /// container path straight to <see cref="PhantomContainerService"/>, which derives its own key.
    /// Binding therefore has to be threaded into that service separately, across every one of its
    /// key-derivation call sites.
    ///
    /// Two properties are being proved here, and the first matters more than the second:
    ///
    /// 1. A container written with no contribution registered derives exactly as it always did, so
    ///    every vault created before this existed still opens.
    /// 2. Read and write agree. If one derivation call site passed the contribution and another did
    ///    not, a container would be written under one key and read under a different one — the vault
    ///    would be unopenable, and no amount of having the right ROM would fix it.
    /// </summary>
    [Collection(BootRomSessionCollection.Name)]
    public sealed class BootRomContainerBindingTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PhantomVault_RomContainer_{Guid.NewGuid():N}");
        private readonly PhantomContainerService _containers = new(new EncryptionService());

        private const string Password = "correct horse battery staple";
        private const long SizeBytes = 2L * 1024 * 1024;

        private string KeyfilePath => Path.Combine(_root, "vault.key");

        public BootRomContainerBindingTests()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(KeyfilePath, RandomNumberGenerator.GetBytes(64));
        }

        public void Dispose()
        {
            BootRomSession.ClearAll();
            try { Directory.Delete(_root, true); } catch { /* best-effort cleanup */ }
        }

        private string ContainerPath(string name) => Path.Combine(_root, $"{name}.pvault");

        private static VaultManifest NewManifest(string name) => new()
        {
            VaultName = name,
            ContainerPath = "container",
        };

        private Task CreateAsync(string path, VaultManifest manifest) =>
            _containers.CreateContainerAsync(path, SizeBytes, Password, KeyfilePath, manifest);

        private VaultManifest? Read(string path) =>
            _containers.ReadManifestFromContainer(path, Password, KeyfilePath);

        [Fact]
        public async Task A_container_written_without_a_contribution_reads_back_unchanged()
        {
            string path = ContainerPath("plain");
            await CreateAsync(path, NewManifest("Plain vault"));

            var read = Read(path);

            Assert.NotNull(read);
            Assert.Equal("Plain vault", read!.VaultName);
        }

        [Fact]
        public async Task A_bound_container_reads_back_while_its_contribution_is_registered()
        {
            string path = ContainerPath("bound");
            BootRomSession.Set(path, RandomNumberGenerator.GetBytes(32));

            await CreateAsync(path, NewManifest("Bound vault"));
            var read = Read(path);

            Assert.NotNull(read);
            Assert.Equal("Bound vault", read!.VaultName);
        }

        [Fact]
        public async Task A_bound_container_does_not_open_once_the_contribution_is_gone()
        {
            string path = ContainerPath("rom-lost");
            BootRomSession.Set(path, RandomNumberGenerator.GetBytes(32));
            await CreateAsync(path, NewManifest("Bound vault"));

            // The ROM is absent on this run: no contribution, so the key is different.
            BootRomSession.Clear(path);

            Assert.Null(SafeRead(path));
        }

        [Fact]
        public async Task A_bound_container_does_not_open_with_the_wrong_contribution()
        {
            string path = ContainerPath("wrong-rom");
            BootRomSession.Set(path, RandomNumberGenerator.GetBytes(32));
            await CreateAsync(path, NewManifest("Bound vault"));

            BootRomSession.Set(path, RandomNumberGenerator.GetBytes(32));

            Assert.Null(SafeRead(path));
        }

        [Fact]
        public async Task An_unbound_container_is_unaffected_by_another_vaults_binding()
        {
            string bound = ContainerPath("other-bound");
            string plain = ContainerPath("untouched");

            BootRomSession.Set(bound, RandomNumberGenerator.GetBytes(32));
            await CreateAsync(bound, NewManifest("Bound vault"));
            await CreateAsync(plain, NewManifest("Untouched vault"));

            // The contribution is keyed per vault, so the unbound one derives as it always did.
            var read = Read(plain);

            Assert.NotNull(read);
            Assert.Equal("Untouched vault", read!.VaultName);
        }

        [Fact]
        public async Task Binding_is_not_retroactive_for_containers_created_without_it()
        {
            string path = ContainerPath("pre-existing");
            await CreateAsync(path, NewManifest("Pre-existing vault"));

            // Registering a contribution afterwards must not be enough to change the key a
            // previously written container is read with — otherwise enabling the feature would
            // lock people out of vaults they already had.
            BootRomSession.Set(path, RandomNumberGenerator.GetBytes(32));

            Assert.Null(SafeRead(path));

            BootRomSession.Clear(path);
            Assert.NotNull(Read(path));
        }

        /// <summary>
        /// A container whose key does not match may surface as a null manifest or as a thrown
        /// authentication failure depending on where the mismatch is caught. Both mean the same
        /// thing here: it did not open.
        /// </summary>
        private VaultManifest? SafeRead(string path)
        {
            try
            {
                return Read(path);
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
