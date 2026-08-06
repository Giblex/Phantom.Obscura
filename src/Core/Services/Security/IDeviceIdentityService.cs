using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Owns this device's cryptographic identity (Ed25519 signing + X25519 key
    /// agreement). Private keys are generated once, sealed at rest, and never
    /// leave the device. The public keys form the device identity placed into a
    /// vault manifest's trusted-device list.
    /// </summary>
    public interface IDeviceIdentityService
    {
        /// <summary>
        /// Returns this device's public identity, generating and sealing a new
        /// keypair set on first use.
        /// </summary>
        DeviceIdentity GetOrCreateLocalIdentity(string? friendlyName = null);

        /// <summary>Signs a message with this device's Ed25519 signing key.</summary>
        byte[] Sign(byte[] message);

        /// <summary>Verifies an Ed25519 signature against a device signing public key (base64).</summary>
        bool Verify(byte[] message, byte[] signature, string signingPublicKeyBase64);

        /// <summary>
        /// Wraps a 32-byte vault key to a target device's X25519 public key so only
        /// that device can later unwrap it.
        /// </summary>
        byte[] WrapVaultKeyToDevice(byte[] vaultKey, string targetAgreementPublicKeyBase64);

        /// <summary>Unwraps a vault key that was wrapped to this device.</summary>
        byte[] UnwrapVaultKey(byte[] wrappedVaultKey);
    }
}
