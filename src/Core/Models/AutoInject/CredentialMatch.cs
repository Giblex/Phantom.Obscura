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

        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}

