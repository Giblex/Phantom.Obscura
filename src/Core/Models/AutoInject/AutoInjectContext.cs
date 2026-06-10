using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{

    public class AutoInjectContext
    {

        public string WindowTitle { get; set; } = string.Empty;

        public string ProcessName { get; set; } = string.Empty;

        public string? Url { get; set; }

        public string? Domain { get; set; }

        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        public string? MachineFingerprint { get; set; }

        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

