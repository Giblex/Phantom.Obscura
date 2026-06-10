using System;

namespace PhantomVault.Core.Options
{

    public sealed class SecurityOptions
    {

        public bool AutoActivateDecoyOnTamper { get; set; } = true;

        public int DecoyCredentialCount { get; set; } = 25;

        public bool LogDecoyActivation { get; set; } = true;

        public bool AlertEmergencyContactOnDecoy { get; set; } = false;

        public string? DecoyAlertFilePath { get; set; }

        public int? DecoyRandomSeed { get; set; }

        public bool EnableTamperDetection { get; set; } = true;

        public int TamperDetectionIntervalSeconds { get; set; } = 10;

        public void Validate()
        {
            if (DecoyCredentialCount < 10 || DecoyCredentialCount > 50)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DecoyCredentialCount),
                    DecoyCredentialCount,
                    "Decoy credential count must be between 10 and 50.");
            }

            if (TamperDetectionIntervalSeconds < 1 || TamperDetectionIntervalSeconds > 300)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(TamperDetectionIntervalSeconds),
                    TamperDetectionIntervalSeconds,
                    "Tamper detection interval must be between 1 and 300 seconds.");
            }
        }
    }
}

