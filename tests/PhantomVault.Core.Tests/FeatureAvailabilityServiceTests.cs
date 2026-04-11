using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class FeatureAvailabilityServiceTests
    {
        [Fact]
        public void VeraCryptIntegration_StatusMatchesReportedAvailability()
        {
            var service = new FeatureAvailabilityService();

            var status = service.GetFeatureStatus("VeraCrypt.Integration");

            Assert.NotNull(status);
            Assert.Equal(service.IsFeatureAvailable("VeraCrypt.Integration"), status!.IsAvailable);
        }

        [Fact]
        public void VeraCryptIntegration_UnavailableStatusHasHelpfulMessage()
        {
            var service = new FeatureAvailabilityService();
            var status = service.GetFeatureStatus("VeraCrypt.Integration");

            Assert.NotNull(status);
            if (!status!.IsAvailable)
            {
                Assert.False(string.IsNullOrWhiteSpace(status.LimitationMessage));
                Assert.Contains("VeraCrypt", status.LimitationMessage!);
            }
        }
    }
}
