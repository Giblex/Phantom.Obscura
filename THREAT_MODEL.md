# Phantom.Obscura — Threat Model (STRIDE)

> **Status:** Living document. Maintained in lockstep with
> [SECURITY.md](SECURITY.md). The hard rules in section 2 of SECURITY.md
> override any conflicting analysis below.

## 0. Assets

| Asset | Sensitivity | Location |
| --- | --- | --- |
| Vault contents (creds, notes, TOTP seeds) | Critical | `*.gv` on disk, RAM during unlock |
| Master KEK derived from password+keyfile | Critical | RAM only |
| USB keyfile material | Critical | Removable media, RAM during unlock |
| Recovery code Argon2id hash | High | Vault metadata |
| Autofill origin allowlist | Medium | `%AppData%\PhantomVault\autofill-origins.dpapi` |
| HIBP / Flaticon API responses | Low | Memory only, never persisted |

## 1. Trust boundaries

```
┌──────────────────────┐  named pipe   ┌──────────────────────┐
│   Browser extension  │ ───────────── │  PhantomVault.UI     │
│ (page script + bg)   │ (per-user)    │  (desktop app)       │
└──────────────────────┘               └──────────────────────┘
        │                                          │
        │ stdio (native messaging)                 │ AES-GCM frames
        ▼                                          ▼
┌──────────────────────┐               ┌──────────────────────┐
│   Chrome host process│               │   *.gv on local disk │
└──────────────────────┘               └──────────────────────┘
                                                   │
                                                   │ gated outbound (IInternetGateway)
                                                   ▼
                                       ┌──────────────────────┐
                                       │ api.pwnedpasswords.com│
                                       │ api.flaticon.com      │
                                       └──────────────────────┘
```

Boundaries (untrusted side first):

1. Page DOM → content script (isolated world).
2. Content script → background service worker.
3. Background service worker → native messaging host (`chrome.exe` stdio).
4. Native messaging host → desktop app (named pipe, current-user only).
5. Desktop app → local disk (DPAPI per-user for metadata; AEAD for vault).
6. Desktop app → internet (gateway-mediated, consent + audit + pinning).

## 2. STRIDE per surface

### 2.1 Vault file on disk

| Threat | Mitigation |
| --- | --- |
| **S**poofing — attacker writes a fake vault | Header has authenticated KDF params; unwrap fails on tamper |
| **T**ampering | Every chunk is AES-GCM with framed AAD; tag mismatch aborts |
| **R**epudiation | n/a (single-user device) |
| **I**nfo disclosure — file copied off device | KEK is per-vault; without USB keyfile + (optional) password the file is opaque |
| **D**enial of service — truncation/corruption | Atomic write + journaled migration |
| **E**levation of privilege | n/a (file is not executable) |

### 2.2 Browser autofill pipe

| Threat | Mitigation |
| --- | --- |
| **S**poofing — non-browser process connects | `GetNamedPipeClientProcessId` + process-name allowlist |
| **T**ampering — pipe MITM by another user | `PipeOptions.CurrentUserOnly` rejects cross-user connects |
| **R**epudiation | Origin + timestamp captured in audit log |
| **I**nfo disclosure — origin allowlist leaks | DPAPI-sealed (per-user) |
| **D**enial of service — slowloris client | Async I/O + per-connection timeout |
| **E**levation — credential exfil by hostile site | Origin allowlist gate + chip requires explicit click |

### 2.3 Internet egress (gateway)

| Threat | Mitigation |
| --- | --- |
| **S**poofing — DNS hijack to attacker server | TLS SPKI pinning (**OPEN** — placeholder pins) |
| **T**ampering — response injection | TLS + AEAD on app payloads where applicable |
| **R**epudiation — silent network calls | `IInternetGateway` audit log + per-feature consent |
| **I**nfo disclosure — password sent to HIBP | k-anonymity range API (first 5 hex of SHA-1 only) |
| **D**enial of service — slow upstream | Per-request timeout + circuit breaker in gateway |
| **E**levation — host allowlist bypass | Host-set is policy-constant; not user-editable at runtime |

### 2.4 Browser extension content script

| Threat | Mitigation |
| --- | --- |
| **S**poofing — page script impersonates the chip | Chip element ID + injection-flag are per-page-load random |
| **T**ampering — page steals credentials before fill | Fill is gated on user click on the chip, not auto-fill |
| **R**epudiation | n/a |
| **I**nfo disclosure — page enumerates `window` properties | Injection flag uses `defineProperty` with `enumerable: false` |
| **D**enial of service — page crashes the extension | Try/catch around all `sendMessage` calls |
| **E**levation | Manifest v3 isolated world; no eval; no remote code |

### 2.5 Update channel

**Out of scope until an in-process updater exists.** When implemented it must
satisfy section 6 of SECURITY.md (SHA-256 + Ed25519 manifest + Authenticode).

## 3. Out-of-scope assumptions

- The host OS kernel is not compromised.
- The user has not installed a malicious browser extension with `host_permissions: <all_urls>` that targets the page DOM directly (we cannot defend against a peer extension that already has full DOM access).
- Physical hardware attacks (cold boot, DMA over Thunderbolt) are not in scope. The keyfile + (optional) password requirement makes a powered-off device acceptably safe; a powered-on unlocked device is by design out of scope.

## 4. Open items (cross-reference)

| ID | Item | Tracked in |
| --- | --- | --- |
| OPEN-1 | Replace `PlaceholderPin` in `FlaticonGatewayPolicy` and `HibpGatewayPolicy` with real SPKI pins | SECURITY.md §4 |
| OPEN-2 | Implement update channel verification (SHA-256 + Ed25519 + Authenticode) | SECURITY.md §6 |
| OPEN-3 | Invert keyfile-mandatory contracts across services + add CI grep / Roslyn analyzer enforcement | `/memories/session/phantom-obscura-audit-execution.md` Phase 4 |
| OPEN-4 | AntiKeyloggingService — replace marketing copy with real input-dispatch-latency probe + clipboard watermark + `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` | audit Phase 5 |
