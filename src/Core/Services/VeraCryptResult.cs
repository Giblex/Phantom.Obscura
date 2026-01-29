using System;

namespace PhantomVault.Core.Services
{
    public sealed class VeraCryptResult
    {
        public int ExitCode { get; init; }
        public string StdOut { get; init; } = string.Empty;
        public string StdErr { get; init; } = string.Empty;
        public bool Success => ExitCode == 0;
    }
}
