using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Interface for the platform-backed device-authenticator flow used by this build.
    /// The current implementation primarily exposes a local Windows Hello path rather
    /// than a full cross-device WebAuthn ecosystem.
    /// </summary>
    public interface IPasskeyService
    {
        /// <summary>
        /// Indicates whether the current platform-backed authenticator is available.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Indicates whether biometric authentication is available and enrolled.
        /// </summary>
        bool IsBiometricAvailable { get; }

        /// <summary>
        /// Registers a new device-bound credential with the platform authenticator.
        /// This creates a local credential that can be used for future authentications.
        /// </summary>
        /// <param name="userId">Unique identifier for the user/vault.</param>
        /// <param name="userName">Display name for the user/vault.</param>
        /// <param name="rpId">Relying party identifier (e.g., "phantomvault.local").</param>
        /// <param name="challenge">Cryptographic challenge to prevent replay attacks.</param>
        /// <returns>The credential ID to be stored in the manifest.</returns>
        Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge);

        /// <summary>
        /// Authenticates the user using a previously registered device credential.
        /// This will typically prompt for biometric or PIN verification.
        /// </summary>
        /// <param name="credentialId">The credential ID from registration.</param>
        /// <param name="rpId">Relying party identifier (must match registration).</param>
        /// <param name="challenge">Cryptographic challenge to prevent replay attacks.</param>
        /// <returns>True if authentication succeeded, false otherwise.</returns>
        Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge);

        /// <summary>
        /// Gets a human-readable description of the authenticator exposed by the current build.
        /// </summary>
        string AuthenticatorDescription { get; }

        /// <summary>
        /// Rotates a device credential by creating a new one and securely deleting the old.
        /// This improves security by ensuring potentially compromised credentials are invalidated.
        /// </summary>
        /// <param name="oldCredentialId">The credential ID to rotate</param>
        /// <param name="relyingParty">Relying party ID (e.g., "phantomvault.local")</param>
        /// <param name="userId">User identifier</param>
        /// <returns>New credential information</returns>
        Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId);

        /// <summary>
        /// Deletes a credential by ID. Used for credential rotation.
        /// </summary>
        /// <param name="credentialId">The credential ID to delete</param>
        Task DeleteCredentialAsync(byte[] credentialId);
    }
}
