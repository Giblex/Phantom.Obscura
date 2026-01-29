# YubiKey FIDO2 Implementation Guide

**Status**: Intentionally Not Implemented - Security-Critical Feature  
**Date**: December 28, 2025  
**SDK**: Yubico.YubiKey v1.12.0 (Already Installed)

---

## Overview

YubiKey FIDO2 authentication is **intentionally stubbed** in PhantomObscuraV6 because implementing it incorrectly could introduce critical security vulnerabilities. This guide provides a comprehensive roadmap for proper implementation.

## Current State

### What's Working ✅

- **Device Detection**: `IsTokenPresent()` - Enumerates connected YubiKeys
- **Device Info**: `GetDeviceInfo()` - Returns serial number, firmware, capabilities
- **FIDO2 Support Check**: `SupportsFido2()` - Validates FIDO2 capability
- **Configuration Check**: `IsConfigured()` - Verifies device is accessible

### What's Not Implemented ⚠️

- **Credential Registration**: Creating FIDO2 credentials during vault setup
- **Authentication**: Actual FIDO2 assertion verification
- **PIN Management**: Setting/verifying YubiKey PIN
- **Credential Storage**: Persisting credential ID and public key in manifest

## Why It's Not Implemented

The placeholder implementation that was removed would have returned `SHA256(deviceSerial + challenge)`, which is **completely insecure** and could be trivially bypassed. The current approach of throwing `NotImplementedException` with detailed instructions is the correct security posture.

---

## Implementation Roadmap

### Phase 1: Credential Registration (Vault Creation)

**File**: `src/Core/Services/YubiKeyService.cs`

```csharp
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;

public class RegisterFido2CredentialResult
{
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public string AttestationFormat { get; set; } = string.Empty;
}

public RegisterFido2CredentialResult RegisterCredential(
    string userId, 
    string userName,
    string relyingPartyId = "phantomvault.local")
{
    // Step 1: Verify YubiKey presence
    var device = YubiKeyDevice.FindAll().FirstOrDefault();
    if (device == null)
        throw new InvalidOperationException("No YubiKey detected.");
    
    if (!device.HasFeature(YubiKeyFeature.Fido2Application))
        throw new InvalidOperationException("YubiKey does not support FIDO2.");

    // Step 2: Open FIDO2 session
    using (var fido2 = device.GetFido2Application())
    {
        // Step 3: Verify PIN (required for credential creation)
        if (fido2.AuthenticatorInfo.RequiresPinForCredentialManagement)
        {
            // Prompt user for PIN (implement in UI layer)
            string pin = PromptForPin(); // TODO: Implement in UI
            fido2.VerifyPin(Encoding.UTF8.GetBytes(pin));
        }

        // Step 4: Generate client data hash (challenge)
        byte[] clientDataHash = new byte[32];
        RandomNumberGenerator.Fill(clientDataHash);

        // Step 5: Create MakeCredential parameters
        var rp = new RelyingParty(relyingPartyId);
        var user = new UserEntity(
            Encoding.UTF8.GetBytes(userId),
            userName,
            userName); // displayName

        var parameters = new MakeCredentialParameters
        {
            RelyingParty = rp,
            User = user,
            ClientDataHash = clientDataHash,
            CredentialParameters = new[] 
            { 
                new PubKeyCredParam(CoseAlgorithmIdentifier.ES256) // ECDSA with SHA-256
            },
            Options = new AuthenticatorOptions
            {
                ResidentKey = true // Store credential on YubiKey
            }
        };

        // Step 6: Create credential (will require user touch)
        var credential = fido2.MakeCredential(parameters);

        // Step 7: Extract and return credential data
        return new RegisterFido2CredentialResult
        {
            CredentialId = credential.AuthData.CredentialId.Id,
            PublicKey = credential.AuthData.CredentialPublicKey.GetBytes(),
            AttestationFormat = credential.Format
        };
    }
}
```

### Phase 2: Update Vault Manifest Model

**File**: `src/Core/Models/VaultManifest.cs`

Add properties to store YubiKey credential:

```csharp
public class VaultManifest
{
    // ... existing properties ...

    /// <summary>
    /// YubiKey FIDO2 credential ID (if YubiKey MFA is enabled).
    /// </summary>
    public byte[]? YubiKeyCredentialId { get; set; }

    /// <summary>
    /// YubiKey FIDO2 public key for signature verification.
    /// </summary>
    public byte[]? YubiKeyPublicKey { get; set; }

    /// <summary>
    /// YubiKey device serial number (for device verification).
    /// </summary>
    public int? YubiKeySerialNumber { get; set; }
}
```

### Phase 3: Authentication Implementation

Replace the `Authenticate` method in `YubiKeyService.cs`:

```csharp
public byte[] Authenticate(byte[] challenge)
{
    // Step 1: Validate YubiKey presence
    var device = YubiKeyDevice.FindAll().FirstOrDefault();
    if (device == null)
        throw new InvalidOperationException("No YubiKey detected.");

    // Step 2: Verify it's the correct device (optional but recommended)
    // Compare device.SerialNumber with manifest.YubiKeySerialNumber

    // Step 3: Load credential ID from manifest (passed as parameter in real implementation)
    // For now, throw if not configured
    // byte[] credentialId = GetStoredCredentialId(); // From manifest

    using (var fido2 = device.GetFido2Application())
    {
        // Step 4: Verify PIN if required
        if (fido2.AuthenticatorInfo.RequiresPinForAssertions)
        {
            string pin = PromptForPin(); // TODO: Implement in UI
            fido2.VerifyPin(Encoding.UTF8.GetBytes(pin));
        }

        // Step 5: Create GetAssertion parameters
        var parameters = new GetAssertionParameters
        {
            RelyingPartyId = "phantomvault.local",
            ClientDataHash = challenge,
            AllowList = new[]
            {
                new PublicKeyCredentialDescriptor
                {
                    Id = credentialId,
                    Type = "public-key"
                }
            },
            Options = new AuthenticatorOptions
            {
                UserPresence = true // Require touch
            }
        };

        // Step 6: Get assertion (will require user touch)
        var assertion = fido2.GetAssertion(parameters);

        // Step 7: Return signature for verification
        return assertion.Signature;
    }
}
```

### Phase 4: Signature Verification

```csharp
public bool VerifySignature(
    byte[] signature,
    byte[] challenge,
    byte[] publicKey)
{
    try
    {
        // Parse COSE public key
        var coseKey = CoseKey.Parse(publicKey);
        
        // Extract coordinates for ECDSA verification
        var x = coseKey.GetX();
        var y = coseKey.GetY();

        // Create ECDsa verifier
        var ecParams = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = x,
                Y = y
            }
        };

        using var ecdsa = ECDsa.Create(ecParams);
        
        // Verify signature
        return ecdsa.VerifyData(challenge, signature, HashAlgorithmName.SHA256);
    }
    catch
    {
        return false;
    }
}
```

---

## Integration Points

### 1. Vault Creation Flow

**File**: `src/UI.Desktop/ViewModels/VaultCreationViewModel.cs`

```csharp
if (useYubiKey)
{
    var yubiKey = new YubiKeyService();
    
    // Register credential
    var result = yubiKey.RegisterCredential(
        userId: _currentUser.Email,
        userName: _currentUser.DisplayName);
    
    // Store in manifest
    manifest.YubiKeyCredentialId = result.CredentialId;
    manifest.YubiKeyPublicKey = result.PublicKey;
    manifest.YubiKeySerialNumber = yubiKey.GetDevice()?.SerialNumber;
    manifest.RequiresYubiKey = true;
}
```

### 2. Vault Unlock Flow

**File**: `src/UI.Desktop/ViewModels/UnlockViewModel.cs`

```csharp
if (manifest.RequiresYubiKey)
{
    var yubiKey = new YubiKeyService();
    
    // Generate challenge
    byte[] challenge = new byte[32];
    RandomNumberGenerator.Fill(challenge);
    
    // Get signature
    byte[] signature = yubiKey.Authenticate(challenge);
    
    // Verify signature
    if (!yubiKey.VerifySignature(signature, challenge, manifest.YubiKeyPublicKey))
    {
        throw new InvalidOperationException("YubiKey authentication failed.");
    }
    
    // Continue with vault unlock...
}
```

---

## Security Considerations

### Critical Requirements ✅

1. **Always verify PIN** before credential operations
2. **Require user presence** (touch) for all authentications
3. **Validate device serial number** to prevent token substitution
4. **Use resident keys** to avoid credential ID disclosure
5. **Implement timeout handling** for PIN retries
6. **Zero sensitive data** from memory after use

### Common Pitfalls ❌

1. **Don't** use placeholder implementations in production
2. **Don't** skip signature verification
3. **Don't** allow unlimited PIN attempts
4. **Don't** store credentials unencrypted
5. **Don't** assume YubiKey is always present

---

## Testing Strategy

### Unit Tests

- Device detection with/without YubiKey
- Credential registration validation
- Signature verification with known test vectors

### Integration Tests

- Full registration → authentication flow
- PIN retry limit enforcement
- Device hot-plug detection

### Manual Tests

- Remove YubiKey during operation
- Wrong device substitution
- Firmware compatibility (5.0+)

---

## Documentation References

- **Yubico .NET SDK**: <https://docs.yubico.com/yesdk/>
- **FIDO2 Programming Guide**: <https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/fido2.html>
- **WebAuthn Spec**: <https://www.w3.org/TR/webauthn-2/>
- **CTAP2 Spec**: <https://fidoalliance.org/specs/fido-v2.0-ps-20190130/fido-client-to-authenticator-protocol-v2.0-ps-20190130.html>

---

## Conclusion

YubiKey FIDO2 authentication is a **security-critical feature** that must be implemented correctly or not at all. The current approach of throwing `NotImplementedException` with detailed guidance is the appropriate security posture until a complete, reviewed implementation can be deployed.

**Recommendation**: Complete implementation requires:

1. Dedicated security review
2. Comprehensive testing with physical YubiKeys
3. PIN management UI
4. Error handling for all failure modes
5. Documentation for end users

**Timeline**: 2-3 weeks for full implementation and testing

**Alternative**: Continue using fully implemented authentication methods (Windows Hello, Passkeys, TOTP) until YubiKey support can be properly implemented.
