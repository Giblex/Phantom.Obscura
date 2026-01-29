using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core;
using static ObscuraPolicy;

namespace PhantomVault.UI.ViewModels.Settings
{
    /// <summary>
    /// ViewModel for policy validation and configuration UI.
    /// Provides safe defaults and allows users to customize security policies.
    /// </summary>
    public partial class PolicySettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _usbRequired;

        [ObservableProperty]
        private bool _requireRemovable;

        [ObservableProperty]
        private string _usbIdentityMode = "Any";

        [ObservableProperty]
        private string? _volumeLabel;

        [ObservableProperty]
        private string _minUsbStandard = "USB2";

        [ObservableProperty]
        private bool _requireMfa;

        [ObservableProperty]
        private bool _requirePassphrase = true;

        [ObservableProperty]
        private bool _requireKeyfile;

        [ObservableProperty]
        private bool _allowBiometrics = true;

        [ObservableProperty]
        private int _sessionTimeoutMinutes = 15;

        [ObservableProperty]
        private bool _autoLockOnMinimize;

        [ObservableProperty]
        private bool _autoLockOnScreenLock = true;

        [ObservableProperty]
        private bool _autoLockOnIdle = true;

        [ObservableProperty]
        private int _idleTimeoutMinutes = 5;

        [ObservableProperty]
        private int _maxFailedAttempts = 5;

        [ObservableProperty]
        private bool _throttleUnlockAttempts = true;

        [ObservableProperty]
        private bool _usePostQuantum;

        [ObservableProperty]
        private bool _auditEnabled = true;

        [ObservableProperty]
        private bool _autoBackupEnabled;

        [ObservableProperty]
        private bool _policyModified;

        [ObservableProperty]
        private string? _statusMessage;

        [ObservableProperty]
        private bool _showingRecommendations;

        [ObservableProperty]
        private ObservableCollection<string> _validationErrors = new();

        private string? _currentPolicyPath;

        public List<string> UsbIdentityModes { get; } = new() { "Any", "LabelOnly", "Serial", "CryptoKey" };
        public List<string> UsbStandards { get; } = new() { "USB2", "USB3", "USB3PLUS" };

        public PolicySettingsViewModel()
        {
            LoadSafeDefaults();
        }

        /// <summary>
        /// Loads safe default policy settings suitable for new users.
        /// Minimal restrictions with security recommendations.
        /// </summary>
        [RelayCommand]
        public void LoadSafeDefaults()
        {
            // USB Configuration - Not required by default (user-friendly)
            UsbRequired = false;
            RequireRemovable = false;
            UsbIdentityMode = "Any";
            VolumeLabel = null;
            MinUsbStandard = "USB2";

            // Security Settings - Balanced defaults
            RequireMfa = false; // Can be enabled later
            RequirePassphrase = true; // Always require at least a password
            RequireKeyfile = false; // Optional for beginners
            AllowBiometrics = true; // Enable convenience features

            // Session Management - Secure but reasonable
            SessionTimeoutMinutes = 15;
            AutoLockOnMinimize = false; // Don't interrupt workflow
            AutoLockOnScreenLock = true; // Lock when system locks
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 5;

            // Authentication Protection
            MaxFailedAttempts = 5;
            ThrottleUnlockAttempts = true;

            // Advanced Features - Disabled by default
            UsePostQuantum = false; // Can enable when ready
            AutoBackupEnabled = false; // Let user configure first

            // Audit - Enabled for security tracking
            AuditEnabled = true;

            PolicyModified = false;
            StatusMessage = "Safe default policy loaded. These settings are recommended for new users.";
            ShowingRecommendations = true;
        }

        /// <summary>
        /// Loads high-security policy settings for advanced users.
        /// Maximum security with all protections enabled.
        /// </summary>
        [RelayCommand]
        public void LoadHighSecurityPolicy()
        {
            // USB Configuration - Required and validated
            UsbRequired = true;
            RequireRemovable = true;
            UsbIdentityMode = "Serial";
            MinUsbStandard = "USB3";

            // Security Settings - All protections enabled
            RequireMfa = true;
            RequirePassphrase = true;
            RequireKeyfile = true;
            AllowBiometrics = false; // Require explicit auth

            // Session Management - Aggressive locking
            SessionTimeoutMinutes = 5;
            AutoLockOnMinimize = true;
            AutoLockOnScreenLock = true;
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 2;

            // Authentication Protection - Strict
            MaxFailedAttempts = 3;
            ThrottleUnlockAttempts = true;

            // Advanced Features - All enabled
            UsePostQuantum = true;
            AutoBackupEnabled = true;
            AuditEnabled = true;

            PolicyModified = true;
            StatusMessage = "High-security policy loaded. All security features enabled.";
            ShowingRecommendations = false;
        }

        /// <summary>
        /// Loads an existing policy from file.
        /// </summary>
        [RelayCommand]
        public void LoadPolicyFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    StatusMessage = $"Policy file not found: {filePath}";
                    return;
                }

                var json = File.ReadAllText(filePath);
                var policy = JsonSerializer.Deserialize<ObscuraPolicy>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (policy == null)
                {
                    StatusMessage = "Failed to parse policy file.";
                    return;
                }

                ApplyPolicyToUI(policy);
                _currentPolicyPath = filePath;
                PolicyModified = false;
                StatusMessage = $"Policy loaded from: {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading policy: {ex.Message}";
            }
        }

        /// <summary>
        /// Saves current policy settings to file.
        /// </summary>
        [RelayCommand]
        public void SavePolicy(string? filePath = null)
        {
            try
            {
                var policy = CreatePolicyFromUI();
                
                var targetPath = filePath ?? _currentPolicyPath ?? 
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PhantomVault", "custom_policy.json");

                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(policy, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(targetPath, json);
                
                _currentPolicyPath = targetPath;
                PolicyModified = false;
                StatusMessage = $"Policy saved to: {Path.GetFileName(targetPath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving policy: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates current policy settings.
        /// </summary>
        [RelayCommand]
        public void ValidatePolicy()
        {
            var result = new ValidationResult { IsValid = true };
            ValidationErrors.Clear();

            // Validate USB settings
            if (UsbRequired && string.IsNullOrWhiteSpace(UsbIdentityMode))
            {
                result.IsValid = false;
                result.Errors.Add("USB identity mode must be specified when USB is required.");
            }

            if (UsbIdentityMode == "LabelOnly" && string.IsNullOrWhiteSpace(VolumeLabel))
            {
                result.IsValid = false;
                result.Errors.Add("Volume label must be specified for LabelOnly mode.");
            }

            // Validate authentication requirements
            if (!RequirePassphrase && !RequireKeyfile && !RequireMfa)
            {
                result.IsValid = false;
                result.Errors.Add("At least one authentication method must be required.");
            }

            // Validate timeouts
            if (SessionTimeoutMinutes < 1 || SessionTimeoutMinutes > 1440)
            {
                result.IsValid = false;
                result.Errors.Add("Session timeout must be between 1 and 1440 minutes.");
            }

            if (IdleTimeoutMinutes < 1 || IdleTimeoutMinutes > SessionTimeoutMinutes)
            {
                result.IsValid = false;
                result.Errors.Add("Idle timeout must be between 1 minute and session timeout.");
            }

            // Validate failed attempts
            if (MaxFailedAttempts < 1 || MaxFailedAttempts > 10)
            {
                result.IsValid = false;
                result.Errors.Add("Max failed attempts must be between 1 and 10.");
            }

            // Add warnings for weak settings
            if (!RequireMfa)
            {
                result.Warnings.Add("MFA is not required. Consider enabling for better security.");
            }

            if (SessionTimeoutMinutes > 30)
            {
                result.Warnings.Add("Long session timeout may reduce security.");
            }

            if (!UsePostQuantum)
            {
                result.Warnings.Add("Post-quantum encryption is not enabled. Enable for future-proof security.");
            }

            // Update ValidationErrors and status
            ValidationErrors.Clear();
            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                {
                    ValidationErrors.Add(error);
                }
                StatusMessage = $"Policy validation failed with {result.Errors.Count} errors.";
            }
            else
            {
                PolicyModified = false;
                StatusMessage = "Policy validation passed. " + 
                    (result.Warnings.Count > 0 ? $"{result.Warnings.Count} warnings found." : "No issues found.");
            }
        }

        /// <summary>
        /// Applies policy object to UI properties.
        /// </summary>
        private void ApplyPolicyToUI(ObscuraPolicy policy)
        {
            // USB settings
            UsbRequired = policy.Usb.Required;
            RequireRemovable = policy.Usb.RequireRemovable;
            UsbIdentityMode = policy.Usb.IdentityMode ?? "Any";
            VolumeLabel = policy.Usb.VolumeLabel;
            MinUsbStandard = policy.Usb.MinStandard ?? "USB2";

            // Security settings (if available in policy)
            // Note: These may need to be added to ObscuraPolicy model
            RequirePassphrase = true; // Default
            AllowBiometrics = true;
            AutoLockOnScreenLock = true;
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 5;
            MaxFailedAttempts = 5;
            ThrottleUnlockAttempts = true;
            AuditEnabled = true;
        }

        /// <summary>
        /// Creates policy object from UI properties.
        /// </summary>
        private ObscuraPolicy CreatePolicyFromUI()
        {
            return new ObscuraPolicy
            {
                Usb = new UsbPolicy
                {
                    Required = UsbRequired,
                    RequireRemovable = RequireRemovable,
                    IdentityMode = UsbIdentityMode,
                    VolumeLabel = VolumeLabel,
                    MinStandard = MinUsbStandard,
                    AllowedSerials = Array.Empty<string>(),
                    RequiredKeyIds = Array.Empty<string>()
                }
                // Add other policy sections as needed
            };
        }

        partial void OnPolicyModifiedChanged(bool value)
        {
            if (value)
            {
                StatusMessage = "Policy has been modified. Save changes to apply.";
            }
        }

        // Property change handlers to track modifications
        partial void OnUsbRequiredChanged(bool value) => PolicyModified = true;
        partial void OnRequireRemovableChanged(bool value) => PolicyModified = true;
        partial void OnUsbIdentityModeChanged(string value) => PolicyModified = true;
        partial void OnVolumeLabelChanged(string? value) => PolicyModified = true;
        partial void OnMinUsbStandardChanged(string value) => PolicyModified = true;
        partial void OnRequireMfaChanged(bool value) => PolicyModified = true;
        partial void OnRequirePassphraseChanged(bool value) => PolicyModified = true;
        partial void OnRequireKeyfileChanged(bool value) => PolicyModified = true;
        partial void OnAllowBiometricsChanged(bool value) => PolicyModified = true;
        partial void OnSessionTimeoutMinutesChanged(int value) => PolicyModified = true;
        partial void OnAutoLockOnMinimizeChanged(bool value) => PolicyModified = true;
        partial void OnAutoLockOnScreenLockChanged(bool value) => PolicyModified = true;
        partial void OnAutoLockOnIdleChanged(bool value) => PolicyModified = true;
        partial void OnIdleTimeoutMinutesChanged(int value) => PolicyModified = true;
        partial void OnMaxFailedAttemptsChanged(int value) => PolicyModified = true;
        partial void OnThrottleUnlockAttemptsChanged(bool value) => PolicyModified = true;
        partial void OnUsePostQuantumChanged(bool value) => PolicyModified = true;
        partial void OnAuditEnabledChanged(bool value) => PolicyModified = true;
        partial void OnAutoBackupEnabledChanged(bool value) => PolicyModified = true;
    }

    /// <summary>
    /// Result of policy validation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}
