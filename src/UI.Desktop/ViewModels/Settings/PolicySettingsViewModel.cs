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

        [RelayCommand]
        public void LoadSafeDefaults()
        {

            UsbRequired = false;
            RequireRemovable = false;
            UsbIdentityMode = "Any";
            VolumeLabel = null;
            MinUsbStandard = "USB2";

            RequireMfa = false;
            RequirePassphrase = true;
            RequireKeyfile = false;
            AllowBiometrics = true;

            SessionTimeoutMinutes = 15;
            AutoLockOnMinimize = false;
            AutoLockOnScreenLock = true;
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 5;

            MaxFailedAttempts = 5;
            ThrottleUnlockAttempts = true;

            UsePostQuantum = false;
            AutoBackupEnabled = false;

            AuditEnabled = true;

            PolicyModified = false;
            StatusMessage = "Safe default policy loaded. These settings are recommended for new users.";
            ShowingRecommendations = true;
        }

        [RelayCommand]
        public void LoadHighSecurityPolicy()
        {

            UsbRequired = true;
            RequireRemovable = true;
            UsbIdentityMode = "Serial";
            MinUsbStandard = "USB3";

            RequireMfa = true;
            RequirePassphrase = true;
            RequireKeyfile = true;
            AllowBiometrics = false;

            SessionTimeoutMinutes = 5;
            AutoLockOnMinimize = true;
            AutoLockOnScreenLock = true;
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 2;

            MaxFailedAttempts = 3;
            ThrottleUnlockAttempts = true;

            UsePostQuantum = true;
            AutoBackupEnabled = true;
            AuditEnabled = true;

            PolicyModified = true;
            StatusMessage = "High-security policy loaded. All security features enabled.";
            ShowingRecommendations = false;
        }

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
                Serilog.Log.Error(ex, "[PolicySettings] Failed to load policy settings.");
                StatusMessage = "Policy settings could not be loaded. Try again.";
            }
        }

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
                Serilog.Log.Error(ex, "[PolicySettings] Failed to save policy settings.");
                StatusMessage = "Policy settings could not be saved. Review the values and try again.";
            }
        }

        [RelayCommand]
        public void ValidatePolicy()
        {
            var result = new ValidationResult { IsValid = true };
            ValidationErrors.Clear();

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

            if (!RequirePassphrase && !RequireKeyfile && !RequireMfa)
            {
                result.IsValid = false;
                result.Errors.Add("At least one authentication method must be required.");
            }

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

            if (MaxFailedAttempts < 1 || MaxFailedAttempts > 10)
            {
                result.IsValid = false;
                result.Errors.Add("Max failed attempts must be between 1 and 10.");
            }

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

        private void ApplyPolicyToUI(ObscuraPolicy policy)
        {

            UsbRequired = policy.Usb.Required;
            RequireRemovable = policy.Usb.RequireRemovable;
            UsbIdentityMode = policy.Usb.IdentityMode ?? "Any";
            VolumeLabel = policy.Usb.VolumeLabel;
            MinUsbStandard = policy.Usb.MinStandard ?? "USB2";

            RequirePassphrase = true;
            AllowBiometrics = true;
            AutoLockOnScreenLock = true;
            AutoLockOnIdle = true;
            IdleTimeoutMinutes = 5;
            MaxFailedAttempts = 5;
            ThrottleUnlockAttempts = true;
            AuditEnabled = true;
        }

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

            };
        }

        partial void OnPolicyModifiedChanged(bool value)
        {
            if (value)
            {
                StatusMessage = "Policy has been modified. Save changes to apply.";
            }
        }

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

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

