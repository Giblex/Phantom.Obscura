using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models.DomainStores
{
    /// <summary>
    /// Cryptographic domains for the USB trio architecture.
    /// Each domain has its own:
    /// - Derived key (via HKDF from master)
    /// - Encrypted store file
    /// - Recovery sealed blob
    /// - (Future) Isolated process
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CryptoDomain
    {
        /// <summary>
        /// Obscura domain - credential storage only.
        /// Contains passwords, notes, cards.
        /// Does NOT contain TOTP, passkeys, or recovery codes.
        /// </summary>
        Obscura,

        /// <summary>
        /// Attestor domain - identity and authentication authority.
        /// Contains TOTP seeds, passkey private keys, certificates, SSH keys.
        /// Performs cryptographic operations (signing, OTP generation).
        /// Keys are USED here, never EXPORTED.
        /// </summary>
        Attestor,

        /// <summary>
        /// Recovery domain - break-glass recovery operations.
        /// Contains sealed recovery key blobs, recovery codes, audit log.
        /// HARDER to unlock than other domains (not a convenience layer).
        /// </summary>
        Recovery
    }
}
