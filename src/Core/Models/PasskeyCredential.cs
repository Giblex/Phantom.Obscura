using System;

namespace PhantomVault.Core.Models
{

    public class PasskeyCredential
    {

        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public string? Name { get; set; }
    }
}

