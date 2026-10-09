using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.UI.Services.AutoFill;
using PhantomVault.UI.Services.Privileged;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class NativeHostPipeServerTests
{
    [Theory]
    [InlineData("bank.com", "bank.com", true)]
    [InlineData("BANK.com.", "bank.com", true)]
    [InlineData("mybank.com", "bank.com", false)]
    [InlineData("bank.com.evil.test", "bank.com", false)]
    [InlineData("bank.com", "evilbank.com", false)]
    [InlineData("login.bank.com", "bank.com", false)]
    [InlineData("www.bank.com", "bank.com", false)]
    [InlineData("bank.com", "", false)]
    public void DomainMatching_IsExact(string saved, string requested, bool expected)
        => Assert.Equal(expected, NativeHostPipeServer.DomainsMatch(saved, requested));

    [Fact]
    public void CredentialResponses_ExcludeCrossHostMatchesAndRejectMissingDomain()
    {
        var context = new Mock<IAutofillVaultContext>();
        context.SetupGet(c => c.IsUnlocked).Returns(true);
        var provider = new Mock<ICredentialProvider>();
        provider.Setup(p => p.GetCredentials()).Returns(new[]
        {
            new Credential { Title = "Correct", Url = "https://bank.com/login", Password = "test-only" },
            new Credential { Title = "Wrong", Url = "https://mybank.com", Password = "test-only" }
        });
        using var server = new NativeHostPipeServer(context.Object);
        server.SetCredentialProvider(provider.Object, new VaultManifest());
        var process = typeof(NativeHostPipeServer).GetMethod("ProcessRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var response = JsonDocument.Parse((string)process.Invoke(server, new object[] { "{\"action\":\"getCredentials\",\"domain\":\"bank.com\"}" })!);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
        var credentials = response.RootElement.GetProperty("credentials");
        Assert.Equal(1, credentials.GetArrayLength());
        Assert.Equal("Correct", credentials[0].GetProperty("title").GetString());
        using var missing = JsonDocument.Parse((string)process.Invoke(server, new object[] { "{\"action\":\"getCredentials\"}" })!);
        Assert.False(missing.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void NativeHostIdentity_RequiresTheExactApplicationImage()
    {
        string application = Path.Combine(Path.GetTempPath(), "Obscura", "PhantomVault.UI.exe");
        Assert.True(NativeHostPipeServer.IsNativeHostImage(application, application));
        Assert.False(NativeHostPipeServer.IsNativeHostImage(Path.Combine(Path.GetTempPath(), "chrome.exe"), application));
        Assert.False(NativeHostPipeServer.IsNativeHostImage(Path.Combine(Path.GetTempPath(), "PhantomVault.UI.exe"), application));
        Assert.False(NativeHostPipeServer.IsNativeHostImage(null, application));
    }

    [Fact]
    public async Task Shutdown_DoesNotActivateOrInstallAnUnavailableHelper()
    {
        var client = new NamedPipeBrokerClient
        {
            EnsureAvailableAsync = () => throw new InvalidOperationException("Shutdown must never activate the helper.")
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ShutdownAsync(cancellation.Token));
    }
}
