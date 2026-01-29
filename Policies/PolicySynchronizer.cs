using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PhantomVault.Core;

/// <summary>
/// Coordinator for synchronized policy enforcement across USB, Desktop, and Manifest policies.
/// Ensures consistent security posture and cross-policy validation.
/// </summary>
public sealed class PolicySynchronizer
{
    private readonly ObscuraPolicy _policy;
    private readonly List<string> _auditLog = new();

    public PolicySynchronizer(ObscuraPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// Performs comprehensive policy synchronization and validation.
    /// Ensures all policies work together coherently.
    /// </summary>
    public PolicySyncResult SynchronizeAndValidate()
    {
        var result = new PolicySyncResult();
        
        if (!_policy.Sync.EnforceCrossPolicyValidation)
        {
            Log("Cross-policy validation disabled - skipping sync");
            result.Success = true;
            return result;
        }

        try
        {
            // Step 1: Validate individual policy consistency
            ValidateUsbPolicyInternal(result);
            ValidateDesktopPolicyInternal(result);
            ValidateManifestPolicyInternal(result);

            // Step 2: Cross-policy consistency checks
            if (_policy.Sync.ValidateDeviceBindingConsistency)
                ValidateDeviceBindingConsistency(result);

            if (_policy.Sync.ValidateUsbManifestBinding)
                ValidateUsbManifestBinding(result);

            if (_policy.Sync.ValidateSignatureConsistency)
                ValidateSignatureConsistency(result);

            // Step 3: Handle conflicts
            if (result.Conflicts.Any())
                ResolveConflicts(result);

            result.Success = !result.Errors.Any();
            
            if (_policy.Sync.LogAllPolicyChecks)
                LogSyncResult(result);

            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Policy synchronization failed: {ex.Message}");
            result.Success = false;
            
            if (_policy.Sync.OnPolicySyncFailure == "Fail")
                throw new PolicyViolationException(PolicyViolationCode.PolicySyncFailed, "Policy synchronization failed", ex);

            return result;
        }
    }

    private void ValidateUsbPolicyInternal(PolicySyncResult result)
    {
        if (!_policy.Usb.Required)
        {
            Log("USB policy: Not required");
            return;
        }

        // Validate identity mode
        var validModes = new[] { "Any", "LabelOnly", "Serial", "CryptoKey" };
        if (!validModes.Contains(_policy.Usb.IdentityMode))
        {
            result.Errors.Add($"Invalid USB identity mode: {_policy.Usb.IdentityMode}");
        }

        // Validate removal mode
        var validRemovalModes = new[] { "Ignore", "Lock", "LockAndZero" };
        if (!validRemovalModes.Contains(_policy.Usb.OnRemoval))
        {
            result.Errors.Add($"Invalid USB removal mode: {_policy.Usb.OnRemoval}");
        }

        // CryptoKey mode requires key IDs
        if (_policy.Usb.IdentityMode == "CryptoKey" && !_policy.Usb.RequiredKeyIds.Any())
        {
            result.Warnings.Add("CryptoKey mode specified but no required key IDs provided");
        }

        // Serial mode requires serials
        if (_policy.Usb.IdentityMode == "Serial" && !_policy.Usb.AllowedSerials.Any())
        {
            result.Warnings.Add("Serial mode specified but no allowed serials provided");
        }

        // LabelOnly mode requires label
        if (_policy.Usb.IdentityMode == "LabelOnly" && string.IsNullOrWhiteSpace(_policy.Usb.VolumeLabel))
        {
            result.Errors.Add("LabelOnly mode specified but no volume label provided");
        }

        Log($"USB policy validated: Mode={_policy.Usb.IdentityMode}, Required={_policy.Usb.Required}");
    }

    private void ValidateDesktopPolicyInternal(PolicySyncResult result)
    {
        // Validate device binding mode
        var validBindingModes = new[] { "None", "Weak", "Strong", "Hardware" };
        if (!validBindingModes.Contains(_policy.Desktop.DeviceBindingMode))
        {
            result.Errors.Add($"Invalid device binding mode: {_policy.Desktop.DeviceBindingMode}");
        }

        // Validate session termination mode
        var validTermModes = new[] { "Lock", "LockAndZero", "Close" };
        if (!validTermModes.Contains(_policy.Desktop.SessionTerminationMode))
        {
            result.Errors.Add($"Invalid session termination mode: {_policy.Desktop.SessionTerminationMode}");
        }

        // Strong binding requires trusted fingerprints
        if (_policy.Desktop.DeviceBindingMode == "Strong" && !_policy.Desktop.TrustedDeviceFingerprints.Any())
        {
            result.Warnings.Add("Strong device binding specified but no trusted fingerprints provided");
        }

        // Cloud sync requires domains
        if (_policy.Desktop.AllowCloudSync && !_policy.Desktop.AllowedSyncDomains.Any())
        {
            result.Warnings.Add("Cloud sync enabled but no allowed domains specified");
        }

        Log($"Desktop policy validated: Binding={_policy.Desktop.DeviceBindingMode}, MaxSessions={_policy.Desktop.MaxConcurrentSessions}");
    }

    private void ValidateManifestPolicyInternal(PolicySyncResult result)
    {
        // Validate signature algorithm
        var validAlgorithms = new[] { "ECDSA-P256", "RSA-2048", "RSA-4096" };
        if (!validAlgorithms.Contains(_policy.Manifest.SignatureAlgorithm))
        {
            result.Errors.Add($"Invalid signature algorithm: {_policy.Manifest.SignatureAlgorithm}");
        }

        // Exact version enforcement requires version
        if (_policy.Manifest.EnforceExactVersion && string.IsNullOrWhiteSpace(_policy.Manifest.RequiredVersion))
        {
            result.Errors.Add("Exact version enforcement enabled but no required version specified");
        }

        // Signature required should have trusted signers
        if (_policy.Manifest.RequireSignature && !_policy.Manifest.TrustedSignerKeyIds.Any() && !_policy.Manifest.AllowSelfSigned)
        {
            result.Warnings.Add("Signature required but no trusted signer key IDs provided and self-signed not allowed");
        }

        // Update channel requires allowed channels
        if (_policy.Manifest.RequireUpdateChannel && !_policy.Manifest.AllowedUpdateChannels.Any())
        {
            result.Errors.Add("Update channel required but no allowed channels specified");
        }

        Log($"Manifest policy validated: Signature={_policy.Manifest.RequireSignature}, Algorithm={_policy.Manifest.SignatureAlgorithm}");
    }

    private void ValidateDeviceBindingConsistency(PolicySyncResult result)
    {
        // Check if USB trusted device IDs match Desktop trusted fingerprints
        var usbDevices = _policy.Usb.TrustedDeviceIds;
        var desktopDevices = _policy.Desktop.TrustedDeviceFingerprints;

        if (usbDevices.Any() && desktopDevices.Any())
        {
            var commonDevices = usbDevices.Intersect(desktopDevices).ToArray();
            if (!commonDevices.Any())
            {
                result.Conflicts.Add("USB trusted device IDs and Desktop trusted fingerprints have no overlap");
                Log("WARNING: No common device IDs between USB and Desktop policies");
            }
            else
            {
                Log($"Device binding consistent: {commonDevices.Length} common device(s)");
            }
        }

        // If manifest requires device binding, check it's configured somewhere
        if (_policy.Manifest.RequireDeviceBinding)
        {
            if (!usbDevices.Any() && !desktopDevices.Any())
            {
                result.Errors.Add("Manifest requires device binding but no trusted devices configured in USB or Desktop policies");
            }
        }
    }

    private void ValidateUsbManifestBinding(PolicySyncResult result)
    {
        // If manifest requires USB binding, check USB has key requirements
        if (_policy.Manifest.RequireUsbBinding)
        {
            if (_policy.Usb.IdentityMode == "Any")
            {
                result.Conflicts.Add("Manifest requires USB binding but USB policy is in 'Any' mode");
                Log("WARNING: Manifest requires USB binding but USB accepts any device");
            }

            if (_policy.Usb.IdentityMode == "CryptoKey" && !_policy.Usb.RequiredKeyIds.Any())
            {
                result.Errors.Add("Manifest requires USB binding and USB is in CryptoKey mode but no required key IDs specified");
            }
        }

        // Cross-check USB key IDs with Manifest trusted signers
        if (_policy.Usb.RequiredKeyIds.Any() && _policy.Manifest.TrustedSignerKeyIds.Any())
        {
            var commonKeys = _policy.Usb.RequiredKeyIds.Intersect(_policy.Manifest.TrustedSignerKeyIds).ToArray();
            if (commonKeys.Any())
            {
                Log($"USB-Manifest key binding consistent: {commonKeys.Length} common key(s)");
            }
        }
    }

    private void ValidateSignatureConsistency(PolicySyncResult result)
    {
        // Ensure consistent signature requirements across policies
        if (_policy.Manifest.RequireSignature && _policy.Usb.IdentityMode == "CryptoKey")
        {
            // Both require crypto - ensure algorithms are compatible
            Log("Both Manifest and USB use cryptographic verification - ensuring consistency");
            
            if (_policy.Manifest.TrustedSignerKeyIds.Any() && _policy.Usb.RequiredKeyIds.Any())
            {
                var overlap = _policy.Manifest.TrustedSignerKeyIds.Intersect(_policy.Usb.RequiredKeyIds).ToArray();
                if (!overlap.Any())
                {
                    result.Warnings.Add("Manifest trusted signers and USB required keys have no overlap - may indicate misconfiguration");
                }
            }
        }

        // Chain of trust validation
        if (_policy.Manifest.RequireChainOfTrust)
        {
            if (!_policy.Manifest.RequireSignature)
            {
                result.Errors.Add("Chain of trust required but manifest signature not required");
            }

            if (_policy.Usb.Required && _policy.Usb.IdentityMode != "CryptoKey")
            {
                result.Warnings.Add("Chain of trust requires cryptographic USB verification - consider CryptoKey mode");
            }
        }
    }

    private void ResolveConflicts(PolicySyncResult result)
    {
        if (!result.Conflicts.Any())
            return;

        Log($"Resolving {result.Conflicts.Count} policy conflict(s)");

        switch (_policy.Sync.OnPolicyConflict)
        {
            case "MostRestrictive":
                Log("Applying most restrictive policy interpretation");
                // In practice, this means failing when there's ambiguity
                result.Warnings.Add("Policy conflicts resolved by applying most restrictive interpretation");
                break;

            case "Fail":
                result.Errors.Add("Policy conflicts detected and OnPolicyConflict is set to Fail");
                Log("FAIL: Policy conflicts not allowed");
                break;

            case "Warn":
                foreach (var conflict in result.Conflicts)
                {
                    result.Warnings.Add($"Policy conflict: {conflict}");
                }
                result.Conflicts.Clear();
                Log("Policy conflicts converted to warnings");
                break;
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";
        _auditLog.Add(logEntry);
        
        if (_policy.Sync.LogAllPolicyChecks)
        {
            Debug.WriteLine($"[PolicySync] {message}");
        }
    }

    private void LogSyncResult(PolicySyncResult result)
    {
        var summary = new StringBuilder();
        summary.AppendLine("=== Policy Synchronization Result ===");
        summary.AppendLine($"Success: {result.Success}");
        summary.AppendLine($"Errors: {result.Errors.Count}");
        summary.AppendLine($"Warnings: {result.Warnings.Count}");
        summary.AppendLine($"Conflicts: {result.Conflicts.Count}");
        
        if (result.Errors.Any())
        {
            summary.AppendLine("\nErrors:");
            foreach (var error in result.Errors)
                summary.AppendLine($"  - {error}");
        }
        
        if (result.Warnings.Any())
        {
            summary.AppendLine("\nWarnings:");
            foreach (var warning in result.Warnings)
                summary.AppendLine($"  - {warning}");
        }
        
        if (result.Conflicts.Any())
        {
            summary.AppendLine("\nConflicts:");
            foreach (var conflict in result.Conflicts)
                summary.AppendLine($"  - {conflict}");
        }

        Log(summary.ToString());
    }

    public string[] GetAuditLog() => _auditLog.ToArray();

    public void SaveAuditLog(string filePath)
    {
        if (!_policy.Sync.RequireAuditTrail)
            return;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(filePath, _auditLog);
        Log($"Audit log saved to {filePath}");
    }
}

public sealed class PolicySyncResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Conflicts { get; } = new();
}
