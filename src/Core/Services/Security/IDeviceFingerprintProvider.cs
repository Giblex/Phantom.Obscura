using PhantomVault.Core.Models.Security;

namespace PhantomVault.Core.Services.Security
{

    public interface IDeviceFingerprintProvider
    {

        DeviceFingerprint GetCurrentFingerprint();
    }
}

