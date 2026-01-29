using System;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Encapsulates the data required to share a credential with another
    /// party. The credential itself is encrypted using a randomly
    /// generated symmetric key. That symmetric key is then encrypted
    /// with the recipient's public key (e.g. RSA‑4096). The payload can
    /// then be sent via email or other untrusted channels; only the
    /// recipient holding the corresponding private key can decrypt the
    /// symmetric key and thus access the credential. This mirrors the
    /// hybrid encryption scheme used elsewhere in the application.
    /// </summary>
    public sealed class SharedCredential
    {
        /// <summary>
        /// Identifier for the credential being shared. This might be
        /// used by the receiver to correlate with their own vault.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The encrypted credential data. The plaintext consists of
        /// serialized <see cref="Credential"/> information encrypted
        /// with a symmetric algorithm such as AES‑GCM.
        /// </summary>
        public string EncryptedCredentialBase64 { get; set; } = string.Empty;

        /// <summary>
        /// The encrypted symmetric key, encoded in Base64. The sender
        /// encrypts the symmetric key using the recipient's public key
        /// (e.g. RSA‑4096). Only the recipient can decrypt this with
        /// their private key.
        /// </summary>
        public string EncryptedKeyBase64 { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp indicating when this share package was created. May
        /// help receivers order or expire shared credentials.
        /// </summary>
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}