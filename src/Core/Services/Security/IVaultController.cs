using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{

    public interface IVaultController
    {

        Task SwitchToDecoyVaultAsync();

        void EnterReadOnlyMode();

        void ExitReadOnlyMode();

        bool IsReadOnly { get; }

        bool IsDecoyActive { get; }

        VaultDatabase? ActiveDecoyDatabase { get; }
    }
}

