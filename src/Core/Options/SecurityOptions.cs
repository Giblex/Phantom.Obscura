using System;

namespace PhantomVault.Core.Options
{
    /// <summary>
    /// Security configuration options for PhantomVault including tamper detection,
    /// decoy vault behavior, and defense mechanisms.
    /// </summary>
    public sealed class SecurityOptions
    {
        /// <summary>
        /// Enable automatic decoy vault activation when tampering is detected.
        /// Default: TRUE (enabled for maximum security).
        /// When enabled, the vault will automatically switch to showing fake credentials
        /// when debuggers, DLL injection, or memory manipulation is detected.
        /// </summary>
        public bool AutoActivateDecoyOnTamper { get; set; } = true;

        /// <summary>
        /// Number of fake credentials to generate in the decoy vault.
        /// Valid range: 10-50. Default: 25.
        /// Higher numbers make the decoy more convincing but slightly slower to generate.
        /// </summary>
        public int DecoyCredentialCount { get; set; } = 25;

        /// <summary>
        /// Whether to log decoy activation events to the audit log.
        /// Default: TRUE.
        /// WARNING: Sophisticated attackers who check logs may discover the decoy is active.
        /// Disable this for maximum stealth, or leave enabled for forensic evidence.
        /// </summary>
        public bool LogDecoyActivation { get; set; } = true;

        /// <summary>
        /// Alert trusted emergency contact when decoy vault is activated.
        /// Default: FALSE (requires emergency contact to be configured first).
        /// When enabled, sends silent alert via configured channel (email, SMS, etc.).
        /// </summary>
        public bool AlertEmergencyContactOnDecoy { get; set; } = false;

        /// <summary>
        /// Path to store emergency decoy activation notifications.
        /// If set, a hidden file will be created when decoy activates for later forensic analysis.
        /// Example: "C:\Users\Username\.phantom_security_alert"
        /// </summary>
        public string? DecoyAlertFilePath { get; set; }

        /// <summary>
        /// Seed for deterministic decoy credential generation.
        /// If null (default), credentials are randomly generated each time.
        /// Set a specific seed to generate the same fake credentials consistently.
        /// </summary>
        public int? DecoyRandomSeed { get; set; }

        /// <summary>
        /// Enable tamper detection monitoring.
        /// Default: TRUE.
        /// When enabled, continuously monitors for debuggers, DLL injection, memory manipulation.
        /// </summary>
        public bool EnableTamperDetection { get; set; } = true;

        /// <summary>
        /// Tamper detection check interval in seconds.
        /// Default: 10 seconds.
        /// Lower values provide faster detection but slightly higher CPU usage.
        /// </summary>
        public int TamperDetectionIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// Validates the security options and throws if any are invalid.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when values are outside valid ranges.</exception>
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
