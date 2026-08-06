using PhantomVault.Core.Models;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Drives the no-camera QR pairing flow. A new device emits an enrollment
    /// request (public keys only); a trusted owner device approves it, which wraps
    /// the vault key to the new device, records it in the manifest, and re-signs
    /// the manifest. The new device then accepts the returned grant to recover the
    /// vault key.
    /// </summary>
    public interface IDeviceEnrollmentService
    {
        /// <summary>Builds the local device's enrollment request from its identity.</summary>
        DeviceEnrollmentRequest CreateEnrollmentRequest(string? friendlyName = null);

        /// <summary>Serializes a request to a compact, QR-friendly transfer string.</summary>
        string EncodeRequest(DeviceEnrollmentRequest request);

        /// <summary>Parses a transfer string back into an enrollment request.</summary>
        DeviceEnrollmentRequest DecodeRequest(string payload);

        /// <summary>Serializes a grant to a compact, QR-friendly transfer string.</summary>
        string EncodeGrant(DeviceEnrollmentGrant grant);

        /// <summary>Parses a transfer string back into an enrollment grant.</summary>
        DeviceEnrollmentGrant DecodeGrant(string payload);

        /// <summary>
        /// Authorizes the requesting device against a vault: wraps the vault key to
        /// it, records it in <paramref name="manifest"/>.TrustedDevices, re-signs the
        /// manifest with the owner's Ed25519 key, and returns the grant to hand back.
        /// </summary>
        DeviceEnrollmentGrant ApproveEnrollment(
            DeviceEnrollmentRequest request,
            byte[] vaultKey,
            VaultManifest manifest,
            byte[] ownerSigningPrivateKey,
            byte[] ownerSigningPublicKey,
            DeviceRole grantedRole = DeviceRole.Member);

        /// <summary>
        /// On the newly authorized device, unwraps the vault key carried by a grant
        /// using the local device's sealed agreement key. Returns the raw vault key.
        /// </summary>
        byte[] AcceptGrant(DeviceEnrollmentGrant grant);
    }
}
