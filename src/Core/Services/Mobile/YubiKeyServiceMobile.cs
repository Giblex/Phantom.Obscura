using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    // Android substitute for the desktop YubiKeyService (Yubico native HID is
    // Windows-only). Hardware token checks report "not present" on mobile.
    public sealed class YubiKeyService
    {
        public bool IsTokenPresent() => false;
    }
}
