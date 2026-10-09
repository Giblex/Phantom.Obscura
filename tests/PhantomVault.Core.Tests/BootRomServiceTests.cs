using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using PhantomVault.Core.Services.BootRom;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Boot ROM provisioning and execution end to end: the ROM releases its contribution on the
    /// device it was provisioned for, and withholds it everywhere else.
    /// </summary>
    public sealed class BootRomServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PhantomVault_Rom_{Guid.NewGuid():N}");
        private readonly BootRomService _service = new();

        private string UsbRoot => Path.Combine(_root, "usb");
        private string KeyfilePath => Path.Combine(_root, "vault.key");

        private readonly byte[] _integrity = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] _binding = RandomNumberGenerator.GetBytes(32);

        public BootRomServiceTests()
        {
            Directory.CreateDirectory(UsbRoot);
            File.WriteAllBytes(KeyfilePath, RandomNumberGenerator.GetBytes(64));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { /* best-effort cleanup */ }
        }

        private BootRomProvisionResult Provision(ulong version = 1)
            => new BootRomProvisioner().Provision(UsbRoot, KeyfilePath, _integrity, _binding, version);

        [Fact]
        public void An_unprovisioned_drive_is_not_bound()
        {
            Assert.False(BootRomService.IsBound(UsbRoot));
            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);
            Assert.Equal(BootRomStatus.NotBound, outcome.Status);
        }

        [Fact]
        public void A_provisioned_rom_releases_its_contribution_on_the_right_device()
        {
            var provisioned = Provision();

            Assert.True(BootRomService.IsBound(UsbRoot));
            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(BootRomStatus.Success, outcome.Status);
            Assert.True(outcome.IsSuccess);
            Assert.Equal(provisioned.Contribution, outcome.Contribution);
        }

        [Fact]
        public void The_contribution_is_stable_across_runs()
        {
            // The vault key depends on it, so it must not vary run to run.
            Provision();
            var first = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);
            var second = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(first.Contribution, second.Contribution);
        }

        [Fact]
        public void The_rom_refuses_when_the_binding_does_not_match()
        {
            Provision();

            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, RandomNumberGenerator.GetBytes(32));

            Assert.Equal(BootRomStatus.RomRefused, outcome.Status);
            Assert.Null(outcome.Contribution);
            Assert.Equal(BootRomProvisioner.RefusedVerdict, outcome.Verdict);
        }

        [Fact]
        public void The_rom_refuses_when_integrity_does_not_match()
        {
            Provision();

            var outcome = _service.Run(UsbRoot, KeyfilePath, RandomNumberGenerator.GetBytes(32), _binding);

            Assert.Equal(BootRomStatus.RomRefused, outcome.Status);
            Assert.Null(outcome.Contribution);
        }

        [Fact]
        public void A_cloned_drive_without_the_host_key_material_cannot_unseal_the_rom()
        {
            // The whole point of deriving the ROM key from keyfile material: copying the stick
            // copies the sealed image, but not the half of the key that never left the machine.
            Provision();

            string otherKeyfile = Path.Combine(_root, "other.key");
            File.WriteAllBytes(otherKeyfile, RandomNumberGenerator.GetBytes(64));

            var outcome = _service.Run(UsbRoot, otherKeyfile, _integrity, _binding);

            Assert.Equal(BootRomStatus.RomRejected, outcome.Status);
            Assert.Null(outcome.Contribution);
        }

        [Fact]
        public void A_missing_image_is_reported_rather_than_ignored()
        {
            Provision();
            File.Delete(BootRomMarker.RomPath(UsbRoot));

            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(BootRomStatus.RomMissing, outcome.Status);
        }

        [Fact]
        public void A_rolled_back_image_is_blocked()
        {
            Provision(version: 2);

            // Raise the floor above the image, as a withdrawal would.
            var marker = BootRomMarker.TryLoad(UsbRoot)!;
            marker.MinRomVersion = 5;
            marker.Save(UsbRoot);

            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(BootRomStatus.RollbackBlocked, outcome.Status);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(135)]
        [InlineData(PhantomRomContainer.MaxContainerBytes + 1)]
        [InlineData(PhantomRomContainer.MaxContainerBytes * 2)]
        public void An_invalid_image_size_is_rejected_before_key_derivation(long length)
        {
            Provision();
            string romPath = BootRomMarker.RomPath(UsbRoot);
            using (var image = File.Open(romPath, FileMode.Open, FileAccess.Write))
                image.SetLength(length);

            var outcome = _service.Run(UsbRoot, Path.Combine(_root, "missing.key"), _integrity, _binding);

            Assert.Equal(BootRomStatus.RomRejected, outcome.Status);
            Assert.Null(outcome.Contribution);
            Assert.Equal("The Boot ROM image size is outside the supported range.", outcome.Message);
            using var exclusive = File.Open(romPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        [Theory]
        [InlineData("keyId")]
        [InlineData("salt")]
        [InlineData("signingKey")]
        public void Incorrect_marker_parameter_lengths_are_rejected(string parameter)
        {
            Provision();
            var marker = BootRomMarker.TryLoad(UsbRoot)!;
            string invalid = Convert.ToBase64String(new byte[1]);
            if (parameter == "keyId") marker.KeyIdBase64 = invalid;
            if (parameter == "salt") marker.SaltBase64 = invalid;
            if (parameter == "signingKey") marker.SigningPublicKeyBase64 = invalid;
            marker.Save(UsbRoot);

            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(BootRomStatus.RomRejected, outcome.Status);
            Assert.Null(outcome.Contribution);
        }

        [Fact]
        public void A_valid_image_at_the_loading_limit_still_runs()
        {
            var provisioned = Provision();
            var marker = provisioned.Marker;
            byte[] romKey = BootRomKeyDerivation.DeriveRomKey(KeyfilePath,
                Convert.FromBase64String(marker.SaltBase64), Convert.FromBase64String(marker.KeyIdBase64));
            byte[] signingPrivate = RandomNumberGenerator.GetBytes(32);
            byte[] program = PhantomRomContainer.Open(File.ReadAllBytes(BootRomMarker.RomPath(UsbRoot)),
                Convert.FromBase64String(marker.SigningPublicKeyBase64), romKey, out _);
            byte[] padded = new byte[PhantomRomContainer.MaxProgramBytes];
            BootRomOutcome outcome = null;
            try
            {
                program.CopyTo(padded, 0);
                marker.SigningPublicKeyBase64 = Convert.ToBase64String(
                    new Ed25519PrivateKeyParameters(signingPrivate, 0).GeneratePublicKey().GetEncoded());
                marker.Save(UsbRoot);
                byte[] container = PhantomRomContainer.Seal(padded, signingPrivate, romKey,
                    Convert.FromBase64String(marker.KeyIdBase64), marker.MinRomVersion);
                Assert.Equal(PhantomRomContainer.MaxContainerBytes, container.Length);
                File.WriteAllBytes(BootRomMarker.RomPath(UsbRoot), container);

                outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

                Assert.Equal(BootRomStatus.Success, outcome.Status);
                Assert.Equal(provisioned.Contribution, outcome.Contribution);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(romKey);
                CryptographicOperations.ZeroMemory(signingPrivate);
                CryptographicOperations.ZeroMemory(program);
                CryptographicOperations.ZeroMemory(padded);
                CryptographicOperations.ZeroMemory(provisioned.Contribution);
                if (outcome?.Contribution is not null) CryptographicOperations.ZeroMemory(outcome.Contribution);
            }
        }

        [Fact]
        public void A_corrupted_image_is_rejected()
        {
            Provision();

            string romPath = BootRomMarker.RomPath(UsbRoot);
            byte[] container = File.ReadAllBytes(romPath);
            container[PhantomRomContainer.HeaderSize + 4] ^= 0xFF;
            File.WriteAllBytes(romPath, container);

            var outcome = _service.Run(UsbRoot, KeyfilePath, _integrity, _binding);

            Assert.Equal(BootRomStatus.RomRejected, outcome.Status);
        }

        [Fact]
        public void The_recovery_code_recovers_the_contribution_when_the_rom_is_gone()
        {
            var provisioned = Provision();
            byte[] escrow = File.ReadAllBytes(BootRomMarker.EscrowPath(UsbRoot));
            byte[] escrowSalt = Convert.FromBase64String(provisioned.Marker.EscrowSaltBase64);

            byte[] recovered = BootRomKeyDerivation.UnwrapContributionForRecovery(
                escrow, provisioned.RecoveryCode, escrowSalt);

            Assert.Equal(provisioned.Contribution, recovered);
        }

        [Fact]
        public void Recovery_code_formatting_does_not_matter()
        {
            var provisioned = Provision();
            byte[] escrow = File.ReadAllBytes(BootRomMarker.EscrowPath(UsbRoot));
            byte[] escrowSalt = Convert.FromBase64String(provisioned.Marker.EscrowSaltBase64);

            string messy = provisioned.RecoveryCode.Replace("-", " ").ToLowerInvariant();

            Assert.Equal(
                provisioned.Contribution,
                BootRomKeyDerivation.UnwrapContributionForRecovery(escrow, messy, escrowSalt));
        }

        [Fact]
        public void A_wrong_recovery_code_is_rejected()
        {
            var provisioned = Provision();
            byte[] escrow = File.ReadAllBytes(BootRomMarker.EscrowPath(UsbRoot));
            byte[] escrowSalt = Convert.FromBase64String(provisioned.Marker.EscrowSaltBase64);

            Assert.Throws<SecurityException>(() =>
                BootRomKeyDerivation.UnwrapContributionForRecovery(
                    escrow, BootRomKeyDerivation.GenerateRecoveryCode(), escrowSalt));
        }

        [Fact]
        public void Removing_binding_leaves_the_drive_unbound()
        {
            Provision();
            BootRomProvisioner.Remove(UsbRoot);

            Assert.False(BootRomService.IsBound(UsbRoot));
            Assert.Equal(BootRomStatus.NotBound, _service.Run(UsbRoot, KeyfilePath, _integrity, _binding).Status);
        }

        [Fact]
        public void Binding_checks_can_be_skipped_when_a_module_is_unavailable()
        {
            // Provisioned with no integrity expectation: the helper may not be installed, and a
            // ROM that refuses forever would be worse than one that checks less.
            new BootRomProvisioner().Provision(UsbRoot, KeyfilePath, expectedIntegrity: null, expectedBinding: _binding);

            var outcome = _service.Run(UsbRoot, KeyfilePath, RandomNumberGenerator.GetBytes(32), _binding);

            Assert.Equal(BootRomStatus.Success, outcome.Status);
        }
    }
}
