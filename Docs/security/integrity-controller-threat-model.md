# Integrity controller threat model

## Assets and trust boundaries

Protected assets are Obscura executables, assemblies, configuration, policy and
security-critical runtime files; the signed release manifest; controller state;
the audit key; and the audit history. Trust boundaries exist between the release
signing environment, installer, non-elevated UI, watchdog service, Windows kernel,
and administrators or other local processes.

## Threats and controls

| Threat | Control | Residual risk |
|---|---|---|
| Binary or configuration replacement | Signed manifest and full SHA-256 scan | A privileged attacker can suppress a user-mode response |
| New DLL/config dropped into install root | Unexpected-file detection | Exclusion rules must remain narrow |
| Manifest rewritten to bless malware | Offline ECDSA signature | Shipped public key can be patched with the verifier if code signing/service protection fails |
| Audit entry rewritten or reordered | Sequenced HMAC hash chain | Complete log deletion requires an externally anchored chain head to detect |
| Watcher event lost or duplicated | Periodic full reconciliation | Detection is delayed until the scan |
| App write mislabeled as hostile | Short-lived single-use authorization | Crashes between authorization and write create harmless stale capabilities until expiry |
| External write mislabeled as authorized | Exact-path, short-lived, single-use capability | A racing attacker with local access could target the authorized path; atomic write APIs and service-side client identity reduce this |
| Controller killed with UI | Separate service in the target architecture | Service administrators can still stop or replace it |
| Symlink/reparse redirection | Canonical root and relative path validation; install ACLs | Reparse-point rejection must be added before protecting attacker-writable trees |
| Signing/audit key theft | Offline release key; DPAPI/TPM/Phantom Key for audit key | Compromise of the signing pipeline defeats release provenance |

## Security invariants

- The release signing private key is never present in an installed client.
- A manifest is never accepted before signature verification.
- Paths are relative, normalized, contained by the protected root, and compared using
  the platform-appropriate case rules.
- Watcher events are alerts, not the authoritative state database.
- Unknown origin is reported honestly; the system does not claim to identify a person.
- Integrity failure must fail closed for secret-revealing or executable-loading flows,
  while preserving a recovery and evidence-export path.

## Deployment phases

Phase 1 is the implemented core library and tests. Phase 2 adds release tooling and
UI-hosted observation. Phase 3 moves ownership to a least-privilege Windows service,
hardens ACLs and keys, and connects the defence engine. Phase 4 adds USN Journal
reconciliation, Authenticode validation and TPM/Phantom-Key chain-head anchoring.
