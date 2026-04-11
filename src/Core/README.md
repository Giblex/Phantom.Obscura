# PhantomVault.Core Review

## Overview

`PhantomVault.Core` is the orchestration layer for Obscura. It owns manifest lifecycle, vault operations, policy enforcement wiring, USB binding, import/export, intrusion response, password health, passkey abstractions, auto-inject policy, and much of the product's security behavior.

## Tech Stack

- `.NET 9`
- C#
- Avalonia referenced for some shared types
- BouncyCastle
- Argon2 via `Isopoh.Cryptography.Argon2`
- Serilog
- Shared project references to `GiblexVault.Security.ZK` and `Phantom.Sync`

## Architecture

- Service-heavy design with most behavior under `Services/`
- Models split across product, security, attestor, and domain-store concepts
- Policy enforcement is compiled in from the repository `Policies/` folder
- USB binding, manifest validation, and vault services are all first-class concerns

## Strengths

- Strong ambition around trust enforcement, not just secret storage
- Good use of Argon2id, AEAD, and zeroization
- Manifest design binds more context than most local-vault tools
- USB/device binding work is unusually ambitious
- Broad feature surface: imports, sharing, intrusion response, password health, anti-tamper, AI-safe redaction

## Competitor Comparison

Score: `7/10`

Why:

- Stronger local trust ideas than many KeePass-style tools
- More ambitious USB/policy binding than Bitwarden-style local exports
- Still behind mature products because too many critical guarantees are not yet fully authoritative

## Production Readiness

Score: `6/10`

Blockers:

- Container integrity path still uses placeholder values
- Zero-knowledge unlock path still contains "assume success" behavior
- Recovery-code hashing path still uses a placeholder implementation
- Hardware-backed auth flows remain incomplete
- Some availability checks report optimism instead of evidence

## Outstanding TODOs And Stubs

- `PhantomVault.Core.csproj` suppresses XML-doc debt and explicitly carries TODOs for public API docs
- `Models/EncryptionAlgorithm.cs` still marks post-quantum support as stub-only
- `Services/YubiKeyService.Implementation.cs` has unimplemented register/auth/reset flows
- `Services/PasskeyService.cs` still exposes unsupported placeholder messaging for non-Windows paths
- `Services/RecoveryCodeService.cs` contains `Argon2Placeholder`

## Broken Or Risky Areas

- `Services/PhantomContainerService.cs` writes placeholder payload hash and HMAC values
- `Services/ZeroKnowledge/ZkVaultService.cs` does not fully prove the derived key before unlock succeeds
- `Services/SecurityCheckService.cs` behaves more like a diagnostic simulation than a real trust gate
- `Policies/base_policy.json`, `Policies/base_policy.signed.json`, and `Policies/safe_default_policy.json` are not aligned with the runtime policy model
- `Policies/UsbKeyFile.cs` still depends on callers to enforce signature presence consistently
- `FeatureAvailabilityService` assumes VeraCrypt is available instead of proving it

## Security And Privacy Notes

- Strongest subsystem in core is still the underlying cryptographic posture and context-aware manifest protection
- Weakest subsystem is "security truthfulness": several surfaces present stronger guarantees than the code actually proves today
- AI-safe placeholder masking is present and is directionally good, but must not be confused with hard security boundaries

## Unused Or Unnecessary Files

- `Services/tmpclaude-ab42-cwd` is a temp breadcrumb and should not be in source
- Some policy artifacts appear obsolete relative to the live model and should either be versioned or removed

## Improvement Ideas

- Make unlock authoritative with a dedicated verifier record
- Split core into narrower assemblies: vault, policy, autofill policy, intrusion/defence, recovery/domain stores
- Add corruption and forgery tests around containers and manifests
- Replace assumption-based availability checks with probe-based checks
- Make policy schema versioning explicit and fail closed on mismatches

## Next Steps

1. Replace placeholder integrity logic in `PhantomContainerService`
2. Fix unlock proof in `ZkVaultService`
3. Replace `Argon2Placeholder` in `RecoveryCodeService`
4. Align all policy artifacts with one schema and one parser
5. Finish or hide incomplete YubiKey and passkey flows
