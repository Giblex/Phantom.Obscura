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
                IsAvailable = true,
                IsFullyImplemented = true,
                Description = "FIDO2 authentication with YubiKey",
                RequiredDependencies = new[] { "Yubico.YubiKey NuGet package" },
                DocumentationUrl = "https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/fido2.html"
            };

            _featureStatus["YubiKey.OATH"] = new FeatureStatus
            {
                IsAvailable = true,
                IsFullyImplemented = true,
                Description = "OATH TOTP code generation and provisioning with YubiKey",
                LimitationMessage = null,
                RequiredDependencies = new[] { "Yubico.YubiKey.Oath namespace (Yubico.YubiKey 1.12.0)" },
                DocumentationUrl = "https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/oath.html"
            };

            // Biometric Authentication
            var helloAvailable = CheckWindowsHelloAvailable();
            _featureStatus["Biometric.WindowsHello"] = new FeatureStatus
            {
                IsAvailable = helloAvailable,
                IsFullyImplemented = helloAvailable,
                Description = "Windows Hello biometric authentication",
                LimitationMessage = helloAvailable
                    ? null
                    : "Windows Hello is not configured on this device. " +
                      "Use keyfile + passphrase for secure authentication.",
                RequiredDependencies = new[] { "Windows 10 1903+ with Windows Hello configured" }
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
            // On Windows, the platform authenticator path is served by WindowsPasskeyService
            // (Windows Hello + Credential Manager). Other platforms have no platform
            // authenticator wired yet; users should fall back to YubiKey FIDO2.
            _featureStatus["WebAuthn.Platform"] = new FeatureStatus
            {
                IsAvailable = helloAvailable,
                IsFullyImplemented = helloAvailable,
                Description = "Platform WebAuthn/FIDO2 authentication",
                LimitationMessage = helloAvailable
                    ? null
                    : "Platform WebAuthn authenticator is only wired on Windows (via Windows Hello). " +
                      "Use a YubiKey for FIDO2 authentication on other platforms.",
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
