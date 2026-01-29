using System;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Represents a TOTP entry that can be synchronized between PhantomAttestor and PhantomObscura
    /// </summary>
    public class SharedTotpEntry
    {
        /// <summary>
        /// Unique identifier for this TOTP entry
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// ID of the linked password entry in the vault
        /// </summary>
        public string LinkedPasswordEntryId { get; set; } = string.Empty;

        /// <summary>
        /// Service issuer (e.g., "Google", "GitHub", "Microsoft")
        /// </summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// Account name or email (e.g., "user@example.com")
        /// </summary>
        public string AccountName { get; set; } = string.Empty;

        /// <summary>
        /// Base32-encoded secret key
        /// </summary>
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// Number of digits in the TOTP code (6 or 8)
        /// </summary>
        public int Digits { get; set; } = 6;

        /// <summary>
        /// Time period in seconds (usually 30)
        /// </summary>
        public int Period { get; set; } = 30;

        /// <summary>
        /// Hash algorithm (SHA1, SHA256, SHA512)
        /// </summary>
        public string Algorithm { get; set; } = "SHA1";

        /// <summary>
        /// Last modification timestamp (UTC)
        /// </summary>
        public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Which application last modified this entry ("PhantomAttestor" or "PhantomObscura")
        /// </summary>
        public string ModifiedByApp { get; set; } = "PhantomObscura";
    }
}
