using System.Collections.Generic;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Sync
{
    /// <summary>
    /// Implemented by the unlocked vault view-model so a TOTP sync pipe server can
    /// merge entries pushed from the paired app without duplicating merge logic.
    /// </summary>
    public interface ITotpSyncBridge
    {
        Task ApplyIncomingEntriesAsync(List<SharedTotpEntry> entries);
    }
}
