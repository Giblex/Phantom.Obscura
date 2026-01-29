using System;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Represents a security threat event detected by the application.
    /// These events are fed into the Defence Engine for evaluation and response.
    /// </summary>
    public sealed class ThreatEvent
    {
        /// <summary>
        /// The category of threat detected.
        /// </summary>
        public ThreatType Type { get; }

        /// <summary>
        /// The severity level of this threat.
        /// </summary>
        public ThreatLevel Level { get; }

        /// <summary>
        /// When this threat event occurred.
        /// </summary>
        public DateTimeOffset OccurredAt { get; }

        /// <summary>
        /// Optional additional context about the threat (e.g., device ID, attempt count, etc.).
        /// </summary>
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
