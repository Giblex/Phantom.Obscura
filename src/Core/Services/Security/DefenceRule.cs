using System;
using System.Linq;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Defines a rule that maps a threat type to defensive actions.
    /// Rules can have cooldowns to prevent over-triggering.
    /// </summary>
    public sealed class DefenceRule
    {
        /// <summary>
        /// Unique identifier for this rule.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The threat type that triggers this rule.
        /// </summary>
        public ThreatType TriggerType { get; }

        /// <summary>
        /// Minimum threat level required to activate this rule.
        /// </summary>
        public ThreatLevel MinLevel { get; }

        /// <summary>
        /// Actions to execute when this rule is triggered.
        /// </summary>
        public DefenceActionType[] Actions { get; }

        /// <summary>
        /// Optional cooldown period before this rule can fire again.
        /// Prevents flooding defensive actions.
        /// </summary>
        public TimeSpan? Cooldown { get; }

        /// <summary>
        /// Whether this rule is currently enabled. Disabled rules will not trigger.
        /// </summary>
        public bool IsEnabled { get; set; }

        public DefenceRule(
            string id,
            ThreatType triggerType,
            ThreatLevel minLevel,
            DefenceActionType[] actions,
            TimeSpan? cooldown = null,
            bool isEnabled = true)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Rule ID cannot be empty", nameof(id));

            if (actions == null || actions.Length == 0)
                throw new ArgumentException("Rule must have at least one action", nameof(actions));

            Id = id;
            TriggerType = triggerType;
            MinLevel = minLevel;
            Actions = actions.ToArray(); // Defensive copy
            Cooldown = cooldown;
            IsEnabled = isEnabled;
        }
    }
}
