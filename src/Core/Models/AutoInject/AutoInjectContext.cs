using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Represents the current context when auto-inject is triggered
    /// </summary>
    public class AutoInjectContext
    {
        /// <summary>
        /// The title of the active window
        /// </summary>
        public string WindowTitle { get; set; } = string.Empty;

        /// <summary>
        /// The process name of the active application
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// The URL if the active window is a browser
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// The domain extracted from the URL (e.g., "github.com")
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// Timestamp when the context was captured
        /// </summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Machine fingerprint for security tracking
        /// </summary>
        public string? MachineFingerprint { get; set; }

        /// <summary>
        /// Additional metadata (browser name, tab title, etc.)
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
