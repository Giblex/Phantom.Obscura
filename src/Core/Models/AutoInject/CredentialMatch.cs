using System;

namespace PhantomVault.Core.Models.AutoInject
{

    public class CredentialMatch
    {

        public string CredentialId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Domain { get; set; } = string.Empty;

        public int ConfidenceScore { get; set; }

        public DateTime? LastUsed { get; set; }

        public bool IsPasskey { get; set; }

        /// <summary>
        /// Stored passkey handle. Needed to actually authenticate — without it the
        /// UI could show a passkey row but had nothing to hand the authenticator.
        /// </summary>
        public string? PasskeyId { get; set; }

        /// <summary>Opaque Attestor-owned passkey handle.</summary>
        public string? AttestorPasskeyReference { get; set; }

        /// <summary>
        /// Relying-party id for the passkey ceremony, normally the site's domain.
        /// </summary>
        public string RelyingPartyId { get; set; } = string.Empty;

        /// <summary>
        /// Whether this credential carries a TOTP secret. Lets the suggestion UI offer
        /// a code without having to fetch the secret itself just to find out.
        /// </summary>
        public bool HasTotp { get; set; }

        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}

