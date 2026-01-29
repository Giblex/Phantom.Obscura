using Xunit;

namespace PhantomVault.Core.Tests
{
    public class PolicyEngineTests
    {
        [Fact]
        public void EnforceManifestPolicy_UsesSemanticComparison()
        {
            var policy = new ObscuraPolicy
            {
                Manifest = new ObscuraPolicy.ManifestPolicy
                {
                    RequireSignature = false,
                    MinVersion = "1.2.0",
                    MaxVersion = "1.10.0"
                }
            };

            var engine = new PolicyEngine(policy);

            // 1.10 should be within [1.2, 1.10] semantically
            engine.EnforceManifestPolicy("1.10.0", manifestHasValidSignature: true);

            Assert.Throws<PolicyViolationException>(() =>
                engine.EnforceManifestPolicy("1.1.9", manifestHasValidSignature: true));

            Assert.Throws<PolicyViolationException>(() =>
                engine.EnforceManifestPolicy("1.10.1", manifestHasValidSignature: true));
        }
    }
}
