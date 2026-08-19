using System.Reflection;
using PhantomVault.Core.Models;
using PhantomVault.UI.ViewModels;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class SetupWizardTransportSelectionTests
{
    [Theory]
    [InlineData(VaultProtectionTier.StandardSecure, true, VaultStorageTransport.PackedVolume)]
    [InlineData(VaultProtectionTier.StandardSecure, false, VaultStorageTransport.FileSystem)]
    [InlineData(VaultProtectionTier.StealthSecure, true, VaultStorageTransport.PackedVolume)]
    [InlineData(VaultProtectionTier.StealthSecure, false, VaultStorageTransport.FileSystem)]
    [InlineData(VaultProtectionTier.BlackSecure, true, VaultStorageTransport.RawDevice)]
    [InlineData(VaultProtectionTier.BlackSecure, false, VaultStorageTransport.RawDevice)]
    public void EncryptedContainerChoiceControlsNonRawTransport(
        VaultProtectionTier tier,
        bool encryptedContainerEnabled,
        VaultStorageTransport expected)
    {
        MethodInfo method = typeof(SetupWizardViewModel).GetMethod(
            "GetEffectiveStorageTransport",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (VaultStorageTransport)method.Invoke(
            null,
            new object[] { tier, encryptedContainerEnabled })!;

        Assert.Equal(expected, actual);
    }
}
