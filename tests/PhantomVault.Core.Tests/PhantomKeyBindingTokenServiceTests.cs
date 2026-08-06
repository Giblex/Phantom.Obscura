#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class PhantomKeyBindingTokenServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public PhantomKeyBindingTokenServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "phantomkey-binding-token-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [Fact]
        public void CreateOrRotate_Validate_RoundTrips()
        {
            var service = new PhantomKeyBindingTokenService();
            var manifest = CreateManifest();
            string keyfilePath = CreateKeyfile("vault.key");

            service.CreateOrRotate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-1");

            var result = service.Validate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-1");

            Assert.True(result.IsValid, result.Message);
            Assert.Equal(PhantomKeyBindingValidationFailure.None, result.Failure);
            Assert.NotNull(result.Payload);
            Assert.True(File.Exists(PhantomDeviceLayout.GetPhantomKeyBindingTokenPath(_tempRoot)));
        }

        [Fact]
        public void Validate_TamperedToken_FailsClosed()
        {
            var service = new PhantomKeyBindingTokenService();
            var manifest = CreateManifest();
            string keyfilePath = CreateKeyfile("vault.key");

            service.CreateOrRotate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-1");
            string tokenPath = PhantomDeviceLayout.GetPhantomKeyBindingTokenPath(_tempRoot);
            File.SetAttributes(tokenPath, FileAttributes.Normal);
            string tokenJson = File.ReadAllText(tokenPath);
            File.WriteAllText(tokenPath, tokenJson.Replace("AES-256-GCM", "AES-128-GCM", StringComparison.Ordinal));

            var result = service.Validate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-1");

            Assert.False(result.IsValid);
            Assert.Equal(PhantomKeyBindingValidationFailure.TokenInvalid, result.Failure);
        }

        [Fact]
        public void Validate_DifferentComputer_FailsWithComputerMismatch()
        {
            var service = new PhantomKeyBindingTokenService();
            var manifest = CreateManifest();
            string keyfilePath = CreateKeyfile("vault.key");

            service.CreateOrRotate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-1");

            var result = service.Validate(_tempRoot, manifest, "correct horse", keyfilePath, "computer-2");

            Assert.False(result.IsValid);
            Assert.Equal(PhantomKeyBindingValidationFailure.ComputerMismatch, result.Failure);
        }

        [Fact]
        public void Validate_DifferentKeyfileName_FailsWithKeyfileMismatch()
        {
            var service = new PhantomKeyBindingTokenService();
            var manifest = CreateManifest();
            string originalKeyfilePath = CreateKeyfile("vault.key");
            string copiedKeyfilePath = Path.Combine(_tempRoot, "copied.key");
            File.Copy(originalKeyfilePath, copiedKeyfilePath);

            service.CreateOrRotate(_tempRoot, manifest, "correct horse", originalKeyfilePath, "computer-1");

            var result = service.Validate(_tempRoot, manifest, "correct horse", copiedKeyfilePath, "computer-1");

            Assert.False(result.IsValid);
            Assert.Equal(PhantomKeyBindingValidationFailure.KeyfileMismatch, result.Failure);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                foreach (var file in Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        private string CreateKeyfile(string fileName)
        {
            string keyfilePath = Path.Combine(_tempRoot, fileName);
            File.WriteAllBytes(keyfilePath, RandomNumberGenerator.GetBytes(64));
            return keyfilePath;
        }

        private static VaultManifest CreateManifest()
        {
            return new VaultManifest
            {
                VaultName = "Token Test Vault",
                Guuid = Guid.NewGuid().ToString("N"),
                DeviceId = "device-1",
                UsbBindingId = null,
                UsbBindingGuid = Guid.NewGuid().ToString("N"),
                SaltBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ManifestSequence = 7,
                KeyRotationCount = 2
            };
        }
    }
}
