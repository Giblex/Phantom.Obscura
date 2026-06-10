using System;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Models
{

    public sealed class SecureTrashRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Credential Payload { get; set; } = new Credential();
        public string? OriginalGroup { get; set; } = string.Empty;
        public DateTimeOffset DeletedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledPurgeUtc { get; set; }
            = DateTimeOffset.UtcNow.AddDays(30);
        public bool SecurelyPurged { get; set; }
            = false;
        public DateTimeOffset? PurgedUtc { get; set; }
            = null;
    }
}

