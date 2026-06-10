using System;

namespace PhantomVault.Core.Services.Security
{

    public sealed class ThreatEvent
    {

        public ThreatType Type { get; }

        public ThreatLevel Level { get; }

        public DateTimeOffset OccurredAt { get; }

        public string? Details { get; }

        public ThreatEvent(ThreatType type, ThreatLevel level, string? details = null)
        {
            Type = type;
            Level = level;
            OccurredAt = DateTimeOffset.UtcNow;
            Details = details;
        }
    }
}

