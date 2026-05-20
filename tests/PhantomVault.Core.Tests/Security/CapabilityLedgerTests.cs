using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security
{
    public sealed class CapabilityLedgerTests
    {
        [Fact]
        public void SecurityCriticalCapabilities_AreExplicitlyImplementedOrFailClosed()
        {
            var unsafeStates = CapabilityLedger.GetSecurityCriticalUnsafeStates();

            Assert.Empty(unsafeStates);
            Assert.Contains(CapabilityLedger.All, r => r.Id == "obscura.rekey.streaming" && r.State == CapabilityState.Implemented);
            Assert.Contains(CapabilityLedger.All, r => r.Id == "obscura.autofill.browser-native-host" && r.State == CapabilityState.FailClosed);
            Assert.Contains(CapabilityLedger.All, r => r.Id == "obscura.yubikey.fido2" && r.State == CapabilityState.FailClosed);
        }

        [Fact]
        public void Solution_IncludesCoreAndUiTestProjects()
        {
            string root = FindRepositoryRoot();
            string solution = File.ReadAllText(Path.Combine(root, "PhantomVault.sln"));

            Assert.Contains(@"tests\PhantomVault.Core.Tests\PhantomVault.Core.Tests.csproj", solution, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(@"tests\PhantomVault.UI.Tests\PhantomVault.UI.Tests.csproj", solution, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BasePolicy_EnforcesCrossPolicyValidationAndFailsClosed()
        {
            string root = FindRepositoryRoot();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Policies", "base_policy.json")));
            var sync = document.RootElement.GetProperty("sync");

            Assert.True(sync.GetProperty("enforceCrossPolicyValidation").GetBoolean());
            Assert.True(sync.GetProperty("validateDeviceBindingConsistency").GetBoolean());
            Assert.True(sync.GetProperty("validateUsbManifestBinding").GetBoolean());
            Assert.True(sync.GetProperty("validateSignatureConsistency").GetBoolean());
            Assert.Equal("Fail", sync.GetProperty("onPolicySyncFailure").GetString());
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PhantomVault.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate PhantomVault.sln from test output path.");
        }
    }
}
