using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Models
{

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

