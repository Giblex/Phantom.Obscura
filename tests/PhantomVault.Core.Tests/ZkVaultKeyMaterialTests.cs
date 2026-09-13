using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Util;
using PhantomVault.Core.Services.ZeroKnowledge;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// An existing vault's pepper, salt and pepper pointer must never be replaced when they
    /// cannot be read. Each of these used to be silently regenerated, which changed the derived
    /// master key and ran the unlock as a first-time bootstrap.
    ///
    /// Every test runs against an isolated directory through the internal constructor, so the
    /// user's real %APPDATA%\PhantomVault is never touched.
    /// </summary>
    public sealed class ZkVaultKeyMaterialTests : IDisposable
    {
        private const string Password = "correct horse battery staple";

        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"PhantomVault_KeyMaterial_{Guid.NewGuid():N}");
        private readonly string _keyfile;

        public ZkVaultKeyMaterialTests()
        {
            Directory.CreateDirectory(_dir);
            _keyfile = Path.Combine(_dir, "test.key");
            File.WriteAllBytes(_keyfile, RandomNumberGenerator.GetBytes(64));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
        }

        private string PointerPath => Path.Combine(_dir, "pepper.ref");
        private string SaltPath => Path.Combine(_dir, "master.salt");
        private string VerifierPath => Path.Combine(_dir, "master.verifier.json");

        [Fact]
        public async Task FirstRun_CreatesKeyMaterial_AndTheVaultReopens()
        {
            using (var first = new ZkVaultService(null, _dir))
            {
                Assert.True(await first.UnlockMasterKeyAsync(Password, _keyfile));
            }

            Assert.True(File.Exists(PointerPath));
            Assert.True(File.Exists(SaltPath));

            using var second = new ZkVaultService(null, _dir);
            Assert.True(await second.UnlockMasterKeyAsync(Password, _keyfile));
        }

        [Fact]
        public async Task UnsealablePepper_FailsUnlock_AndIsNotOverwritten()
        {
            const string pepperName = "0123456789abcdef.dat";
            File.WriteAllText(PointerPath, pepperName);
            var pepperPath = Path.Combine(_dir, pepperName);
            var garbage = RandomNumberGenerator.GetBytes(128);
            File.WriteAllBytes(pepperPath, garbage);

            using var service = new ZkVaultService(null, _dir);

            await Assert.ThrowsAnyAsync<Exception>(() => service.UnlockMasterKeyAsync(Password, _keyfile));

            Assert.False(service.IsUnlocked);
            Assert.Equal(garbage, File.ReadAllBytes(pepperPath));
            Assert.Equal(pepperName, File.ReadAllText(PointerPath));
            Assert.False(File.Exists(VerifierPath));
        }

        [Fact]
        public async Task UnreadableSalt_FailsUnlock_AndIsNotOverwritten()
        {
            // Denying read while leaving write allowed is the case that distinguishes the fix: the
            // old code caught the failed read and then successfully overwrote the file.
            if (!OperatingSystem.IsWindows()) return;

            const string pepperName = "fedcba9876543210.dat";
            File.WriteAllText(PointerPath, pepperName);
            File.WriteAllBytes(Path.Combine(_dir, pepperName), SecurityTuning.CreatePepperProtected());
            var salt = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(SaltPath, salt);

            var saltFile = new FileInfo(SaltPath);
            var denyRead = new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ReadData,
                AccessControlType.Deny);

            var acl = saltFile.GetAccessControl();
            acl.AddAccessRule(denyRead);
            saltFile.SetAccessControl(acl);
            try
            {
                using var service = new ZkVaultService(null, _dir);
                await Assert.ThrowsAnyAsync<Exception>(() => service.UnlockMasterKeyAsync(Password, _keyfile));
                Assert.False(service.IsUnlocked);
            }
            finally
            {
                acl = saltFile.GetAccessControl();
                acl.RemoveAccessRule(denyRead);
                saltFile.SetAccessControl(acl);
            }

            Assert.Equal(salt, File.ReadAllBytes(SaltPath));
            Assert.False(File.Exists(VerifierPath));
        }

        [Fact]
        public async Task InvalidPepperPointer_FailsUnlock_AndIsNotRewritten()
        {
            const string invalidPointer = "..\\escape.dat";
            File.WriteAllText(PointerPath, invalidPointer);

            using var service = new ZkVaultService(null, _dir);

            await Assert.ThrowsAnyAsync<Exception>(() => service.UnlockMasterKeyAsync(Password, _keyfile));

            Assert.False(service.IsUnlocked);
            Assert.Equal(invalidPointer, File.ReadAllText(PointerPath));
            Assert.Empty(Directory.GetFiles(_dir, "*.dat"));
            Assert.False(File.Exists(VerifierPath));
        }
    }
}
