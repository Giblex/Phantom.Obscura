using System;

namespace PhantomVault.Core.Models
{

    public sealed class SharedCredential
    {

        public string Title { get; set; } = string.Empty;

        public string EncryptedCredentialBase64 { get; set; } = string.Empty;

        public string EncryptedKeyBase64 { get; set; } = string.Empty;

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}

