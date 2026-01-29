using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Summarises the results of a password health analysis. Counters
    /// track how many credentials are considered weak (entropy below a
    /// threshold), old (not updated for a defined duration) or reused
    /// across multiple entries. A list of titles for problematic
    /// credentials can be provided for display.
    /// </summary>
    public sealed class PasswordHealthReport
    {
        public int TotalCredentials { get; set; }
        public int WeakCount { get; set; }
        public int ReusedCount { get; set; }
        public int OldCount { get; set; }
        public double AverageEntropy { get; set; }
        public List<string> WeakTitles { get; set; } = new();
        public List<string> ReusedTitles { get; set; } = new();
        public List<string> OldTitles { get; set; } = new();
    }
}