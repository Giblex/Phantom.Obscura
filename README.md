# Phantom Obscura (PhantomVault)

A local-first, privacy-preserving password vault and security suite. Phantom Obscura stores credentials encrypted on-device using Argon2id-derived keys and AES-GCM authenticated encryption — no cloud sync required. It ships a Windows desktop app, an Android app (Avalonia), an Apple iOS/iPadOS app (Avalonia), and a browser extension that communicates with the local vault over native messaging.

---

## Projects

`PhantomVault.sln` contains the four projects that ship in the production desktop build (`Core`, `UI.Desktop`, `Autofill`, `Platform`). The Crypto library, Android head, dev tools, and tests live in the same repo and build via direct `ProjectReference`s, `Tools/Tools.sln`, or per-project `dotnet build`.

| Project | Path | Target | Role |
|---|---|---|---|
| `GiblexVault.Security.ZK` | `src/Crypto` | `net10.0` | Cryptographic primitives: Argon2id, AES-GCM, HKDF, key wrapping, ZK vault format, recovery material |
| `PhantomVault.Core` | `src/Core` | `net10.0;net10.0-android` | Vault services: encryption, ZK vault, containers, passkeys, TOTP, import/export, USB binding, policy, security defence, update verification. Android builds substitute `Services/Mobile/*Mobile.cs` for desktop-only services. |
| `PhantomVault.Platform` | `src/Platform` | `net10.0` | Platform-specific services behind interfaces |
| `PhantomVault.Autofill` | `src/Autofill` | `net10.0` | Autofill backend: native messaging host, Windows + Android autofill, form field detection |
| `PhantomVault.UI` (Desktop) | `src/UI.Desktop` | `net10.0-windows10.0.19041.0` | Windows desktop app (Avalonia 11) |
| `PhantomVault.UI` (Android/Avalonia) | `src/UI.Android.Avalonia` | `net10.0-android` | Android app (Avalonia) — shares assembly name with desktop to reuse `avares://` resources |
| `PhantomVault.UI` (iOS/Avalonia) | `src/UI.iOS.Avalonia` | `net10.0-ios` | Apple iOS/iPadOS app (Avalonia) — links the Android head's shared mobile views/view-models and reuses the desktop `avares://` resources |
| Browser Extension | `src/Extension` | — | MV3 extension (Chrome/Edge/Firefox) — relays autofill via native messaging |
| `Obscura.Keysmith` | `Tools/Obscura.Keysmith` | `net10.0` | Dev tool for generating and inspecting vault keys and signing policies (`Tools/Tools.sln`) |
| `PhantomVault.Core.Tests` | `tests/PhantomVault.Core.Tests` | `net10.0` | Unit and integration tests for Core and Crypto |
| `PhantomVault.UI.Tests` | `tests/PhantomVault.UI.Tests` | `net10.0` | UI-layer tests (ViewModel coverage) |

### External (shared) dependencies

Phantom Obscura links into the shared libraries that live alongside it in the workspace:

| Reference | Consumed by | Role |
|---|---|---|
| `Phantom.Shared/Giblex.AssetShield` | `PhantomVault.UI` (Desktop) | Shared asset / brand-shielding helpers; the AssetShield tool also encrypts all published files except the main exe at publish time |

The integrated recovery panel (`Views/RecoveryPanelStub.cs` → `RecoveryPanel`, plus `RecoveryWindow.axaml`) is wired and launches the external `PhantomRecovery` process when the recovery vault is detected; the `PhantomRecovery.App` / `PhantomRecovery.Core` references remain commented out in the desktop project file because recovery runs as a separate process for isolation.

---

## Features

### Vault & Encryption
- Argon2id master key derivation with DPAPI-protected pepper and a **mandatory USB keyfile** (enforced by `KeyfileGuard.Require` at every create/unlock/mount entry point — passwords are an optional additional factor)
- AES-GCM authenticated encryption for all vault data
- Custom `PhantomContainerService` container format (v4): static bootstrap header, Argon2id KDF material, encrypted private header with payload hash and HMAC, backwards-compatible with v2/v3
- Zero-knowledge vault service (`ZkVaultService`) — master key verified against a stored HMAC verifier before access is granted; key material zeroed after lock via `CryptographicOperations.ZeroMemory`
- Post-quantum hybrid encryption — BouncyCastle ML-KEM-768 (CRYSTALS-Kyber) encapsulates a 32-byte shared secret that keys AES-256-GCM (`KyberAesHybrid` algorithm in `HybridEncryptionService`)
- Layered and hybrid encryption pipelines

### Authentication
- **Mandatory USB keyfile** + optional password + optional device binding (`DeviceBinding.DeviceSalt()`)
- Windows Hello (biometric / PIN)
- Passkeys (FIDO2 interface; platform-backed)
- YubiKey hardware token — device enumeration, info, and OATH TOTP credential listing / code generation via Yubico.YubiKey 1.12.0 (desktop only)
- TOTP with QR scanner
- PIN lock (PBKDF2-150k, stored in the vault manifest with a settings fallback) with unlock throttling
- Recovery codes — 10 codes × 128-bit entropy, formatted `XXXX-XXXX-XXXX-XXXX`, Argon2-hashed, single-use, constant-time validation; exportable as a printable PDF recovery kit (rendered locally via SkiaSharp, no extra dependency)

### Security Defence
- Idle auto-lock with configurable timeout and unlock throttle; auto-lock on minimize / screen lock
- Anti-keylogging heuristics, clipboard guard with auto-clear, clipboard history exclusion
- Crash dump suppression and memory protection
- Build integrity verifier (embedded git hash + build timestamp)
- Tamper detection and advanced debugger detection
- Decoy vault / decoy credential generator
- Intrusion defence rule engine with signed policy (`Policies/base_policy.signed.json`)
- Virtual machine detection (desktop only — gated out of the Android `Core` build)
- Window protection and screenshot protection services
- Verified update pipeline — Ed25519 signature over the update manifest, SHA-256 over the downloaded asset, then Authenticode check on Windows (`UpdateVerifier`); auto-check is **off by default**

### Privacy
- Master "Go Offline" switch (`PrivacyShield` / `IInternetGateway`) gates every outbound HTTP client (HIBP, icon downloads, updates) and revokes active consent grants
- HIBP breach checking uses k-anonymity — only the first 5 characters of a password hash ever leave the device
- No telemetry or analytics SDKs

### Credentials
- Full credential CRUD with categories, tags, and icons; nine entry types (passwords, API keys, bank accounts, cards, contacts, identities, PINs, Wi-Fi, notes)
- Duplicate scan and merge, password health checker (HIBP), password generator
- KeePass import (`.kdbx`) via KeePassLib.Standard 2.57.1
- Import / export with history and template support
- Secure deletion with a retention-based Secure Rubbish Bin, USB artifact protection and binding
- Sharing service (RSA public-key encrypted share payloads)

### Autofill
- Native messaging host `com.phantomvault.autofill` — desktop runs as `PhantomVault.UI.exe --native-messaging`
- Local IPC pipe restricted with `PipeOptions.CurrentUserOnly`
- MV3 extension detects login / registration / TOTP forms and injects an inline fill chip
- Windows and Android autofill service backends

### Desktop Experience
- **Start with Windows** — optional per-user launch registration (`Software\Microsoft\Windows\CurrentVersion\Run`); off by default and toggled from Settings
- **Global hotkey** — optional system-wide chord (default `Ctrl+Alt+P`) that brings the vault to the foreground, served by a dedicated message-only window so it never subclasses the Avalonia window proc; off by default
- **Recent Issues panel** — a bounded, in-memory log (last 50 entries) of warnings and errors surfaced to the user, reviewable from Settings after a toast fades; error/warning toasts feed it automatically and security-relevant silent failures (settings persistence, PIN setup, auto-lock-on-threat, auto-fill init, TOTP sync) are recorded so nothing fails invisibly

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0.x (no pin; uses latest installed) |
| Windows (desktop) | Windows 10 build 19041+ |
| Android SDK | API 26+ (Android 8.0+) |
| Avalonia | 11.3.11 (restored automatically) |

---

## Build & Run

Use the standard launcher for a normal run with full policy enforcement. The Dev Pass script (`run-dev.ps1` / `run-dev.cmd`) sets `PHANTOM_DEV_BYPASS_POLICY=1` for development only — the bypass is honoured in Debug builds exclusively, and Release builds refuse to start if the variable is set.

```powershell
# Standard launch (full policy enforcement)
.\run.ps1            # or run.cmd

# Development launch (policy bypass, Debug builds only)
.\run-dev.ps1        # or run-dev.cmd

# Build the production solution (Core, UI.Desktop, Autofill, Platform)
dotnet build PhantomVault.sln

# Build individual projects not in the solution
dotnet build src\Crypto\GiblexVault.Security.ZK.csproj
dotnet build src\UI.Android.Avalonia\PhantomVault.UI.Android.csproj -f net10.0-android
dotnet build Tools\Tools.sln

# Build the Apple iOS head (requires macOS + Xcode; pass a RID).
# Unsigned simulator build (no provisioning profile needed):
dotnet build src/UI.iOS.Avalonia/PhantomVault.UI.iOS.csproj -f net10.0-ios -r iossimulator-arm64
# Signed device build (needs an Apple signing identity + provisioning profile):
dotnet build src/UI.iOS.Avalonia/PhantomVault.UI.iOS.csproj -f net10.0-ios -r ios-arm64

# Tests (run per project — they are not part of PhantomVault.sln)
dotnet test tests\PhantomVault.Core.Tests
dotnet test tests\PhantomVault.UI.Tests
```

The Android APK is produced by the `build-apk.yml` GitHub Actions workflow, and the iOS app is built (unsigned, for the simulator) by the `build-ios.yml` workflow on a macOS runner.

---

## Browser Extension

### Load as Unpacked (Dev)

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
│   ├── Core/                     PhantomVault.Core — all vault services (multi-target net10.0 / net10.0-android)
│   │   └── Services/
│   │       ├── Security/         Defence engine, tamper detection, clipboard guard, decoy vault
│   │       ├── Update/           Signed update manifest + UpdateVerifier (Ed25519 / SHA-256 / Authenticode)
│   │       ├── ZeroKnowledge/    ZkVaultService
│   │       └── Mobile/           Android substitutes for desktop-only services
│   ├── Platform/                 Platform abstraction (Windows / mobile)
│   ├── Autofill/                 Native messaging host + OS autofill services
│   ├── UI.Desktop/               Windows app (Avalonia)
│   ├── UI.Android.Avalonia/      Android app (Avalonia shell — shares avares:// with desktop; canonical home of the shared mobile views/view-models)
│   ├── UI.iOS.Avalonia/          Apple iOS/iPadOS app (Avalonia shell — links the Android head's shared mobile UI; iOS-only glue in Platforms/iOS)
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
├── scripts/                      Helper scripts
├── .github/workflows/            CI: desktop build, Android APK, CodeQL, security hard rules
├── PhantomVault.sln              Production solution (Core, UI.Desktop, Autofill, Platform)
├── global.json                   .NET SDK (no pin)
├── run.ps1 / run.cmd             Standard launcher (full policy enforcement)
├── run-dev.ps1 / run-dev.cmd     Dev launcher (sets PHANTOM_DEV_BYPASS_POLICY=1; Debug builds only)
├── SECURITY.md                   Vulnerability disclosure policy
└── THREAT_MODEL.md               Threat model (per-surface)
```

---

## Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| Avalonia | 11.3.11 | Cross-platform UI (desktop + Android) |
| Isopoh.Cryptography.Argon2 | 2.0.0 | Argon2id master key derivation |
| NSec.Cryptography | 22.4.0 | Modern libsodium-backed crypto primitives |
| BouncyCastle.Cryptography | 2.4.0 | ML-KEM (Kyber) post-quantum KEM (referenced from `Core`) |
| Yubico.YubiKey | 1.12.0 | YubiKey device enumeration and FIDO2 (Windows only) |
| KeePassLib.Standard | 2.57.1 | KeePass `.kdbx` import |
| Serilog | 4.2.0 | Structured logging |
| System.Runtime.WindowsRuntime | 4.7.0 | WinRT async bridging (Windows Hello — Windows only) |
| System.Management | 10.0.0 | WMI for policy / VM detection (Windows only) |
| System.Security.Cryptography.ProtectedData | 10.0.0 | DPAPI pepper protection |

Windows-only packages (`System.Runtime.WindowsRuntime`, `System.Management`, `Yubico.YubiKey`) are conditionally referenced and excluded from the Android `Core` target.

---

## Known Limitations

| Area | Detail |
|---|---|
| Platform passkeys (non-Windows) | macOS and Linux platform passkeys are surfaced as unsupported in `PasskeySettingsWindow`; only Windows Hello passkeys are wired |
| USB binding / phone | Binding only occurs on the desktop. The mobile head can read a binding token from an already-bound USB vault but cannot create or rebind on Android |
| Android (Avalonia) | `UI.Android.Avalonia` is the single Android head (application ID `com.giblex.phantom.obscura`). It ships Welcome, Unlock, Dashboard, CredentialList, AddEditCredential, CategoryLanding, SecurityDashboard, ImportExport, IconDownloader, Settings, ThemeSettings, and SmokeTest views; remaining desktop windows are tracked for future ports |
| iOS (Avalonia) | `UI.iOS.Avalonia` is the single Apple head (bundle ID `com.giblex.phantom.obscura`). It links and ships the same shared mobile views as the Android head. USB-keyfile binding has no iOS equivalent yet (iOS exposes no removable USB volume to apps), so `UsbDriveMonitor` reports no drives; biometric unlock surfaces via Face ID/Touch ID is not yet wired. Device builds require an Apple signing identity + provisioning profile |
| Multi-session vault access | Concurrent multi-session vault access is intentionally not implemented; the settings toggle is shown for roadmap visibility only |
| Keysmith certs | `Tools/Obscura.Keysmith/certs/` contains development certificate material; should not be committed to production branches |
| Settings storage | `%APPDATA%\PhantomVault\settings.json` is plaintext JSON and includes PIN verification material (salt/hash); the vault manifest copy is authoritative |

---

## Policies

The `Policies/` directory holds the runtime security policy consumed by `PolicyEngine` and `PolicyVerifier`. Source files (`ObscuraPolicy.cs`, `PolicyEngine.cs`, `PolicySynchronizer.cs`, `PolicyViolationException.cs`, `UsbKeyFile.cs`) are linked directly into `PhantomVault.Core` from this folder. `PolicyEngine.cs` uses WMI and is included only in the desktop target. The baseline runtime policy ships pre-signed (`base_policy.signed.json`); custom overrides must be re-signed with Keysmith before they are accepted at runtime.

---

## Development Notes

- **Launchers** — `run.ps1` / `run.cmd` start the app with full policy enforcement. `run-dev.ps1` / `run-dev.cmd` set `PHANTOM_DEV_BYPASS_POLICY=1` for development; the bypass is compiled in for Debug builds only (`#if DEBUG`), and Release builds refuse to start when it is set.
- **Deterministic builds** are enabled on Core and Crypto (`<Deterministic>true</Deterministic>`) for reproducibility.
- **Build metadata** — git commit hash (`SourceRevisionId`) and UTC build timestamp are embedded as assembly attributes and verified at startup by `BuildIntegrityVerifier`.
- A root `Legacy/` folder holds archived snapshots (e.g. `Legacy/2026-06-04/`) kept for reference; nothing in it is compiled.
- The `UI.Android.Avalonia` project sets `AssemblyName=PhantomVault.UI` intentionally so that `avares://PhantomVault.UI/…` URIs resolve to the same resources as on desktop.
- `PhantomVault.Core` multi-targets `net10.0;net10.0-android;net10.0-ios`. Both mobile targets (gated by the `IsMobileBuild` MSBuild flag) remove desktop-only services (`BlackSecureRawVolumeService`, `PasskeyService`, `UsbBindingService`, `YubiKeyService`, `VirtualMachineDetection`, `PolicyService`) and the Windows-only native packages, substituting the `Services/Mobile/*Mobile.cs` stubs in their place.
- The iOS head (`UI.iOS.Avalonia`) links the Android head's `ViewModels/`, `Views/`, and the platform-neutral `Services/UsbDriveMonitor.cs` rather than copying them, so the two mobile surfaces stay in lockstep; only the platform entry points (`Platforms/iOS/AppDelegate.cs`, `Main.cs`) and `Info.plist` are iOS-specific. Like the Android head it sets `AssemblyName=PhantomVault.UI` so `avares://PhantomVault.UI/…` URIs resolve to the shared desktop resources.
- Test runner: xUnit. Run `dotnet test` against an individual test project (the test projects are not part of `PhantomVault.sln`).
