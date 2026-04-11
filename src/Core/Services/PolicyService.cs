using System;
using System.Security.Cryptography;
using System.Text.Json;
using PhantomVault.Core;

namespace PhantomVault.Core.Services;

/// <summary>
/// Enforces signed security policies at runtime.
/// Uses ObscuraPolicy + PolicyEngine + PolicySynchronizer for enforcement.
/// </summary>
public class PolicyService : IDisposable
{
    private readonly ObscuraPolicy _policy;
    private readonly PolicyEngine _engine;
    private readonly PolicySynchronizer _synchronizer;
    private readonly bool _signatureVerified;

    /// <summary>Gets the current security policy.</summary>
    public ObscuraPolicy Policy => _policy;

    /// <summary>True when a signature block was present and verified.</summary>
    public bool SignatureVerified => _signatureVerified;

    /// <summary>
    /// Initializes PolicyService by verifying (when present) the policy signature with the root verifier.
    /// </summary>
    /// <param name="rootVerifier">ECDsa instance for signature verification (from certificate). Required if policy contains signature.</param>
    /// <param name="policyJson">Policy JSON content (signed or unsigned)</param>
    /// <param name="requireSignature">If true, throws when signature is missing or invalid</param>
    /// <exception cref="ArgumentNullException">When rootVerifier is null but policy contains signature</exception>
    /// <exception cref="CryptographicException">When signature verification fails or is required but missing</exception>
    public PolicyService(ECDsa? rootVerifier, string policyJson, bool requireSignature = true)
    {
        var hasSignature = HasSignature(policyJson);

        if (hasSignature)
        {
            // SECURITY: If policy has signature, verifier MUST be provided
            if (rootVerifier == null)
            {
                throw new ArgumentNullException(nameof(rootVerifier),
                    "Policy contains signature but no verifier provided. This indicates a security misconfiguration.");
            }

            PolicyVerifier.VerifyPolicy(policyJson, rootVerifier);
            _signatureVerified = true;
        }
        else if (requireSignature)
        {
            throw new CryptographicException("Policy signature missing while signature is required.");
        }

        _policy = ObscuraPolicy.FromVerifiedJson(policyJson);
        _engine = new PolicyEngine(_policy, rootVerifier);
        _synchronizer = new PolicySynchronizer(_policy);
    }

    private static bool HasSignature(string policyJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(policyJson);
            return doc.RootElement.TryGetProperty("signature", out _);
        }
        catch (JsonException)
        {
            // Invalid JSON means no valid signature
            return false;
        }
        catch (Exception ex)
        {
            // SECURITY: Log unexpected exceptions for debugging
            // Note: This shouldn't happen with valid policy JSON
            System.Diagnostics.Debug.WriteLine($"Unexpected error checking for policy signature: {ex.Message}");
            return false;
        }
    }

    public bool IsUsbRequired() => _policy.Usb.Required;
    public bool IsDesktopSyncAllowed() => _policy.Desktop.AllowDesktopSync;
    public bool AreDebuggersAllowed() => _policy.Desktop.AllowDebuggers;
    public bool AreVirtualMachinesBlocked() => _policy.Desktop.BlockVirtualMachines;

    public void EnforceDebuggerPolicy()
    {
        if (!_policy.Desktop.AllowDebuggers && System.Diagnostics.Debugger.IsAttached)
        {
            throw new System.Security.SecurityException(
                "Policy violation: Debugger detected. This application cannot run under a debugger per security policy.");
        }
    }

    public void EnforceVirtualMachinePolicy()
    {
        // SECURITY: BlockVirtualMachines = true means VMs are NOT allowed
        if (_policy.Desktop.BlockVirtualMachines)
        {
            bool isVM = Security.VirtualMachineDetection.IsRunningInVirtualMachine();
            if (isVM)
            {
                string vmType = Security.VirtualMachineDetection.GetVirtualMachineType();
                throw new System.Security.SecurityException(
                    $"Policy violation: Virtual machines are blocked by security policy. Detected: {vmType}");
            }
        }
    }

    public (System.IO.DriveInfo? drive, string? volumeSerial) EnforceUsbPolicy()
    {
        return _engine.EnforceUsbAtStartup();
    }

    public void EnforceDesktopSyncPolicy()
    {
        if (!_policy.Desktop.AllowDesktopSync)
        {
            throw new System.Security.SecurityException(
                "Policy violation: Desktop synchronization is not permitted by security policy.");
        }
    }

    public void EnforceManifestPolicy(string manifestVersion, bool manifestSignatureValid)
    {
        _engine.EnforceManifestPolicy(manifestVersion, manifestSignatureValid);
    }

    public PolicySyncResult SynchronizeAndValidate() => _synchronizer.SynchronizeAndValidate();

    public void Dispose()
    {
        // Currently stateless; future: hook disposal if engine allocates resources.
    }
}
