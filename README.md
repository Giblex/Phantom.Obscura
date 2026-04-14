# Phantom.Obscura Review README

Source-grounded review of the current `Phantom.Obscura` repository as inspected on 2026-04-02.

## Portfolio Overview

Phantom.Obscura is not a single thin desktop app. It is a small security suite built around one end-user product:

- `src/UI.Desktop`: the Avalonia desktop app users run.
- `src/Core`: the main vault, manifest, policy, import/export, USB binding, and security orchestration layer.
- `src/Crypto`: the low-level zero-knowledge and crypto primitives library.
- `src/Autofill`: the native host and autofill backend.
- `src/Platform`: Windows and platform integration services.
- `Policies`: signed policy and USB trust enforcement assets.
- `tests/PhantomVault.Core.Tests`: the primary verification suite.
- `tests/PhantomVault.UI.Tests`: the UI verification surface.
- `Tools/Obscura.Keysmith`: internal signing and certificate tool.

## Purpose

Obscura is aiming at a higher-assurance local vault product rather than a generic password manager. Its differentiators are USB/device binding, signed policy enforcement, manifest hardening, anti-tamper ideas, and a more ambitious trust model than typical local-vault competitors.

## Reviewed Projects

| Project | Purpose | Score | Production Readiness | Main Blocker |
|---|---|---:|---:|---|
| `src/UI.Desktop` | User-facing vault app | 7/10 | 6/10 | UI still contains placeholders, recovery shims, and several throwing converters |
| `src/Core` | Vault orchestration and security services | 7/10 | 6/10 | Integrity and unlock paths still contain placeholder logic in important areas |
| `src/Crypto` | Zero-knowledge primitives and file crypto | 8/10 | 7/10 | Strongest subsystem, but still depends on upstream core integrations being completed |
| `src/Autofill` | Native host and autofill backend | 5/10 | 4/10 | Browser extension and full deployment surface are still missing |
| `src/Platform` | Platform detection, passkeys, auto-type | 5/10 | 4/10 | Passkey story is overstated and mobile targeting is incomplete |
| `tests/PhantomVault.Core.Tests` | Core verification suite | 7/10 | 6/10 | Good breadth, but several "integration" tests are placeholders |
| `tests/PhantomVault.UI.Tests` | UI verification suite | 3/10 | 3/10 | Only one file with four assertions |
| `Tools/Obscura.Keysmith` | Signing and cert utility | 6/10 | 5/10 | Sensitive cert material in repo and obsolete cert loading |

## Cross-Project Strengths

- Clear product ambition. Obscura is trying to compete on trust posture, not just vault CRUD.
- Strong crypto baseline in the lower-level library: Argon2id, AEAD, zeroization, key wrapping, stream/file encryption.
- Signed policy, USB binding, anti-rollback, and manifest AAD binding are meaningful differentiators.
- The desktop app is visually richer than most security tooling and has a broad UX surface already built.
- The codebase is modular enough to review by project, even though some responsibilities still bleed across boundaries.

## Cross-Project Weaknesses

- Several security-critical paths still contain placeholder or simulated behavior.
- Marketing/docs often overstate readiness relative to what the code currently guarantees.
- Policy sample artifacts are out of sync with runtime schema.
- Recovery integration is still shimmed in the desktop app when the external recovery module is absent.
- UI coverage is very thin compared with the breadth of the desktop surface.
- The solution/test split is easy to drift because the main solution does not include all verification projects.
- Repo hygiene needs work: stale scripts, stale docs, stray temp artifact files, and operational cert material in-tree.

## Key Verified Findings

- `src/Core/Services/PhantomContainerService.cs` still writes placeholder integrity values for payload hash and HMAC.
- `src/Core/Services/ZeroKnowledge/ZkVaultService.cs` still contains an unlock path that "assumes success" instead of proving the derived key immediately.
- `src/Core/Services/RecoveryCodeService.cs` still uses a PBKDF2 placeholder where Argon2-backed recovery-code protection is implied.
- `src/Core/Services/YubiKeyService.Implementation.cs` still throws `FeatureNotImplementedException` for FIDO2 register/auth/reset flows.
- `src/UI.Desktop/Views/SignInDialog.axaml` still exposes Windows Hello / Google / Microsoft sign-in buttons as placeholders.
- `src/UI.Desktop/Services/IntegratedRecoveryServiceStub.cs`, `RecoveryDeveloperModeStub.cs`, and `Views/RecoveryPanelStub.cs` are still active shim layers (note: `RecoveryPanelStub.cs` now displays informational UI with "Recovery Not Available" title and descriptive guidance instead of being an empty stub).
- `src/UI.Desktop/Views/MainWindow.axaml.cs` — `OpenAutoFillSettings_Click` now has try/catch for `InvalidOperationException` with a user-friendly "Not Available" dialog (prevents crash when AutoFill DI services are not registered).
- `src/UI.Desktop/Converters` still contains multiple `ConvertBack` implementations that throw `NotImplementedException`.
- `src/Autofill` has meaningful backend work, but the documented browser extension layer is not present in the current tree.
- `Tools/Obscura.Keysmith/certs` contains a `.crt` and `.pfx` in the repository.
- `src/Core/Services/tmpclaude-ab42-cwd` is a stray temp artifact file.

## Linked Review Files

- [Core review](./src/Core/README.md)
- [Desktop UI review](./src/UI.Desktop/README.md)
- [Autofill review](./src/Autofill/README.md)
- [Platform review](./src/Platform/README.md)
- [Crypto review](./src/Crypto/README.md)
- [Core tests review](./tests/PhantomVault.Core.Tests/README.md)
- [UI tests review](./tests/PhantomVault.UI.Tests/README.md)
- [Keysmith review](./Tools/Obscura.Keysmith/README.md)

## Ordered Next Steps

### Tier 0 - Security Truthfulness And Integrity

1. Replace placeholder payload hash and HMAC logic in `PhantomContainerService`.
2. Make unlock authoritative in `ZkVaultService` by verifying a fixed encrypted verifier before declaring success.
3. Replace the recovery-code PBKDF2 placeholder with Argon2id-backed verification.
4. Collapse policy files onto one versioned schema and reject stale artifacts at load time.
5. Tighten `UsbKeyFile.LoadAndVerify` so signature presence is enforced by the helper, not only by callers.

### Tier 1 - Remove Overstated Security Surfaces

1. Either implement real YubiKey FIDO2 flows or downgrade the UI and docs to "not available yet".
2. Either implement a real Windows Hello / passkey path end-to-end or stop presenting it as a near-ready feature.
3. Re-scope `SecurityCheckService` as diagnostics, or upgrade it into a real verification gate.
4. Remove or quarantine developer bypass paths from normal developer docs and release expectations.

### Tier 2 - Desktop Product Hardening

1. Replace desktop recovery shims with real integration or clearly gate the feature behind availability checks. *(Partial: `RecoveryPanelStub.cs` now shows informational UI instead of being empty.)*
2. Remove placeholder sign-in options from `SignInDialog` until they are functional.
3. Replace throwing `ConvertBack` implementations with safe no-op behavior where reverse conversion is not supported.
4. Audit the desktop app for any other placeholder-only UX that still appears user-facing. *(Partial: `OpenAutoFillSettings_Click` now catches missing DI services with user-friendly dialog.)*

### Tier 3 - Autofill And Platform Completion

1. Build the missing browser extension and native-host registration assets for the autofill stack.
2. Decide whether `Autofill` and `Platform` are genuinely cross-platform; retarget or simplify accordingly.
3. Replace simulated passkey behavior with a true supported implementation or tighter scope.

### Tier 4 - Verification And Repo Hygiene

1. Convert placeholder KeePass tests into fixture-backed tests with real `.kdbx` coverage.
2. Expand UI tests beyond `CredentialViewModel`.
3. Add the missing projects or documented split logic to the main solution workflow.
4. Remove `tmpclaude-*` artifacts and stale scripts.
5. Remove sensitive cert material from the repo and modernize Keysmith certificate loading.

### Tier 5 - Differentiation Work Worth Investing In

1. Finish the strongest differentiators first: signed policy trust, anti-rollback, USB cryptographic identity, and manifest-bound AAD.
2. Split the overgrown core assembly into cleaner domain-focused assemblies.
3. Build a genuinely high-trust onboarding and recovery story instead of stitching recovery through stubs.
4. Add fixture-backed red-team style tests around tamper detection, manifest corruption, USB mismatch, and downgrade attempts.

## Overall Verdict

Obscura has more originality and stronger security ambition than most indie password managers. Its best parts are legitimately interesting. It is not production-ready yet, though, because several of the trust guarantees it wants to claim are still simulated, placeholder-backed, or only partially enforced.

## Recently Applied Fixes

| Fix | File | Change |
|-----|------|--------|
| AutoFill crash prevention | `MainWindow.axaml.cs` | `OpenAutoFillSettings_Click` wrapped in try/catch with "Not Available" dialog when DI services are missing |
| Recovery stub communication | `RecoveryPanelStub.cs` | Empty stub replaced with StackPanel containing "Recovery Not Available" title and descriptive guidance |
| Credential storage warnings | `WindowsHelloSettingsViewModel.cs`, `PasskeySettingsViewModel.cs` | `#warning SECURITY` directives added above DPAPI credential storage methods flagging migration to Credential Manager |
| Dark theme input styling | `PreVaultTheme.Dark.axaml` | Added `InputBackgroundBrush` and `InputForegroundBrush` for readable text inputs in dark mode |
| Font family fallback | `PreVaultTheme.axaml` | Applied `"Segoe UI Variable, Segoe UI"` fallback pattern for consistent typography |
