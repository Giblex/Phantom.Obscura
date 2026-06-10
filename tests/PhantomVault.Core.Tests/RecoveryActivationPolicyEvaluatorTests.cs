using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class RecoveryActivationPolicyEvaluatorTests
    {
        private static RecoveryActivationPolicy DefaultPolicy() => new()
        {
            RequiredPossessionFactors = 2,
            RequiredIdentityFactors = 2,
            EnabledPossessionFactors = PossessionFactor.UsbBinding | PossessionFactor.Keyfile | PossessionFactor.RecoveryCode,
            EnabledIdentityFactors = IdentityFactor.PhantomKey | IdentityFactor.Passkey | IdentityFactor.Password | IdentityFactor.Pin,
        };

        [Fact]
        public void NullPolicy_AllowsRecovery()
        {
            var result = RecoveryActivationPolicyEvaluator.Evaluate(null, new RecoveryActivationSessionSnapshot());

            Assert.True(result.Allowed);
        }

        [Fact]
        public void NullSnapshot_TreatedAsEmpty_AndDenied()
        {
            var result = RecoveryActivationPolicyEvaluator.Evaluate(DefaultPolicy(), null);

            Assert.False(result.Allowed);
            Assert.Equal(0, result.SatisfiedPossession);
            Assert.Equal(0, result.SatisfiedIdentity);
        }

        [Fact]
        public void MeetsBothQuotas_Allowed()
        {
            var snap = new RecoveryActivationSessionSnapshot
            {
                UsedPossessionFactors = PossessionFactor.Keyfile | PossessionFactor.UsbBinding,
                UsedIdentityFactors = IdentityFactor.Password | IdentityFactor.PhantomKey,
            };

            var result = RecoveryActivationPolicyEvaluator.Evaluate(DefaultPolicy(), snap);

            Assert.True(result.Allowed);
            Assert.Equal(2, result.SatisfiedPossession);
            Assert.Equal(2, result.SatisfiedIdentity);
        }

        [Fact]
        public void OnlyKeyfile_DeniesWhenTwoPossessionRequired()
        {
            // Mandatory USB keyfile alone — no device binding, no recovery code.
            var snap = new RecoveryActivationSessionSnapshot
            {
                UsedPossessionFactors = PossessionFactor.Keyfile,
                UsedIdentityFactors = IdentityFactor.Password | IdentityFactor.PhantomKey,
            };

            var result = RecoveryActivationPolicyEvaluator.Evaluate(DefaultPolicy(), snap);

            Assert.False(result.Allowed);
            Assert.Equal(1, result.SatisfiedPossession);
            Assert.True(result.MissingPossession.HasFlag(PossessionFactor.UsbBinding));
            Assert.True(result.MissingPossession.HasFlag(PossessionFactor.RecoveryCode));
        }

        [Fact]
        public void EnoughPossession_NotEnoughIdentity_Denied()
        {
            var snap = new RecoveryActivationSessionSnapshot
            {
                UsedPossessionFactors = PossessionFactor.Keyfile | PossessionFactor.UsbBinding,
                UsedIdentityFactors = IdentityFactor.Password,
            };

            var result = RecoveryActivationPolicyEvaluator.Evaluate(DefaultPolicy(), snap);

            Assert.False(result.Allowed);
            Assert.Equal(2, result.SatisfiedPossession);
            Assert.Equal(1, result.SatisfiedIdentity);
        }

        [Fact]
        public void FactorsNotEnabledInPolicy_AreNotCounted()
        {
            var policy = DefaultPolicy();
            // Disable Password — only PhantomKey / Passkey / Pin count as identity.
            policy.EnabledIdentityFactors = IdentityFactor.PhantomKey | IdentityFactor.Passkey | IdentityFactor.Pin;

            var snap = new RecoveryActivationSessionSnapshot
            {
                UsedPossessionFactors = PossessionFactor.Keyfile | PossessionFactor.UsbBinding,
                UsedIdentityFactors = IdentityFactor.Password | IdentityFactor.Pin,
            };

            var result = RecoveryActivationPolicyEvaluator.Evaluate(policy, snap);

            // Password is disabled by policy, only Pin counts → 1 identity satisfied, need 2.
            Assert.False(result.Allowed);
            Assert.Equal(1, result.SatisfiedIdentity);
        }

        [Fact]
        public void ZeroRequirements_AlwaysAllowed()
        {
            var policy = DefaultPolicy();
            policy.RequiredPossessionFactors = 0;
            policy.RequiredIdentityFactors = 0;

            var result = RecoveryActivationPolicyEvaluator.Evaluate(policy, new RecoveryActivationSessionSnapshot());

            Assert.True(result.Allowed);
        }

        [Theory]
        [InlineData(PossessionFactor.None, 0)]
        [InlineData(PossessionFactor.Keyfile, 1)]
        [InlineData(PossessionFactor.Keyfile | PossessionFactor.UsbBinding, 2)]
        [InlineData(PossessionFactor.Keyfile | PossessionFactor.UsbBinding | PossessionFactor.RecoveryCode, 3)]
        public void CountEnabled_Possession_ReturnsPopCount(PossessionFactor flags, int expected)
        {
            Assert.Equal(expected, RecoveryActivationPolicyEvaluator.CountEnabled(flags));
        }

        [Theory]
        [InlineData(IdentityFactor.None, 0)]
        [InlineData(IdentityFactor.Password, 1)]
        [InlineData(IdentityFactor.Password | IdentityFactor.Pin, 2)]
        [InlineData(IdentityFactor.PhantomKey | IdentityFactor.Passkey | IdentityFactor.Password | IdentityFactor.Pin, 4)]
        public void CountEnabled_Identity_ReturnsPopCount(IdentityFactor flags, int expected)
        {
            Assert.Equal(expected, RecoveryActivationPolicyEvaluator.CountEnabled(flags));
        }
    }
}
