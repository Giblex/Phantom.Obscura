using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Singleton snapshot of which factors were actually used during the active unlock
    /// session. Populated by the unlock flow; consulted by the Recovery launch gate.
    /// </summary>
    public sealed class RecoveryActivationSessionState
    {
        private readonly object _sync = new();
        private PossessionFactor _possession;
        private IdentityFactor _identity;

        public PossessionFactor PossessionFactorsUsed
        {
            get { lock (_sync) return _possession; }
        }

        public IdentityFactor IdentityFactorsUsed
        {
            get { lock (_sync) return _identity; }
        }

        public void MarkPossessionUsed(PossessionFactor factor)
        {
            lock (_sync) _possession |= factor;
        }

        public void MarkIdentityUsed(IdentityFactor factor)
        {
            lock (_sync) _identity |= factor;
        }

        public void Reset()
        {
            lock (_sync)
            {
                _possession = PossessionFactor.None;
                _identity = IdentityFactor.None;
            }
        }

        public RecoveryActivationSessionSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new RecoveryActivationSessionSnapshot
                {
                    UsedPossessionFactors = _possession,
                    UsedIdentityFactors = _identity,
                };
            }
        }
    }
}
