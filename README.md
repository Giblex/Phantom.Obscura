# Phantom Obscura (PhantomVault)

A local-first, privacy-preserving password vault and security suite. Phantom Obscura stores credentials encrypted on-device using Argon2id-derived keys and AES-GCM authenticated encryption — no cloud sync required. It ships a Windows desktop app, an Android app (Avalonia and a parallel MAUI track), and a browser extension that communicates with the local vault over native messaging.

---

## Projects

`PhantomVault.sln` contains the four projects that ship in the production desktop build (`Core`, `UI.Desktop`, `Autofill`, `Platform`). The Crypto library, Android heads, dev tools, and tests live in the same repo and build via direct `ProjectReference`s, `Tools/Tools.sln`, or per-project `dotnet build`.

| Project | Path | Target | Role |
|---|---|---|---|
| `GiblexVault.Security.ZK` | `src/Crypto` | `net9.0` | Cryptographic primitives: Argon2id, AES-GCM, HKDF, key wrapping, ZK vault format, recovery material |
| `PhantomVault.Core` | `src/Core` | `net9.0;net9.0-android` | Vault services: encryption, ZK vault, containers, passkeys, TOTP, import/export, USB binding, policy, security defence. Android builds substitute `Services/Mobile/*Mobile.cs` for desktop-only services. |
| `PhantomVault.Platform` | `src/Platform` | `net9.0` | Platform-specific services behind interfaces |
| `PhantomVault.Autofill` | `src/Autofill` | `net9.0` | Autofill backend: native messaging host, Windows + Android autofill, form field detection |
| `PhantomVault.UI` (Desktop) | `src/UI.Desktop` | `net9.0-windows10.0.19041.0` | Windows desktop app (Avalonia 11) |
| `PhantomVault.UI` (Android/Avalonia) | `src/UI.Android.Avalonia` | `net9.0-android` | Android app (Avalonia) — shares assembly name with desktop to reuse `avares://` resources |
| `PhantomVault.Android` (MAUI) | `src/UI.Android` | `net9.0-android` | Android app (MAUI) — full page set, parallel track |
| Browser Extension | `src/Extension` | — | MV3 extension (Chrome/Edge/Firefox) — relays autofill via native messaging |
| `Obscura.Keysmith` | `Tools/Obscura.Keysmith` | `net9.0` | Dev tool for generating and inspecting vault keys and signing policies (`Tools/Tools.sln`) |
| `PhantomVault.Core.Tests` | `tests/PhantomVault.Core.Tests` | `net9.0` | Unit and integration tests for Core and Crypto |
| `PhantomVault.UI.Tests` | `tests/PhantomVault.UI.Tests` | `net9.0` | UI-layer tests (ViewModel coverage) |

### External (shared) dependencies

Phantom Obscura links into the shared `Phantom.Shared` libraries that live alongside it in the workspace:

| Reference | Consumed by | Role |
|---|---|---|
| `Phantom.Shared/Phantom.Sync` | `PhantomVault.Core` | Encrypted cross-app sync of TOTP, session, and trust material |
| `Phantom.Shared/Giblex.AssetShield` | `PhantomVault.UI` (Desktop) | Shared asset / brand-shielding helpers |

The integrated recovery panel (`Views/RecoveryPanelStub.cs` → `RecoveryPanel`, plus `RecoveryWindow.axaml`) is wired and launches the external `PhantomRecovery` process when the recovery vault is detected; the `PhantomRecovery.App` / `PhantomRecovery.Core` references remain commented out in the desktop project file because recovery runs as a separate process for isolation.

---

## Features

### Vault & Encryption
- Argon2id master key derivation with DPAPI-protected pepper and a **mandatory USB keyfile** (the keyfile is required to unlock — passwords are optional)
- AES-GCM authenticated encryption for all vault data
- Custom `PhantomContainerService` container format (v4): static bootstrap header, Argon2id KDF material, encrypted private header with payload hash and HMAC, backwards-compatible with v2/v3
- Zero-knowledge vault service (`ZkVaultService`) — master key verified against a stored HMAC verifier before access is granted; key material zeroed after lock via `CryptographicOperations.ZeroMemory`
- Post-quantum hybrid encryption — BouncyCastle ML-KEM-768 (CRYSTALS-Kyber) encapsulates a 32-byte shared secret that keys AES-256-GCM (`KyberAesHybrid` algorithm in `HybridEncryptionService`)
- Layered and hybrid encryption pipelines

### Authentication
- **Mandatory USB keyfile** + optional password + optional device binding (`DeviceBinding.DeviceSalt()`) — the keyfile is the required unlock factor; a password is an additional optional factor, never required
- Windows Hello (biometric / PIN)
- Passkeys (FIDO2 interface; platform-backed)
- YubiKey hardware token — device enumeration, info, and OATH TOTP credential listing / code generation via Yubico.YubiKey 1.12.0 (desktop only)
- TOTP with QR scanner
- Recovery codes — 10 codes × 128-bit entropy, formatted `XXXX-XXXX-XXXX-XXXX`, Argon2-hashed, single-use, constant-time validation

### Security Defence
- Idle auto-lock with configurable timeout and unlock throttle
- Anti-keylogging, clipboard guard, clipboard history exclusion
- Crash dump suppression and memory protection
- Build integrity verifier (embedded git hash + build timestamp)
- Tamper detection and advanced debugger detection
- Decoy vault / decoy credential generator
- Intrusion defence rule engine with signed policy (`Policies/base_policy.signed.json`)
- Virtual machine detection (desktop only — gated out of the Android `Core` build)
- Window protection service

### Credentials
- Full credential CRUD with categories, tags, and icons
- Duplicate scan and merge, password health checker (HIBP), password generator
- KeePass import (`.kdbx`) via KeePassLib.Standard 2.57.1
- Import / export with history and template support
- Secure deletion, USB artifact protection and binding
- Sharing service

### Autofill
- Native messaging host `com.phantomvault.autofill` — desktop runs as `PhantomVault.UI.exe --native-messaging`
- MV3 extension detects login / registration / TOTP forms and injects an inline fill chip
- Windows and Android autofill service backends

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 9.0.311 (pinned via `global.json`) |
| Windows (desktop) | Windows 10 build 19041+ |
| Android SDK | API 26+ (Android 8.0+) |
| Avalonia | 11.3.9 (restored automatically) |

---

## Build & Run

Per repo policy, the desktop app **must** be launched via the Dev Pass script — it sets `PHANTOM_DEV_BYPASS_POLICY=1` and the MSBuild flags needed for a clean run. Do not add alternate run paths.

```powershell
# Quick-start desktop (Dev Pass — required entry point)
.\run-dev.ps1        # or run-dev.cmd

# Build the production solution (Core, UI.Desktop, Autofill, Platform)
dotnet build PhantomVault.sln

# Build individual projects not in the solution
dotnet build src\Crypto\GiblexVault.Security.ZK.csproj
dotnet build src\UI.Android.Avalonia\PhantomVault.UI.Android.csproj -f net9.0-android
dotnet build src\UI.Android\PhantomVault.Android.csproj -f net9.0-android
dotnet build Tools\Tools.sln

# Tests (run per project — they are not part of PhantomVault.sln)
dotnet test tests\PhantomVault.Core.Tests
dotnet test tests\PhantomVault.UI.Tests
```

`PhantomObscura-Release.apk` (MAUI track) and `PhantomObscura-Avalonia.apk` (Avalonia track) at the repository root are the latest Android release builds.

---

## Browser Extension

### Load as Unpacked (Dev)

See `deployment/install-dev-extension.md` for full instructions.

- **Chrome / Edge** — `chrome://extensions` → "Load unpacked" → select `src/Extension/`
- **Firefox** — `about:debugging` → "Load Temporary Add-on" → select `src/Extension/manifest.json`

### Register Native Messaging Host

Run once after installing the desktop app:

```powershell
.\deployment\register-native-host.ps1   # writes registry key + manifest
.\deployment\unregister-native-host.ps1 # remove
```

Templates `deployment/com.phantomvault.autofill-chromium.json.template` and `com.phantomvault.autofill-firefox.json.template` are expanded by the script with the correct path to `PhantomVault.UI.exe`.

---

## Project Structure

```
Phantom.Obscura/
├── src/
│   ├── Crypto/                   GiblexVault.Security.ZK — Argon2id, AES-GCM, HKDF, key wrap, recovery, ZK vault format
│   ├── Core/                     PhantomVault.Core — all vault services (multi-target net9.0 / net9.0-android)
│   │   └── Services/
│   │       ├── Security/         Defence engine, tamper detection, clipboard guard, decoy vault
│   │       ├── ZeroKnowledge/    ZkVaultService
│   │       └── Mobile/           Android substitutes for desktop-only services
│   ├── Platform/                 Platform abstraction (Windows / mobile)
│   ├── Autofill/                 Native messaging host + OS autofill services
│   ├── UI.Desktop/               Windows app (Avalonia)
│   ├── UI.Android.Avalonia/      Android app (Avalonia shell — shares avares:// with desktop)
│   ├── UI.Android/               Android app (MAUI — full page set, parallel track)
│   └── Extension/                Browser extension (MV3)
│       ├── manifest.json         Firefox ID: phantomvault@giblex.com; min Firefox 128
│       ├── background.js         Service worker / native messaging bridge
│       ├── content.js            Form detection + fill chip injection
│       └── popup.js / popup.html Toolbar popup
├── tests/
│   ├── PhantomVault.Core.Tests/  Encryption, ZK vault, containers, TOTP, recovery, policy…
│   └── PhantomVault.UI.Tests/    ViewModel tests
├── Tools/
│   ├── Tools.sln
│   └── Obscura.Keysmith/         Key/certificate utility + policy signing
├── Policies/                     Signed security policies (base_policy.signed.json), linked into Core
├── deployment/                   Native host registration scripts + manifest templates
├── scripts/                      Diagnostic / validation run logs and helper scripts
├── artifacts/                    Build artifacts
├── PhantomVault.sln              Production solution (Core, UI.Desktop, Autofill, Platform)
├── global.json                   .NET SDK 9.0.311 pin
├── run-dev.ps1 / run-dev.cmd     Dev Pass launcher (sets PHANTOM_DEV_BYPASS_POLICY=1)
├── PhantomObscura-Release.apk    Latest MAUI Android release build
└── PhantomObscura-Avalonia.apk   Latest Avalonia Android release build
```

---

## Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| Avalonia | 11.3.9 | Cross-platform UI (desktop + Android) |
| Isopoh.Cryptography.Argon2 | 2.0.0 | Argon2id master key derivation |
| NSec.Cryptography | 22.4.0 | Modern libsodium-backed crypto primitives |
| BouncyCastle.Cryptography | 2.4.0 | ML-KEM (Kyber) post-quantum KEM (referenced from `Core`) |
| Yubico.YubiKey | 1.12.0 | YubiKey device enumeration and FIDO2 (Windows only) |
| KeePassLib.Standard | 2.57.1 | KeePass `.kdbx` import |
| Serilog | 4.2.0 | Structured logging |
| System.Runtime.WindowsRuntime | 4.7.0 | WinRT async bridging (Windows Hello — Windows only) |
| System.Management | 9.0.0 | WMI for policy / VM detection (Windows only) |
| System.Security.Cryptography.ProtectedData | 9.0.0 | DPAPI pepper protection |

Windows-only packages (`System.Runtime.WindowsRuntime`, `System.Management`, `Yubico.YubiKey`) are conditionally referenced and excluded from the Android `Core` target.

---

## Known Limitations

| Area | Detail |
|---|---|
| YubiKey FIDO2 | OATH TOTP listing and code generation are wired (`YubiKeyTotpService`). Full FIDO2 credential assertion against `Yubico.YubiKey.Fido2` is still pending |
| Windows Hello / Passkeys | Backend has `#warning SECURITY` guards on DPAPI credential storage paths — migration to Credential Manager pending |
| Platform passkeys (non-Windows) | macOS and Linux platform passkeys are surfaced as unsupported in `PasskeySettingsWindow`; only Windows Hello passkeys are wired |
| USB binding / phone | Binding only occurs on the desktop. The mobile heads can read a binding token from an already-bound USB vault but cannot create or rebind on Android |
| Android (Avalonia) | `UI.Android.Avalonia` ships Welcome, Unlock, Dashboard, CredentialList, AddEditCredential, CategoryLanding, SecurityDashboard, ImportExport, IconDownloader, Settings, ThemeSettings, and SmokeTest views; remaining desktop windows are tracked for future ports |
| Android (MAUI) | `UI.Android` has a full 25+ page set as a parallel track; both Android targets share the same application ID (`com.giblex.phantom.obscura`) |
| Multi-session vault access | Concurrent multi-session vault access is intentionally not implemented; the settings toggle is shown for roadmap visibility only |
| Keysmith certs | `Tools/Obscura.Keysmith/certs/` contains development certificate material; should not be committed to production branches |

---

## Policies

The `Policies/` directory holds the runtime security policy consumed by `PolicyEngine` and `PolicyVerifier`. Source files (`ObscuraPolicy.cs`, `PolicyEngine.cs`, `PolicySynchronizer.cs`, `PolicyViolationException.cs`, `UsbKeyFile.cs`) are linked directly into `PhantomVault.Core` from this folder. `PolicyEngine.cs` uses WMI and is included only in the desktop target. The baseline runtime policy ships pre-signed (`base_policy.signed.json`); custom overrides must be re-signed with Keysmith before they are accepted at runtime.

---

## Development Notes

- **Dev Pass only** — runnable apps must be launched via `run-dev.ps1` / `run-dev.cmd`. Do not add alternate run paths or bypass the launcher; it sets `PHANTOM_DEV_BYPASS_POLICY=1` and the MSBuild flags the app expects in dev.
- **Deterministic builds** are enabled on Core and Crypto (`<Deterministic>true</Deterministic>`) for reproducibility.
- **Build metadata** — git commit hash (`SourceRevisionId`) and UTC build timestamp are embedded as assembly attributes and verified at startup by `BuildIntegrityVerifier`.
- The desktop project excludes a `Legacy/` folder from compilation (`<Compile Remove="Legacy\**\*" />`); those files are archived stubs kept for reference. `IntegratedRecoveryService.cs`, `RecoveryDeveloperMode.cs`, and `RecoveryPanel.axaml(.cs)` live there.
- The `UI.Android.Avalonia` project sets `AssemblyName=PhantomVault.UI` intentionally so that `avares://PhantomVault.UI/…` URIs resolve to the same resources as on desktop.
- `PhantomVault.Core` multi-targets `net9.0;net9.0-android`. The Android target removes desktop-only services (`BlackSecureRawVolumeService`, `PasskeyService`, `UsbBindingService`, `YubiKeyService`, `VirtualMachineDetection`, `PolicyService`) and substitutes the `Services/Mobile/*Mobile.cs` stubs in their place.
- Test runner: xUnit. Run `dotnet test` against an individual test project (the test projects are not part of `PhantomVault.sln`).
