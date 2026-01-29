using System;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Represents a credential that matches the current context
    /// </summary>
    public class CredentialMatch
    {
        /// <summary>
        /// The credential ID (Title for Credential model)
        /// </summary>
        public string CredentialId { get; set; } = string.Empty;

        /// <summary>
        /// Display name (e.g., "GitHub - john@email.com")
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Username/email
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The domain/site this credential is for
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// Match confidence score (0-100)
        /// </summary>
        public int ConfidenceScore { get; set; }

        /// <summary>
        /// When this credential was last used
        /// </summary>
        public DateTime? LastUsed { get; set; }

        /// <summary>
        /// Whether this is a passkey credential
        /// </summary>
        public bool IsPasskey { get; set; }

        /// <summary>
        /// Tags/labels for categorization
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}
