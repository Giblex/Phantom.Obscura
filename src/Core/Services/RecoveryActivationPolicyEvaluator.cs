using System;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Pure evaluator: given a policy and a snapshot of factors used during this session,
    /// decides whether Recovery launch is allowed. No I/O, no DI — fully unit-testable.
    /// </summary>
    public static class RecoveryActivationPolicyEvaluator
    {
        public static RecoveryActivationGateResult Evaluate(
            RecoveryActivationPolicy? policy,
            RecoveryActivationSessionSnapshot? snapshot)
        {
            if (policy == null)
            {
                return new RecoveryActivationGateResult
                {
                    Allowed = true,
                    Reason = "No Recovery Activation policy configured.",
                };
            }

            var used = snapshot ?? new RecoveryActivationSessionSnapshot();

            // Only count factors that are both enabled in the policy AND satisfied this session.
            var possessionUsed = used.UsedPossessionFactors & policy.EnabledPossessionFactors;
            var identityUsed = used.UsedIdentityFactors & policy.EnabledIdentityFactors;

            int possessionCount = PopCount((int)possessionUsed);
            int identityCount = PopCount((int)identityUsed);

            var missingPossession = policy.EnabledPossessionFactors & ~used.UsedPossessionFactors;
            var missingIdentity = policy.EnabledIdentityFactors & ~used.UsedIdentityFactors;

            bool possessionOk = possessionCount >= policy.RequiredPossessionFactors;
            bool identityOk = identityCount >= policy.RequiredIdentityFactors;
            bool allowed = possessionOk && identityOk;

            string reason = allowed
                ? $"Recovery activation policy satisfied ({possessionCount}/{policy.RequiredPossessionFactors} possession, {identityCount}/{policy.RequiredIdentityFactors} identity)."
                : BuildDenyReason(possessionCount, identityCount, policy);

            return new RecoveryActivationGateResult
            {
                Allowed = allowed,
                Reason = reason,
                RequiredPossession = policy.RequiredPossessionFactors,
                RequiredIdentity = policy.RequiredIdentityFactors,
                SatisfiedPossession = possessionCount,
                SatisfiedIdentity = identityCount,
                MissingPossession = missingPossession,
                MissingIdentity = missingIdentity,
            };
        }

        private static string BuildDenyReason(int possession, int identity, RecoveryActivationPolicy policy)
        {
            return
                $"Recovery is blocked by activation policy: " +
                $"{possession}/{policy.RequiredPossessionFactors} possession factors and " +
                $"{identity}/{policy.RequiredIdentityFactors} identity factors satisfied this session. " +
                $"Open Settings → Recovery Activation to review.";
        }

        private static int PopCount(int value)
        {
            // Kernighan: clears the lowest set bit per iteration.
            int count = 0;
            unchecked
            {
                uint v = (uint)value;
                while (v != 0)
                {
                    v &= v - 1;
                    count++;
                }
            }
            return count;
        }

        public static int CountEnabled(PossessionFactor flags) => PopCount((int)flags);
        public static int CountEnabled(IdentityFactor flags) => PopCount((int)flags);
    }
}
