using System;
using System.Linq;

namespace PhantomVault.Core.Services.Security
{

    public sealed class DefenceRule
    {

        public string Id { get; }

        public ThreatType TriggerType { get; }

        public ThreatLevel MinLevel { get; }

        public DefenceActionType[] Actions { get; }

        public TimeSpan? Cooldown { get; }

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
            Actions = actions.ToArray();
            Cooldown = cooldown;
            IsEnabled = isEnabled;
        }
    }
}

