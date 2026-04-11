#nullable enable

using System.Runtime.InteropServices;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class FeatureAvailabilityServiceWindowsHelloTests
    {
        [Fact]
        public void WindowsHelloFeatureStatus_MatchesPasskeyServiceSupport()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var featureAvailability = new FeatureAvailabilityService();
            var passkeyService = new PasskeyService();

            var status = featureAvailability.GetFeatureStatus("Biometric.WindowsHello");

            Assert.NotNull(status);
            Assert.Equal(passkeyService.IsSupported, status!.IsAvailable);
        }
    }
}
