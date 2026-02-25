using System;
using System.IO;
using System.Security;
using System.Text.Json;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class ManifestServiceTests
    {
        [Fact]
        public void TryReadManifest_ReturnsFalse_ForMissingFile()
        {
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            if (File.Exists(path)) File.Delete(path);

            bool ok = svc.TryReadManifest(path, "password", null, out var manifest, out var error);

            Assert.False(ok);
            Assert.Null(manifest);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Contains("Manifest file not found", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryReadManifest_ReturnsFalse_ForMalformedJson()
        {
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            File.WriteAllText(path, "this is not valid json");

            try
            {
                bool ok = svc.TryReadManifest(path, "password", null, out var manifest, out var error);

                Assert.False(ok);
                Assert.Null(manifest);
                Assert.False(string.IsNullOrEmpty(error));
                Assert.Contains("malformed", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void WriteManifest_RespectsPreconfiguredSalt()
        {
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "TestVault",
                    ContainerPath = "vaults/primary.pvault",
                    ContainerSizeBytes = 1024
                };

                var presetSalt = new byte[] { 0x2A, 0x5C, 0x11, 0x9F, 0x88, 0x37, 0x4B, 0x0D, 0x71, 0xEF, 0xC4, 0x55, 0x66, 0x90, 0xAB, 0xCD };
                manifest.SaltBase64 = Convert.ToBase64String(presetSalt);

                svc.WriteManifest(manifest, path, "phase2-passphrase", null);

                string payloadJson = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(payloadJson);
                string storedSalt = doc.RootElement.GetProperty("salt").GetString()!;

                Assert.Equal(manifest.SaltBase64, storedSalt);

                var roundTrip = svc.ReadManifest(path, "phase2-passphrase", null);
                Assert.Equal(manifest.SaltBase64, roundTrip.SaltBase64);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        #region USB Serial Binding Tests

        [Fact]
        public void ReadManifest_ThrowsSecurityException_WhenUsbBoundButNotProvided()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            string usbSerial = "USB-1234-ABCD";

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "UsbBoundVault",
                    ContainerPath = "vaults/usb.pvault"
                };

                // Write manifest bound to USB
                svc.WriteManifest(manifest, path, "password", null, usbSerial);

                // Act & Assert - Try to read without USB serial
                var ex = Assert.Throws<SecurityException>(() => 
                    svc.ReadManifest(path, "password", null, null));

                Assert.Contains("USB device must be connected", ex.Message);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void ReadManifest_ThrowsSecurityException_WhenUsbSerialMismatch()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            string originalSerial = "USB-ORIGINAL";
            string wrongSerial = "USB-WRONG";

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "UsbBoundVault",
                    ContainerPath = "vaults/usb.pvault"
                };

                // Write manifest bound to one USB
                svc.WriteManifest(manifest, path, "password", null, originalSerial);

                // Act & Assert - Try to read with different USB serial
                var ex = Assert.Throws<SecurityException>(() => 
                    svc.ReadManifest(path, "password", null, wrongSerial));

                Assert.Contains("USB serial mismatch", ex.Message);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void ReadManifest_Succeeds_WhenUsbSerialMatches()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            string usbSerial = "USB-CORRECT-SERIAL";

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "UsbBoundVault",
                    ContainerPath = "vaults/usb.pvault"
                };

                // Write manifest bound to USB
                svc.WriteManifest(manifest, path, "password", null, usbSerial);

                // Act - Read with matching USB serial
                var result = svc.ReadManifest(path, "password", null, usbSerial);

                // Assert
                Assert.NotNull(result);
                Assert.Equal("UsbBoundVault", result.VaultName);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        #endregion

        #region Integrity Signature Tests

        [Fact]
        public void WriteManifest_AddsIntegritySignature()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "SignedVault",
                    ContainerPath = "vaults/signed.pvault"
                };

                // Act
                svc.WriteManifest(manifest, path, "password", null);
                var result = svc.ReadManifest(path, "password", null);

                // Assert - Signature should be present
                Assert.NotNull(result.IntegritySignatureBase64);
                Assert.NotNull(result.SignatureTimestamp);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void ComputeIntegritySignature_IsDeterministic()
        {
            // Arrange
            var manifest = new VaultManifest
            {
                VaultName = "DeterministicTest",
                ContainerPath = "vaults/deterministic.pvault",
                SaltBase64 = Convert.ToBase64String(new byte[16]),
                DeviceId = "device-123"
            };
            byte[] key = new byte[32];
            new Random(42).NextBytes(key);

            // Act
            string sig1 = ManifestService.ComputeIntegritySignature(manifest, key);
            string sig2 = ManifestService.ComputeIntegritySignature(manifest, key);

            // Assert
            Assert.Equal(sig1, sig2);
        }

        [Fact]
        public void VerifyIntegritySignature_ThrowsOnTampering()
        {
            // Arrange
            byte[] key = new byte[32];
            new Random(42).NextBytes(key);

            var manifest = new VaultManifest
            {
                VaultName = "TamperTest",
                ContainerPath = "vaults/tamper.pvault",
                SaltBase64 = Convert.ToBase64String(new byte[16])
            };

            ManifestService.SignManifest(manifest, key);
            
            // Act - Tamper with a critical field
            manifest.ContainerPath = "vaults/TAMPERED.pvault";

            // Assert
            var ex = Assert.Throws<SecurityException>(() => 
                ManifestService.VerifyIntegritySignature(manifest, key));
            
            Assert.Contains("tampered", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VerifyIntegritySignature_AllowsMissingSignature_WhenNotRequired()
        {
            // Arrange
            byte[] key = new byte[32];
            new Random(42).NextBytes(key);

            var manifest = new VaultManifest
            {
                VaultName = "NoSigTest",
                ContainerPath = "vaults/nosig.pvault"
            };
            // Don't sign the manifest

            // Act & Assert - Should not throw when requireSignature is false
            bool result = ManifestService.VerifyIntegritySignature(manifest, key, requireSignature: false);
            Assert.True(result);
        }

        [Fact]
        public void VerifyIntegritySignature_Throws_WhenSignatureRequiredButMissing()
        {
            // Arrange
            byte[] key = new byte[32];
            new Random(42).NextBytes(key);

            var manifest = new VaultManifest
            {
                VaultName = "RequireSigTest",
                ContainerPath = "vaults/requiresig.pvault"
            };
            // Don't sign the manifest

            // Act & Assert
            var ex = Assert.Throws<SecurityException>(() => 
                ManifestService.VerifyIntegritySignature(manifest, key, requireSignature: true));
            
            Assert.Contains("required but missing", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Keyfile Tests

        [Fact]
        public void WriteReadManifest_WorksWithKeyfileOnly()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string manifestPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            string keyfilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".key");

            try
            {
                // Create keyfile
                File.WriteAllBytes(keyfilePath, new byte[64]);

                var manifest = new VaultManifest
                {
                    VaultName = "KeyfileOnlyVault",
                    ContainerPath = "vaults/keyfile.pvault"
                };

                // Act - Write with keyfile only (null password)
                svc.WriteManifest(manifest, manifestPath, null, keyfilePath);
                var result = svc.ReadManifest(manifestPath, null, keyfilePath);

                // Assert
                Assert.NotNull(result);
                Assert.Equal("KeyfileOnlyVault", result.VaultName);
            }
            finally
            {
                try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
                try { if (File.Exists(keyfilePath)) File.Delete(keyfilePath); } catch { }
            }
        }

        [Fact]
        public void WriteReadManifest_WorksWithDualFactor()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string manifestPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");
            string keyfilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".key");

            try
            {
                // Create keyfile
                File.WriteAllBytes(keyfilePath, new byte[64]);

                var manifest = new VaultManifest
                {
                    VaultName = "DualFactorVault",
                    ContainerPath = "vaults/dualfactor.pvault"
                };

                // Act - Write with both password and keyfile (dual-factor)
                svc.WriteManifest(manifest, manifestPath, "password123", keyfilePath, null, requireDualFactor: true);
                var result = svc.ReadManifest(manifestPath, "password123", keyfilePath, null, requireDualFactor: true);

                // Assert
                Assert.NotNull(result);
                Assert.Equal("DualFactorVault", result.VaultName);
            }
            finally
            {
                try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
                try { if (File.Exists(keyfilePath)) File.Delete(keyfilePath); } catch { }
            }
        }

        [Fact]
        public void ReadManifest_ThrowsArgumentException_WhenDualFactorRequiredButKeyfileMissing()
        {
            // Arrange
            var enc = new EncryptionService();
            var svc = new ManifestService(enc);
            string manifestPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".manifest");

            try
            {
                var manifest = new VaultManifest
                {
                    VaultName = "DualFactorTest",
                    ContainerPath = "vaults/dualfactor.pvault"
                };

                // Write without dual-factor requirement
                svc.WriteManifest(manifest, manifestPath, "password", null);

                // Act & Assert - Try to read with dual-factor but no keyfile
                var ex = Assert.Throws<ArgumentException>(() => 
                    svc.ReadManifest(manifestPath, "password", null, null, requireDualFactor: true));

                Assert.Contains("BOTH", ex.Message);
            }
            finally
            {
                try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
            }
        }

        #endregion
    }
}
