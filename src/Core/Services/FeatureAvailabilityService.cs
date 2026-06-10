using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.Core.Services
{

    public sealed class FeatureAvailabilityService
    {
        private readonly Dictionary<string, FeatureStatus> _featureStatus = new();

        public FeatureAvailabilityService()
        {
            InitializeFeatureStatus();
        }

        private void InitializeFeatureStatus()
        {

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

        public bool IsFeatureAvailable(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) && status.IsAvailable;
        }

        public bool IsFeatureFullyImplemented(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) &&
                   status.IsAvailable &&
                   status.IsFullyImplemented;
        }

        public string? GetFeatureLimitationMessage(string featureName)
        {
            if (!_featureStatus.TryGetValue(featureName, out var status))
            {
                return "This feature is not recognized.";
            }

            if (status.IsAvailable && status.IsFullyImplemented)
            {
                return null;
            }

            return status.LimitationMessage ?? $"{status.Description} is not available.";
        }

        public FeatureStatus? GetFeatureStatus(string featureName)
        {
            return _featureStatus.TryGetValue(featureName, out var status) ? status : null;
        }

        public IReadOnlyDictionary<string, FeatureStatus> GetAllFeatures()
        {
            return _featureStatus;
        }

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

    public sealed class FeatureStatus
    {

        public bool IsAvailable { get; init; }

        public bool IsFullyImplemented { get; init; }

        public string Description { get; init; } = string.Empty;

        public string? LimitationMessage { get; init; }

        public string[]? RequiredDependencies { get; init; }

        public string? DocumentationUrl { get; init; }
    }

    public sealed class FeatureNotAvailableException : NotSupportedException
    {
        public string FeatureName { get; }

        public FeatureNotAvailableException(string message, string featureName)
            : base(message)
        {
            FeatureName = featureName;
        }
    }

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

