using System;
using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{

    public sealed class IncidentState
    {

        public double Score { get; set; }

        public VaultSecurityState State { get; set; } = VaultSecurityState.Normal;

        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }
}

