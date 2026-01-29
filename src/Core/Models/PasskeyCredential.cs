using System;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Represents a WebAuthn/FIDO2 passkey credential.
    /// Contains the credential ID and public key for authentication.
    /// </summary>
    public class PasskeyCredential
    {
        /// <summary>
        /// Unique identifier for this credential.
        /// </summary>
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Public key for signature verification.
        /// </summary>
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// When the credential was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Display name for the credential.
        /// </summary>
        public string? Name { get; set; }
    }
}
