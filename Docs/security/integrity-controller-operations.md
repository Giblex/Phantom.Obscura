# Integrity controller operations

The manifest tool is deliberately separate from the desktop application. The private
key belongs in an offline release secret store or HSM and must never be copied into a
published directory.

```powershell
dotnet run --project tools/PhantomVault.IntegrityTool -- keygen private.pem public.pem
dotnet run --project tools/PhantomVault.IntegrityTool -- sign publish private.pem publish/integrity-manifest.json
dotnet run --project tools/PhantomVault.IntegrityTool -- verify publish public.pem publish/integrity-manifest.json
dotnet run --project tools/PhantomVault.IntegrityTool -- verify-anchors phantom-key-anchors.jsonl
dotnet run --project tools/PhantomVault.IntegrityTool -- evidence watchdog-state evidence.zip
```

Generate the key once, move `private.pem` into the protected release environment, and
embed or installer-protect `public.pem`. Sign only after publish output is final. Run
verification before packaging and again after installer construction.

At runtime, create `TamperEvidentIntegrityLog` with a random 32-byte key protected by
DPAPI machine scope, TPM, or Phantom Key. Construct `IntegrityController` with the
installation root and signed manifest, subscribe to `ChangeDetected`, perform an
initial `Scan()`, then call `Start()`. Critical unknown changes should enter read-only
mode and lock the vault through the existing defence engine.

For Phantom Key anchoring, construct `PhantomKeyIntegrityAnchorProvider` with the
suite broker client and pass it to `IntegrityAnchorCoordinator`. Pin the first
approved transaction-key ID in protected controller policy. Tier 1 requires the
TPM-backed key; Tier 3 also requires the unlocked USB share. Receipts contain no
vault secrets or filenames and can be verified offline.

Every app-owned mutation must call `AuthorizeWrite(relativePath)` immediately before
an atomic replace. Do not grant wildcard or directory-wide authorizations. Keep the
state directory outside the protected installation root where possible and restrict
its ACL to the watchdog service identity and administrators.

Security-critical writers should use `AuthorizeWrite(IntegrityWriteIntent)` and bind
the capability to the exact operation, current hash, resulting hash and maximum byte
length. A result violating any bound is classified as external even if the path had
a live authorization.

Release publishing can generate and install the manifest automatically:

```powershell
dotnet publish src/UI.Desktop/PhantomVault.UI.csproj -c Release `
  -p:RequireIntegritySigning=true `
  -p:IntegritySigningPrivateKey=C:\release-secrets\obscura-private.pem `
  -p:IntegrityPublicKey=C:\release-secrets\obscura-public.pem
```

The signing target runs after AssetShield so the manifest describes the actual bytes
that ship. The installer already carries the complete publish payload, including the
public key and manifest. The private key is never copied.

For managed high-security devices, generate an App Control policy in audit mode first:

```powershell
./tools/New-ObscuraAppControlPolicy.ps1 -PublishRoot publish -OutputPolicy Obscura.xml
```

After compatibility testing, add `-Enforce`. Policy deployment remains an explicit
administrator action because an incorrect enforced policy can prevent Windows code
from running and must not be silently changed by the password manager.

The watchdog runs inside the already independent, auto-start privileged broker service.
It performs full scans, USN-triggered reconciliation, anti-rollback enforcement,
handle-based final-path and hard-link checks, Authenticode verification, loaded-module
inspection, DPAPI-protected logging and Phantom Key anchoring. Its health file is
bridged into `IDefenceEngine`; critical findings force read-only mode and cache scrubbing.

During service installation the SHA-256 identity of `integrity-public-key.pem` is
stored independently under the service-controlled configuration directory. Runtime
verification requires the signed manifest's key ID to match that installer pin; replacing
both the manifest and adjacent public-key file therefore does not establish a new trust root.

Vault unlock uses protocol v2 challenge-response against the authenticated broker pipe.
The response must echo a 256-bit nonce, be no more than 30 seconds old, report a ready
controller and carry a healthy or warning incident state. Missing, stale, malformed,
unprovisioned or compromised responses fail closed while recovery remains available.

Phantom Key transaction envelopes use the `PKT2` format. The TPM signature covers a
domain-separated hash of the action digest, requested factor tier and USB commitment.
Tier 3/4 proofs require a 32-byte USB MAC, preventing a TPM-only proof from being
relabeled as a multi-factor proof. The first approved transaction key is pinned in the
service state and subsequent identity changes fail verification.
