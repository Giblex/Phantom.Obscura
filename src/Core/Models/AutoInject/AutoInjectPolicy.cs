using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{

    public class AutoInjectPolicy
    {

        public Guid Id { get; set; } = Guid.NewGuid();

        public string DomainPattern { get; set; } = string.Empty;

        public string? ProcessPattern { get; set; }

        public AutoInjectBehavior Behavior { get; set; } = AutoInjectBehavior.Prompt;

        public bool RequireAdditionalAuth { get; set; }

        public bool AutoSubmit { get; set; }

        public List<string> AllowedMachines { get; set; } = new();

        public TimeRestriction? TimeRestriction { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    }

    public enum AutoInjectBehavior
    {

        Never,

        Prompt,

        Auto
    }

    public class TimeRestriction
    {

        public int StartHour { get; set; }

        public int EndHour { get; set; }

        public List<int> AllowedDays { get; set; } = new();
    }
}

