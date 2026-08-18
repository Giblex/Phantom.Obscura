using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models
{

    public sealed class PasswordHealthReport
    {
        public int TotalCredentials { get; set; }

        /// <summary>
        /// How many individual secrets were audited. This is at least
        /// <see cref="TotalCredentials"/>, because an entry contributes its own password
        /// plus any secret sections it carries — so weak/reused/breached counts are
        /// measured against this, not against the entry count.
        /// </summary>
        public int AnalyzedSecretCount { get; set; }
        public int WeakCount { get; set; }
        public int ReusedCount { get; set; }
        public int OldCount { get; set; }
        public int BreachedCount { get; set; }
        public double AverageEntropy { get; set; }
        public List<string> WeakTitles { get; set; } = new();
        public List<string> ReusedTitles { get; set; } = new();
        public List<string> OldTitles { get; set; } = new();
        public List<string> BreachedTitles { get; set; } = new();

        /// <summary>
        /// True when breach checking was requested but no breach lookup was available
        /// (e.g. offline / not opted in). Lets the UI distinguish "0 breached" from
        /// "breach status unknown" instead of silently showing a reassuring zero.
        /// </summary>
        public bool BreachCheckPerformed { get; set; }
    }
}

