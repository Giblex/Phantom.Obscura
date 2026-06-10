using System;
using System.Security.Cryptography;
using System.Text.Json;
using PhantomVault.Core;

namespace PhantomVault.Core.Services;

public class PolicyService : IDisposable
{
    private readonly ObscuraPolicy _policy;
    private readonly PolicyEngine _engine;
    private readonly PolicySynchronizer _synchronizer;
    private readonly bool _signatureVerified;

    public ObscuraPolicy Policy => _policy;

    public bool SignatureVerified => _signatureVerified;

    public PolicyService(ECDsa? rootVerifier, string policyJson, bool requireSignature = true)
    {
        var hasSignature = HasSignature(policyJson);

        if (hasSignature)
        {

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

            return false;
        }
        catch (Exception ex)
        {

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

    }
}

