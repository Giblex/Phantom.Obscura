# Phantom.Obscura — Security Posture

> **Status:** Living document. Updated as part of the multidisciplinary audit
> remediation. Sections flagged **OPEN** describe known weaknesses that must be
> resolved before any production release.

## 1. Threat scope (one-line summary)

Phantom.Obscura is a zero-knowledge local-first credential vault. The threat
model assumes a hostile network, a partially-trusted host OS, and an attacker
with arbitrary code-execution capability inside the browser tab. The vault must
remain confidential and integrity-protected under all three conditions.

## 2. Hard rules (immutable, see `/memories/phantom-obscura.md`)

1. **Mandatory USB keyfile + optional password + optional device binding.**
   The keyfile is required to unlock the vault on every platform. The password
   is *optional*. Never describe it as required. Never write "master password"
   as a required factor. Device binding is desktop-only and optional.
2. **Zero-knowledge.** No secrets leave the device unencrypted. Memory is
   zeroized via `CryptographicOperations.ZeroMemory` immediately after use.
   All secret comparisons go through `CryptographicOperations.FixedTimeEquals`.
3. **Liquid-glass UI is brand identity.** Do not regress it.

## 3. Cryptographic primitives

| Purpose | Primitive | Notes |
| --- | --- | --- |
| Symmetric AEAD | AES-256-GCM | All on-disk vault frames |
| Password KDF | Argon2id (Isopoh) | Per-context parameters in `KdfDefaults` |
| Signing | Ed25519 | Manifest + attestor outputs |
| Local key wrap | DPAPI (per-user) | Windows-only; documented scope below |
| Key exchange | X25519 | Pairing + recovery |
| PQC KEM | ML-KEM-768 | Hybrid mode in recovery |
| HKDF | `System.Security.Cryptography.HKDF` | `Hkdf.Sha256` wrapper only |

Argon2id parameter sets live in
[src/Crypto/Primitives/KdfDefaults.cs](src/Crypto/Primitives/KdfDefaults.cs).
Inline `new KdfParams { ... }` for hardcoded values is forbidden; pull from
`KdfDefaults` or from configuration (`EngineOptions`, `VaultOptions`).

## 4. Internet exposure surface (gated)

Every outbound network request must pass through `IInternetGateway`. The
gateway enforces explicit user consent, an audit log, host allowlisting, and
TLS SPKI pinning where a policy configures pins (pinning is optional per
policy — a host with pins has them enforced; a host without proceeds on
public-CA TLS validation). Two integrations currently route through it:

| Service | Hosts | Policy file |
| --- | --- | --- |
| Flaticon icon downloader | `api.flaticon.com`, `cdn-icons-png.flaticon.com` | `FlaticonGatewayPolicy` |
| HaveIBeenPwned (k-anonymity) | `api.pwnedpasswords.com`, `haveibeenpwned.com` | `HibpGatewayPolicy` |

**SPKI pinning — live pins in place.** Both policies ship real
base64 SHA-256(SubjectPublicKeyInfo) pins extracted from a live TLS handshake.
Two pins per host: current leaf (primary) and the issuing intermediate
(backup), so leaf rotation does not brick the gateway. The dead
`cdn-icons-svg.flaticon.com` host (NXDOMAIN, never referenced from code) was
removed from the Flaticon policy.

**Pin rotation.** Re-extract when the issuing intermediate changes (LE YE1 for
Flaticon; Google Trust Services WE1 for HIBP) or when leaf cert rotation makes
the primary pin stale. Recipe:

```powershell
# Replace <host> with api.flaticon.com, cdn-icons-png.flaticon.com,
# api.pwnedpasswords.com, or haveibeenpwned.com.
openssl s_client -connect <host>:443 -servername <host> </dev/null 2>$null `
  | openssl x509 -pubkey -noout `
  | openssl pkey -pubin -outform DER `
  | openssl dgst -sha256 -binary `
  | openssl base64
```

Update the leaf and intermediate constants in the matching `*GatewayPolicy.cs`.
Always keep at least two pins per host (current + backup) for rotation
tolerance.

## 5. Browser autofill surface

The browser extension talks to the desktop app via a named pipe
(`NativeHostPipeServer`) with these defences:

- `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`
- Connecting process is identified via `GetNamedPipeClientProcessId` and the
  executable path must match the running Obscura application image. The browser
  launches that image in native-messaging mode; the browser itself is not the
  pipe client. Release builds also require valid Authenticode trust; Debug
  permits the exact unsigned local application image.
- Credential lookup requires an exact hostname match (case-insensitive, ignoring
  a trailing DNS dot). Empty hostnames and substring/sibling/subdomain matches
  do not return credentials.
- The autofill origin allowlist is read from `%AppData%\PhantomVault\` and is
  **DPAPI-sealed** to `autofill-origins.dpapi` (per-user scope). Legacy
  plaintext `autofill-origins.json` is auto-migrated and best-effort deleted
  on first read. See
  [src/UI.Desktop/NativeMessagingMode.cs](src/UI.Desktop/NativeMessagingMode.cs).
- The page-injected content script uses per-page-load randomised tokens for
  its injection guard property and the suggestion-chip element ID. Page
  scripts cannot enumerate a fixed `__phantomvault_*` identifier to fingerprint
  presence. See [src/Extension/content.js](src/Extension/content.js).

## 6. Update channel

**RESOLVED — Update verification (OPEN-2).** The desktop app now has a fully
gated in-process update channel. The pipeline never trusts the network — every
trust decision flows from the embedded Ed25519 public key in
[src/Core/Services/Update/UpdatePublicKey.cs](src/Core/Services/Update/UpdatePublicKey.cs)
which is unprovisioned (32-byte zero placeholder) by default. While
unprovisioned, the verifier fails closed and the entire pipeline is inert.

### 6.1 Pipeline

`CheckAsync` → `DownloadAndStageAsync` (Obscura) → out-of-process
`giblex-installer.exe --apply-update` (after Obscura exits).

| Stage | Gate | Failure |
|---|---|---|
| Open network | `IInternetGateway` grant for `updates.giblex.com` from `UpdateGatewayPolicy` (no SPKI pin — pinning optional for this channel; integrity gate is the signed manifest below) | request denied → state remains `Idle` |
| Manifest fetch | `Content-Length` ≤ 64 KB, signature ≤ 256 B | `BadManifest` |
| Signature | Ed25519 over raw manifest bytes (no re-canonicalisation) | `BadSignature` |
| Schema | `schema == 1`, `channel ∈ {stable, beta}`, well-formed `Version` | `BadManifest` / `ChannelMismatch` |
| Version | `manifest.version > installed`; if present, `installed >= minPreviousVersion` | `VersionNotNewer` / `UpgradePathBlocked` |
| Asset URL | `https://updates.giblex.com/...` exactly | `AssetHostMismatch` |
| Download | `Content-Length == manifest.asset.size`, streamed under cap | `AssetSizeMismatch` |
| Asset hash | SHA-256(asset bytes) == `manifest.asset.sha256` | `AssetHashMismatch` |
| Authenticode | `WinVerifyTrust` chain valid; if pinned: signer Subject + SHA-256 thumbprint match `manifest.authenticode` | `AuthenticodeSubjectMismatch` / `AuthenticodePinMismatch` |

The staged directory contains the raw `manifest.json`, `manifest.json.sig`,
the verified asset, and a JSON `info.json` handoff. **Every gate is re-run by
the installer at apply time** against the staged bytes — see
[Giblex Installer/src/Giblex.Installer/Services/UpdateApplyMode.cs](../Giblex%20suite/Giblex%20Installer/src/Giblex.Installer/Services/UpdateApplyMode.cs).
The signing key and verifier source (the seven files in
[src/Core/Services/Update/](src/Core/Services/Update/) excluding the
service-layer types) are **linked** into the installer project — single source
of truth — so a Phase-E key rotation only touches `UpdatePublicKey.cs`.

### 6.2 Trust boundary invariants

- The Ed25519 public key lives in exactly one file
  (`UpdatePublicKey.cs`) — enforced by CI hard-rule.
- `UpdateVerifier` always consults `UpdatePublicKey.IsProvisioned` before
  attempting verify — enforced by CI hard-rule (regression guard).
- SPKI pinning is optional for the update channel: `UpdateGatewayPolicy`
  ships no pin and relies on public-CA TLS plus the Ed25519-signed manifest.
  A real pin can be added later as defence-in-depth. (The legacy all-zero
  placeholder-pin literal remains CI-rejected outside `*GatewayPolicy.cs`.)
- All network access for the update channel goes through the gateway with
  audited grant/revoke; `OfflineMode` halts the entire pipeline.

### 6.3 Key provisioning checklist

1. Generate Ed25519 keypair on an air-gapped signing box.
2. Replace `Placeholder` in `UpdatePublicKey.cs` with the 32-byte public key.
3. (Optional, recommended) Add real SPKI pin(s) for `updates.giblex.com`
   (leaf + intermediate) to `SpkiPinsByHost` in `UpdateGatewayPolicy.cs` to
   enable pinning as defence-in-depth; the gateway enforces them once present.
4. Rebuild both Phantom.Obscura and Giblex.Installer — they pick the new
   key/pin up automatically via the linked source.
5. Sign the release Authenticode chain and pin it in the manifest's
   `authenticode` block.

## 7. DPAPI scope (Windows)

All DPAPI calls in this codebase use `DataProtectionScope.CurrentUser`. This
means:

- A compromised user account can decrypt vault metadata sealed with DPAPI.
- A different user on the same machine cannot.
- Backup/restore of the sealed blob to a different user profile will fail.

Per-user scope is intentional and is the correct choice for a single-user
credential vault. Sealed-with-machine scope is explicitly rejected because it
allows any process on the host to decrypt.

## 8. Reporting

Security issues: open a private security advisory on
<https://github.com/Giblex/Phantom.Obscura/security/advisories>. Do not file
public issues for vulnerabilities.

## 9. Container and helper lifecycle controls

- Only encrypted-header Obscura v2 volumes and Phantom v4 containers are accepted.
  Plaintext `OBSCUR01` volumes and Phantom v1-v3 containers are rejected before
  extraction or output creation. No automatic legacy conversion is performed.
- Volume extraction validates every entry range and canonical destination path
  before creating output files.
- Phantom container KDF costs are limited to 1-16 iterations and at most 1 GiB
  of memory. Private headers are limited to 64 KiB and manifest-footer ciphertext
  to 64 MiB, with remaining-file checks before allocation.
- GV-CZK headers, TOCs, and entry ciphertext are limited to 64 KiB, 16 MiB, and
  256 MiB respectively. Larger entries require a streaming format rather than
  unbounded in-memory allocation. Updated TOC size is checked before appending
  entry data, so size rejection leaves existing entries intact.
- Decrypted Phantom payload blocks are zeroized in `finally`, including failed
  and cancelled output writes.
- Boot ROM image loading checks the opened file's length before allocating:
  at most 1 MiB of program plus the 136-byte signed-container overhead. Reads
  require the exact length and reject growth; malformed marker key and salt
  lengths are rejected before key derivation.
- TLS pinning checks the leaf and intermediates in the validated certificate
  chain; trust anchors are excluded. Name or chain validation errors still fail
  closed. An intermediate pin therefore supports leaf-key rotation.
- On actual desktop exit, the app disables helper activation, sends the
  authenticated broker shutdown request without reinstall/restart retries, and
  waits for SCM to report stopped before disposal (15-second bound). Failure is
  logged explicitly. Closing to the autofill tray is not an application exit.
