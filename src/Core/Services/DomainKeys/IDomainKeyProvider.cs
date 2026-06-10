using System;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{

    public interface IDomainKeyProvider : IDisposable
    {

        bool IsUnlocked { get; }

        ReadOnlySpan<byte> GetObscuraKey();

        ReadOnlySpan<byte> GetAttestorKey();

        ReadOnlySpan<byte> GetRecoveryKey();

        void Lock();
    }
}

