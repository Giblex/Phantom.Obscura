using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using GiblexVault.Security.ZK.Keys;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.Security;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    /// <summary>
    /// Phase 3: no-camera QR pairing. A new device emits a request, the owner
    /// approves (wrapping the vault key + re-signing the manifest), and the new
    /// device accepts the grant to recover the exact vault key.
    /// </summary>
    public sealed class DeviceEnrollmentServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public DeviceEnrollmentServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PhantomObscuraEnrollTests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private DeviceEnrollmentService NewEnrollment(string subdir, out IDeviceIdentityService identity)
        {
            identity = new DeviceIdentityService(Path.Combine(_tempDir, subdir));
            return new DeviceEnrollmentService(identity);
        }

        private static VaultManifest NewManifest() => new()
        {
            ContainerPath = "G:/vault/obscura.vol",
            SaltBase64 = Convert.ToBase64String(new byte[16]),
            DeviceId = Guid.NewGuid().ToString("N"),
            Algorithm = "AES-256-GCM",
            Version = 2,
        };

        [Fact]
        public void Request_Encode_Decode_RoundTrips()
        {
            var svc = NewEnrollment("dev", out _);
            var request = svc.CreateEnrollmentRequest("New Laptop");

            string payload = svc.EncodeRequest(request);
            var decoded = svc.DecodeRequest(payload);

            Assert.Equal(request.DeviceId, decoded.DeviceId);
            Assert.Equal(request.SigningPublicKeyBase64, decoded.SigningPublicKeyBase64);
            Assert.Equal(request.AgreementPublicKeyBase64, decoded.AgreementPublicKeyBase64);
            Assert.Equal("New Laptop", decoded.FriendlyName);
        }

        [Fact]
        public void DecodeRequest_RejectsWrongPrefix()
        {
            var svc = NewEnrollment("dev", out _);
            Assert.Throws<FormatException>(() => svc.DecodeRequest("POGRT1.abc"));
        }

        [Fact]
        public void FullPairing_NewDeviceRecoversVaultKey()
        {
            // New device builds + serializes its request.
            var newDeviceSvc = NewEnrollment("newdevice", out var newDeviceIdentity);
            var request = newDeviceSvc.CreateEnrollmentRequest("New Phone");
            string requestPayload = newDeviceSvc.EncodeRequest(request);

            // Owner device parses request and approves against the vault.
            var ownerSvc = NewEnrollment("owner", out _);
            var parsed = ownerSvc.DecodeRequest(requestPayload);

            var (ownerPub, ownerPriv) = UserKeys.Generate();
            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            var manifest = NewManifest();

            DeviceEnrollmentGrant grant = ownerSvc.ApproveEnrollment(
                parsed, vaultKey, manifest, ownerPriv, ownerPub);

            // Manifest now trusts the new device and is Ed25519-signed by the owner.
            Assert.Single(manifest.TrustedDevices);
            var trusted = manifest.TrustedDevices[0];
            Assert.Equal(request.DeviceId, trusted.DeviceId);
            Assert.Equal(DeviceRole.Member, trusted.Role);
            Assert.False(string.IsNullOrEmpty(trusted.WrappedVaultKeyBase64));
            Assert.True(ManifestService.VerifyManifestEd25519(manifest, requireSignature: true));

            // Grant travels back; new device unwraps and recovers the exact vault key.
            string grantPayload = ownerSvc.EncodeGrant(grant);
            var grantParsed = newDeviceSvc.DecodeGrant(grantPayload);
            byte[] recovered = newDeviceSvc.AcceptGrant(grantParsed);

            Assert.Equal(vaultKey, recovered);
        }

        [Fact]
        public void AcceptGrant_FailsOnDifferentDevice()
        {
            var newDeviceSvc = NewEnrollment("nd", out _);
            var request = newDeviceSvc.CreateEnrollmentRequest("Target");

            var ownerSvc = NewEnrollment("ow", out _);
            var (ownerPub, ownerPriv) = UserKeys.Generate();
            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            var manifest = NewManifest();
            var grant = ownerSvc.ApproveEnrollment(request, vaultKey, manifest, ownerPriv, ownerPub);

            // A different device cannot accept a grant wrapped to the target device.
            var attackerSvc = NewEnrollment("att", out _);
            Assert.ThrowsAny<CryptographicException>(() => attackerSvc.AcceptGrant(grant));
        }

        [Fact]
        public void ApproveEnrollment_IsIdempotentForSameDevice()
        {
            var newDeviceSvc = NewEnrollment("nd2", out _);
            var request = newDeviceSvc.CreateEnrollmentRequest("Repeat");

            var ownerSvc = NewEnrollment("ow2", out _);
            var (ownerPub, ownerPriv) = UserKeys.Generate();
            byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
            var manifest = NewManifest();

            ownerSvc.ApproveEnrollment(request, vaultKey, manifest, ownerPriv, ownerPub);
            ownerSvc.ApproveEnrollment(request, vaultKey, manifest, ownerPriv, ownerPub);

            // Re-approving updates the existing entry rather than duplicating it.
            Assert.Single(manifest.TrustedDevices);
        }

        [Fact]
        public void ApproveEnrollment_RejectsIncompleteRequest()
        {
            var ownerSvc = NewEnrollment("ow3", out _);
            var (ownerPub, ownerPriv) = UserKeys.Generate();
            var manifest = NewManifest();
            var bad = new DeviceEnrollmentRequest { DeviceId = "x" }; // missing keys

            Assert.Throws<SecurityException>(() =>
                ownerSvc.ApproveEnrollment(bad, new byte[32], manifest, ownerPriv, ownerPub));
        }
    }
}
