using System;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{

    public interface ISystemSecurityController
    {

        Task ClearClipboardAsync();

        void ScrubSensitiveCaches();

        void RegisterClipboardClearer(Func<Task> clearer);
    }
}

