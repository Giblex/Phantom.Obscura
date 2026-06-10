namespace PhantomVault.Core.Services.Security
{

    public enum ThreatType
    {

        FailedLoginBurst,

        NewDeviceFingerprint,

        IntegrityMismatch,

        ExcessiveExports,

        HighRiskEntryFlood,

        BehaviourDeviation,

        PhysicalRemoval
    }
}

