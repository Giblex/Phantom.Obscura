using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    // Android substitute for the desktop BlackSecureRawVolumeService (raw
    // physical-device access is Windows-only). Raw-volume validation is
    // unavailable on mobile.
    public sealed class BlackSecureRawVolumeService
    {
        public string? TryResolvePhysicalDevicePathFromSelection(string? selection) => null;

        public Task<bool> IsBlackSecureVolumeAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
