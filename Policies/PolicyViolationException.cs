using System;

namespace PhantomVault.Core
{
    public enum PolicyViolationCode
    {
        UsbNotFound,
        UsbPolicyInvalid,
        UsbIdentityRejected,
        UsbStandardInsufficient,
        UsbCapacityInsufficient,
        UsbHotSwapNotAllowed,
        UsbAttestationMissing,
        ManifestSignatureInvalid,
        ManifestVersionMismatch,
        ManifestChainOfTrustBroken,
        ManifestRollbackNotAllowed,
        DesktopSyncDisabled,
        DesktopMultipleSessionsNotAllowed,
        DeviceBindingFailed,
        DeviceNotRegistered,
        DebuggerDetected,
        VirtualMachineDetected,
        SecureBootRequired,
        TpmRequired,
        SessionExpired,
        SessionIdleTimeout,
        PolicySyncFailed,
        PolicyConflictDetected
    }

    public class PolicyViolationException : Exception
    {
        public PolicyViolationCode Code { get; }

        public PolicyViolationException(PolicyViolationCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public PolicyViolationException(PolicyViolationCode code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }
    }
}
