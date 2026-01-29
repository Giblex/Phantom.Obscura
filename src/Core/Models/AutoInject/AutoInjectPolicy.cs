using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Defines auto-inject behavior for a domain or application
    /// </summary>
    public class AutoInjectPolicy
    {
        /// <summary>
        /// Unique identifier for this policy
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Domain pattern (e.g., "github.com", "*.company.com")
        /// </summary>
        public string DomainPattern { get; set; } = string.Empty;

        /// <summary>
        /// Process name pattern (e.g., "chrome.exe", "Slack")
        /// </summary>
        public string? ProcessPattern { get; set; }

        /// <summary>
        /// Auto-inject behavior
        /// </summary>
        public AutoInjectBehavior Behavior { get; set; } = AutoInjectBehavior.Prompt;

        /// <summary>
        /// Whether to require PIN/2FA for this domain
        /// </summary>
        public bool RequireAdditionalAuth { get; set; }

        /// <summary>
        /// Whether to submit form automatically after filling
        /// </summary>
        public bool AutoSubmit { get; set; }

        /// <summary>
        /// Allowed machine fingerprints (empty = all machines allowed)
        /// </summary>
        public List<string> AllowedMachines { get; set; } = new();

        /// <summary>
        /// Time restrictions (e.g., only during business hours)
        /// </summary>
        public TimeRestriction? TimeRestriction { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    }

    public enum AutoInjectBehavior
    {
        /// <summary>
        /// Never auto-inject, user must manually fill
        /// </summary>
        Never,

        /// <summary>
        /// Show prompt asking user for confirmation
        /// </summary>
        Prompt,

        /// <summary>
        /// Automatically inject without prompting
        /// </summary>
        Auto
    }

    public class TimeRestriction
    {
        /// <summary>
        /// Start hour (0-23)
        /// </summary>
        public int StartHour { get; set; }

        /// <summary>
        /// End hour (0-23)
        /// </summary>
        public int EndHour { get; set; }

        /// <summary>
        /// Days of week (0 = Sunday, 6 = Saturday)
        /// </summary>
        public List<int> AllowedDays { get; set; } = new();
    }
}
