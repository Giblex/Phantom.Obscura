using System;
using System.Threading.Tasks;
using Moq;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Network;
using PhantomVault.UI.Services.Licensing;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class StripeLicensingClientTests
{
    [Fact]
    public async Task ActivateAsync_OfflineMode_DeniesBeforeRequestingConsent()
    {
        var gateway = new Mock<IInternetGateway>(MockBehavior.Strict);
        gateway.SetupGet(x => x.OfflineMode).Returns(true);
        var client = new StripeLicensingClient(gateway.Object);

        var result = await client.ActivateAsync(PremiumTier.Premium, usbBindingId: null);

        Assert.Equal(LicensingResultKind.Failed, result.Kind);
        Assert.Contains("offline mode", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        gateway.VerifyGet(x => x.OfflineMode, Times.Once);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public void LicensingPolicy_IsExactPinnedHttpsGatewayRequest()
    {
        var request = LicensingGatewayPolicy.CreateRequest();

        request.Validate();
        Assert.Equal(LicensingGatewayPolicy.FeatureId, request.FeatureId);
        Assert.Equal(new[] { "giblex.com" }, request.AllowedHosts);
        Assert.True(request.SpkiPinsByHost.TryGetValue("giblex.com", out var pins));
        Assert.NotNull(pins);
        Assert.True(pins!.Count >= 2, "A live and backup SPKI pin are required for rotation safety.");
        Assert.False(request.AllowSessionGrant);
    }
}
