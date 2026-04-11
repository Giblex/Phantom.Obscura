using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Determines which features are available in the current build and environment.
    /// This service provides graceful degradation for features that are not fully
    /// implemented or require external dependencies.
    /// </summary>
    public sealed class FeatureAvailabilityService
    {
        private readonly Dictionary<string, FeatureStatus> _featureStatus = new();

        public FeatureAvailabilityService()
        {
            InitializeFeatureStatus();
        }

        private void InitializeFeatureStatus()
        {
            // YubiKey Features
            _featureStatus["YubiKey.Detection"] = new FeatureStatus
            {
                IsAvailable = true,
                IsFullyImplemented = true,
                Description = "Detect if YubiKey hardware is connected",
                RequiredDependencies = new[] { "Yubico.YubiKey NuGet package" }
            };

            _featureStatus["YubiKey.FIDO2"] = new FeatureStatus
            {
                IsAvailable = false, // Not fully implemented
                IsFullyImplemented = false,
                Description = "FIDO2 authentication with YubiKey",
                LimitationMessage = "YubiKey FIDO2 authentication requires additional implementation. " +
                                  "Currently, you can use keyfile + passphrase authentication instead. " +
                                  "Full FIDO2 support will be added in a future release.",
                RequiredDependencies = new[] { "Yubico.YubiKey.Fido2 namespace implementation" },
                DocumentationUrl = "https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/fido2.html"
            };

            _featureStatus["YubiKey.OATH"] = new FeatureStatus
            {
                IsAvailable = false, // Returns null gracefully
                IsFullyImplemented = false,
                Description = "OATH TOTP code generation with YubiKey",
                LimitationMessage = "YubiKey OATH TOTP is not currently configured. " +
                                  "Use the built-in TOTP service for time-based one-time passwords.",
                RequiredDependencies = new[] { "Yubico.YubiKey.Oath namespace implementation" }
            };

            // Biometric Authentication
            _featureStatus["Biometric.WindowsHello"] = new FeatureStatus
            {
                IsAvailable = CheckWindowsHelloAvailable(),
                IsFullyImplemented = false,
                Description = "Windows Hello biometric authentication",
                LimitationMessage = "Windows Hello integration requires additional platform-specific implementation. " +
                                  "Use keyfile + passphrase for secure authentication.",
                RequiredDependencies = new[] { "Windows.Security.Credentials.UI APIs" }
            };

            _featureStatus["Biometric.TouchID"] = new FeatureStatus
            {
                IsAvailable = OperatingSystem.IsMacOS(),
                IsFullyImplemented = false,
                Description = "macOS Touch ID authentication",
                LimitationMessage = "Touch ID integration requires additional macOS-specific implementation. " +
                                  "Use keyfile + passphrase for secure authentication.",
                RequiredDependencies = new[] { "LocalAuthentication framework" },
                DocumentationUrl = "https://developer.apple.com/documentation/localauthentication"
            };

            _featureStatus["Biometric.FaceID"] = new FeatureStatus
            {
                IsAvailable = OperatingSystem.IsMacOS(),
                IsFullyImplemented = false,
                Description = "macOS Face ID authentication",
                LimitationMessage = "Face ID integration requires additional macOS-specific implementation. " +
                                  "Use keyfile + passphrase for secure authentication.",
                RequiredDependencies = new[] { "LocalAuthentication framework" }
            };

            // WebAuthn/FIDO2 (Platform Authenticators)
            _featureStatus["WebAuthn.Platform"] = new FeatureStatus
            {
                IsAvailable = false,
                IsFullyImplemented = false,
                Description = "Platform WebAuthn/FIDO2 authentication",
                LimitationMessage = "WebAuthn platform authenticator support requires additional implementation. " +
                                  "For hardware security keys, YubiKey support is planned for future releases.",
                RequiredDependencies = new[] { "Platform-specific WebAuthn APIs" }
            };

            // VeraCrypt Integration
            _featureStatus["VeraCrypt.Integration"] = new FeatureStatus
            {
                IsAvailable = CheckVeraCryptAvailable(),
                IsFullyImplemented = true,
                Description = "VeraCrypt encrypted container support",
                LimitationMessage = !CheckVeraCryptAvailable()
                    ? "VeraCrypt not found. Please install VeraCrypt or configure the path in settings. " +
                      "Download from: https://www.veracrypt.fr/en/Downloads.html"
                    : null,
                RequiredDependencies = new[] { "VeraCrypt installed on system" },
                DocumentationUrl = "https://www.veracrypt.fr/en/Documentation.html"
            };
        }

        /// <summary>
        /// Checks if a feature is available in the current environment.
        /// </summary>
        public bool IsFeatureAvailable(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) && status.IsAvailable;
        }

        /// <summary>
        /// Checks if a feature is fully implemented (not a stub).
        /// </summary>
        public bool IsFeatureFullyImplemented(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) &&
                   status.IsAvailable &&
                   status.IsFullyImplemented;
        }

        /// <summary>
        /// Gets a user-friendly message explaining why a feature is not available.
        /// Returns null if the feature is available.
        /// </summary>
        public string? GetFeatureLimitationMessage(string featureName)
        {
            if (!_featureStatus.TryGetValue(featureName, out var status))
            {
                return "This feature is not recognized.";
            }

            if (status.IsAvailable && status.IsFullyImplemented)
            {
                return null; // Feature is fully available
            }

            return status.LimitationMessage ?? $"{status.Description} is not available.";
        }

        /// <summary>
        /// Gets detailed status information for a feature.
        /// </summary>
        public FeatureStatus? GetFeatureStatus(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) ? status : null;
        }

        /// <summary>
        /// Gets all features and their availability status.
        /// Useful for diagnostic/settings UI.
        /// </summary>
        public IReadOnlyDictionary<string, FeatureStatus> GetAllFeatures()
        {
            return _featureStatus;
        }

        /// <summary>
        /// Throws an informative exception if a feature is not available.
        /// Use this in service methods to provide clear error messages.
        /// </summary>
        public void ThrowIfNotAvailable(string featureName)
        {
            if (!_featureStatus.TryGetValue(featureName, out var status))
            {
                throw new NotSupportedException($"Feature '{featureName}' is not recognized.");
            }

            if (!status.IsAvailable)
            {
                var message = status.LimitationMessage ?? $"{status.Description} is not available.";
                var exception = new FeatureNotAvailableException(message, featureName);

                if (!string.IsNullOrEmpty(status.DocumentationUrl))
                {
                    exception.Data["DocumentationUrl"] = status.DocumentationUrl;
                }

                if (status.RequiredDependencies?.Any() == true)
                {
                    exception.Data["RequiredDependencies"] = string.Join(", ", status.RequiredDependencies);
                }

                throw exception;
            }

            if (!status.IsFullyImplemented)
            {
                var message = status.LimitationMessage ??
                             $"{status.Description} is not fully implemented yet.";
                var exception = new FeatureNotImplementedException(message, featureName);

                if (!string.IsNullOrEmpty(status.DocumentationUrl))
                {
                    exception.Data["DocumentationUrl"] = status.DocumentationUrl;
                }

                throw exception;
            }
        }

        private bool CheckVeraCryptAvailable()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VeraCrypt", "VeraCrypt.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VeraCrypt", "VeraCrypt.exe")
            };

            return candidates.Any(File.Exists);
        }

        private bool CheckWindowsHelloAvailable()
        {
            if (!OperatingSystem.IsWindows() || !OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                return false;
            }

            try
            {
                var passkeyService = new PasskeyService();
                return passkeyService.IsSupported;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Describes the availability status of a feature.
    /// </summary>
    public sealed class FeatureStatus
    {
        /// <summary>
        /// Whether the feature is available in the current environment.
        /// </summary>
        public bool IsAvailable { get; init; }

        /// <summary>
        /// Whether the feature is fully implemented (not a stub).
        /// </summary>
        public bool IsFullyImplemented { get; init; }

        /// <summary>
        /// Human-readable description of the feature.
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Message explaining why the feature is not available/implemented.
        /// Null if feature is fully available.
        /// </summary>
        public string? LimitationMessage { get; init; }

        /// <summary>
        /// External dependencies required for this feature.
        /// </summary>
        public string[]? RequiredDependencies { get; init; }

        /// <summary>
        /// URL to documentation for implementing or using this feature.
        /// </summary>
        public string? DocumentationUrl { get; init; }
    }

    /// <summary>
    /// Exception thrown when a feature is not available in the current environment.
    /// </summary>
    public sealed class FeatureNotAvailableException : NotSupportedException
    {
        public string FeatureName { get; }

        public FeatureNotAvailableException(string message, string featureName)
            : base(message)
        {
            FeatureName = featureName;
        }
    }

    /// <summary>
    /// Exception thrown when a feature is available but not fully implemented.
    /// </summary>
    public sealed class FeatureNotImplementedException : NotImplementedException
    {
        public string FeatureName { get; }

        public FeatureNotImplementedException(string message, string featureName)
            : base(message)
        {
            FeatureName = featureName;
        }
    }
}
