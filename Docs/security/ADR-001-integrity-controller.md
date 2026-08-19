# ADR-001: Phantom Obscura integrity controller

**Status:** Accepted for staged implementation  
**Date:** 2026-08-18  
**Deciders:** Phantom security engineering

## Context

Phantom Obscura must inventory the files it owns, detect creation, modification,
rename and deletion, distinguish application-authorized writes from changes of
unknown origin, and preserve evidence in a tamper-evident history. Existing
`TamperDetectionService` only compares the current executable with an in-memory
startup hash. `AuditService` records vault activity, while AssetShield encrypts
packaged assets. None provides durable release provenance.

The controller cannot truthfully identify a human editor from filesystem metadata.
It can prove that a write was covered by a short-lived capability issued by the app;
everything else is classified as external or unknown. Administrator or kernel-level
attackers remain capable of disabling user-mode monitoring.

## Decision

Use five cooperating controls:

1. A release-time inventory containing normalized relative paths, sizes, timestamps
   and SHA-256 digests.
2. An ECDSA P-256 signature over a deterministic representation of that inventory.
   The signing private key stays outside the shipped application.
3. Short-lived, single-use authorized-write capabilities for runtime provenance.
4. `FileSystemWatcher` for low-latency signals plus periodic complete scans as the
   source of truth.
5. An HMAC-SHA-256 chained local event log. Its key must come from DPAPI, TPM, or a
   Phantom Key in production; it must not be stored beside the log in plaintext.

The portable implementation lives in `PhantomVault.Core.Services.Integrity`. The
eventual Windows service hosts it independently of the UI and feeds critical
integrity mismatches into the existing defence engine as `IntegrityMismatch`.

## Options considered

| Option | Detection | Provenance | Resists UI termination | Complexity |
|---|---:|---:|---:|---:|
| UI-only startup hash | Low | No | No | Low |
| UI-hosted controller (implemented foundation) | High while running | Yes | No | Medium |
| Dedicated LocalService watchdog (target) | High | Yes | Yes | High |
| Kernel minifilter | Highest | Strong process evidence | Yes | Very high; driver risk |

The service is the target because it improves independence without introducing a
kernel driver and its signing, deployment, stability, and attack-surface costs.

## Consequences

- Offline modifications are found by the next full scan.
- Watcher duplication or overflow does not determine truth; scans reconcile state.
- Authorizations must be issued immediately before atomic app writes and are consumed
  once. A capability indicates approved app workflow, not metaphysical proof of the
  process that changed the bytes.
- Release updates require a newly signed manifest.
- Audit-chain verification detects rewriting but cannot prevent deletion. Anchoring
  the latest chain head to TPM/Phantom Key is a subsequent hardening step.

## Action items

- [x] Add signed manifest creation and verification.
- [x] Add complete inventory scans and watcher signals.
- [x] Add authorized-write provenance and authenticated hash-chain logging.
- [x] Add unit tests for file changes, signature mutation and log mutation.
- [x] Add a release-build tool that signs manifests using an offline CI key.
- [x] Host the controller in the independently running privileged Windows service.
- [x] Protect the audit authentication key with machine-scope DPAPI and service ACL inheritance.
- [x] Connect critical events to `IDefenceEngine` read-only and cache-scrubbing actions.
- [x] Add USN Journal continuity reconciliation and Authenticode verification on Windows.
- [x] Bind audit heads to independently verifiable Phantom Key TPM signatures.
- [x] Support Tier 3 anchors that also require the unlocked Phantom Key USB share.
- [x] Bind authorized writes to operation, before/after hashes, size and expiry.
- [x] Reject reparse points, out-of-root final paths and hard links using file handles.
- [x] Add signed-release anti-rollback state.
- [x] Verify loaded in-root modules and their Authenticode trust.
- [x] Generate optional audit/enforcement Windows App Control policies.
- [x] Add evidence export and offline Phantom Key anchor verification.
