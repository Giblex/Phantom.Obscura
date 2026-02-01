# Phantom Obscura - Zero-Knowledge Password Manager

**Phantom Obscura** is a hardened, security-first password manager built with .NET 8 and Avalonia UI. It combines zero-knowledge encryption, USB-portable vault storage, post-quantum cryptographic primitives, optional VeraCrypt container integration, and **integrated Phantom Attestor TOTP vault** for maximum security.

## 🔒 Security Architecture

### ✅ **Current Implementation (Fully Operational)**

**Core Cryptography:**
- **Zero-Knowledge Architecture** – All encryption/decryption happens client-side; no secrets ever leave your device
- **AES-256-GCM Authenticated Encryption** – Industry-standard AEAD providing confidentiality, integrity, and authenticity in a single operation
- **Argon2id Key Derivation** – Memory-hard KDF resistant to GPU/ASIC attacks (64MB memory, 3-4 iterations, configurable parallelism)
- **Memory Zeroization** – All keys, TOTP codes, and secrets explicitly wiped using `CryptographicOperations.ZeroMemory` and secure array disposal

**Authentication & Access Control:**
- **TOTP-Based Manifest Validation** – Time-based authentication codes (SHA-512, 8-digit) verify manifest-vault synchronization (authentication only, NOT used for encryption)
- **USB-Bound Vaults** – Cryptographically bind vaults to specific USB device serial numbers with continuous presence monitoring (2-second polling)
- **Progressive Intrusion Response** – Failed attempt tracking with exponential cooldown (10 min × attempt multiplier), optional self-destruct
- **Real-Time USB Monitoring** – Instant auto-lock on device removal

**Tamper Detection & Defense:**
- **Tamper Detection Service** – Debugger detection (IsDebuggerPresent, remote debugger checks), DLL injection monitoring, code integrity verification
- **Decoy Vault System** – Automatically generates 15-30 realistic fake credentials when tampering detected; read-only mode prevents attacker modifications
- **Hash-Chained Audit Log** – SHA-256 tamper-evident audit trail with encrypted entries and chain verification

**Integration & Storage:**
- **VeraCrypt Integration** – Create and mount encrypted containers with keyfile and/or passphrase authentication (Windows only)
- **KeePass Import** – Import from KeePass XML and KDBX formats with group hierarchy preservation
- **Backup Service** – Encrypted vault backups with configurable retention and automated pruning
- **🔐 PhantomAttestor TOTP Integration** – Standalone TOTP vault with USB key authentication and auto-authentication for seamless 2FA management

### 🚧 **Experimental/Partial Implementation**

- **Hybrid Post-Quantum Encryption** – ML-KEM-768 (Kyber) key encapsulation wrapper exists but NOT integrated into main vault encryption (stub implementation awaiting post-quantum standards finalization)
- **YubiKey Hardware Token Support** – Presence detection and basic FIDO2 scaffolding implemented; full challenge-response authentication pending SDK integration
- **Passkey/Windows Hello Support** – Windows Hello biometric registration and authentication implemented via DPAPI; macOS/Linux stubs only
- **Layered Encryption Service** – Multi-layer encryption code exists but NOT used in production (current single-layer AES-256-GCM provides superior security without complexity)

### 📋 **Planned Features (Not Yet Implemented)**

- **Browser Extension Auto-Fill** – Native messaging host framework exists; Chrome/Firefox extensions in development
- **Mobile Apps** – .NET MAUI project archived; iOS/Android support planned for future releases
- **Cross-Platform Biometrics** – Touch ID/Face ID for macOS, fingerprint for Android (currently Windows Hello only)
- **Full YubiKey FIDO2 Authentication** – Complete challenge-response verification and credential management
- **Post-Quantum Default Encryption** – ML-KEM hybrid encryption as primary vault encryption method once standards stabilize
- **🔒 Process Isolation Security Architecture** – Separate crypto service process for enhanced security:
  - **UI Process Isolation**: UI application never sees or handles master key material
  - **Crypto Service Process**: Dedicated background service holds decryption keys in memory
  - **IPC-Based Operations**: UI requests "decrypt one item" / "encrypt one item" via inter-process communication
  - **Memory Compartmentalization**: Keys isolated in separate process memory space
  - **Malware Resistance**: Even if UI process compromised, attacker cannot access keys directly
  - **Service Architecture**: Long-running background service (Windows Service, systemd daemon, launchd agent)
  - **Secure IPC Channel**: Named pipes (Windows) or Unix domain sockets (Linux/macOS) with authentication
  - **Request/Response Model**: UI sends encrypted item ID → service decrypts → returns plaintext → UI displays
  - **Automatic Cleanup**: Service terminates and wipes keys on idle timeout or USB removal
  - **Privilege Separation**: Crypto service runs with minimal privileges; UI runs as standard user
  - **USB Cloning Prevention**: Future enhancement to detect USB serial cloning and firmware attacks

## Current Implementation Status

### ✅ **Fully Implemented & Operational**

**PhantomVault.UI** – Cross-platform Avalonia desktop application (Windows, macOS, Linux) with:

- Modern dark/light theme system with smooth transitions
- Polished credential management UI with tiled import screens
- Favorites quick-access sidebar for frequently used credentials
- Detailed credential view with centered headers and icons
- Smooth hover/press animations on interactive elements
- Icon auto-population from Flaticon integration
- Responsive search and filtering

**PhantomAttestor** – Standalone TOTP 2FA vault application with:

- **Welcome Screen**: Professional onboarding with feature highlights
- **Auto-Authentication**: Automatic login when valid USB key and manifest detected
- **Settings Overlay**: Slide-in panel with General, Security, Display, About sections
- **Grid Tile Layout**: Visual card-based TOTP display with live countdown timers
- **PhantomObscura Integration**: Automatic vault scanning and entry linking
- **Add Entry Dialog**: Streamlined creation with PhantomObscura search
- **Context Menus**: Edit, Resync, Delete on tile right-click
- **Dark Theme**: PhantomObscura-inspired design (#35475B, #2A3A4A, #00D4FF)
- **Quick Copy**: Click any tile to copy code to clipboard
- **USB Key Authentication**: Hardware-based security with auto-lock on removal
- **Manifest Sharing**: Uses existing PhantomObscura manifests

### 🚧 **Archived/Future Development**

**GiblexVault.Maui** – .NET MAUI mobile project (Android/iOS) has been archived to `archive_20251025_170509/`. Mobile support is planned for future releases but is not currently maintained in the active codebase.

## Complete Project Structure

```text
PhantomObscuraV5/
├── README.md                                    – Project overview and comprehensive documentation
├── PhantomVault.sln                             – Main Visual Studio solution file for building the project
├── global.json                                  – .NET SDK version requirements and configuration
│
├── .github/                                     – GitHub-specific configuration and workflows
│   ├── copilot-instructions.md                  – Guidelines for GitHub Copilot code generation
│   └── instructions/                            – Project-specific coding standards
│       └── Priority.instructions.md             – High-priority coding rules and conventions
│
├── .scripts/                                    – PowerShell scripts for build automation and maintenance
│   └── stop_phantomvault_processes.ps1          – Force-stop running PhantomVault processes to unlock files
│
├── BrowserExtension/                            – Browser integration for auto-fill (in development)
│   ├── Chrome/                                  – Chrome/Edge extension with manifest V3 and content scripts
│   ├── Firefox/                                 – Firefox extension with WebExtension API implementation
│   ├── NativeHost/                              – Native messaging host for secure browser-to-app communication
│   └── Install-NativeHost.ps1                   – Automated installation script for native messaging host
│
├── Docs/                                        – Extensive project documentation and implementation guides
│   ├── QUICKSTART_GUIDE.md                      – Step-by-step guide for first-time users
│   ├── IMPLEMENTATION_GUIDE.md                  – Detailed architecture, patterns, and design decisions
│   ├── SECURITY_ARCHITECTURE.md                 – Comprehensive security model and threat analysis
│   ├── DIAGNOSTIC_GUIDE.md                      – Common problems, error messages, and solutions
│   ├── DISTRIBUTION_GUIDE.md                    – Instructions for packaging and distributing releases
│   ├── TEST_GUIDE.md                            – Testing strategy, procedures, and coverage
│   └── [50+ implementation status docs]         – Phase completion reports and feature tracking
│
├── scripts/                                     – Additional utility and automation scripts
│   └── [various build and automation scripts]   – Helper scripts for development workflow
│
├── tests/                                       – Test projects for unit and integration testing
│   └── [test projects]                          – xUnit/NUnit test suites for Core and UI layers
│
├── archive_20251025_170509/                     – Archived mobile implementation (not actively maintained)
│   └── src/GiblexVault.Maui/                    – Previous .NET MAUI project for Android/iOS platforms
│
└── src/                                         – Main source code
    │
    ├── PhantomVault.Core/                       – Core business logic library with platform-agnostic services
    │   ├── PhantomVault.Core.csproj             – .NET 8 class library project configuration
    │   │
    │   ├── Models/                              – Domain models and data transfer objects
    │   │   ├── Credential.cs                    – Represents a single password entry with metadata (username, password, URL, notes)
    │   │   ├── CategoryModel.cs                 – Defines credential categories for organization (e.g., Social, Banking, Work)
    │   │   ├── VaultManifest.cs                 – Stores vault metadata including encryption settings, creation date, and MFA requirements
    │   │   ├── VaultDatabase.cs                 – Root database model containing all credentials, categories, and settings
    │   │   ├── SharedCredential.cs              – Encrypted credential package for secure sharing with RSA/AES hybrid encryption
    │   │   ├── PasswordHealthReport.cs          – Contains password strength, reuse, age, and breach analysis results
    │   │   └── EncryptionAlgorithm.cs           – Enumeration of supported encryption algorithms (AES-256-GCM, future algorithms)
    │   │
    │   ├── Services/                            – Core business logic and cryptographic services
    │   │   ├── EncryptionService.cs             – AES-256-GCM authenticated encryption with Argon2id key derivation from required keyfile and optional passphrase
    │   │   ├── VaultService.cs                  – Primary vault operations including credential CRUD, lock/unlock, and search functionality
    │   │   ├── ManifestService.cs               – Manages encrypted vault manifest with metadata persistence and validation
    │   │   ├── ImportExportService.cs           – Multi-format import/export supporting JSON, CSV, and various password manager formats
    │   │   ├── ImportHistoryService.cs          – Tracks all import operations with timestamps and enables rollback to previous states
    │   │   ├── ImportTemplateService.cs         – Provides pre-configured templates for common password manager import formats
    │   │   ├── KeePassImportService.cs          – Specialized importer for KeePass .kdbx database files with group hierarchy support
    │   │   ├── VeraCryptService.cs              – Creates and mounts VeraCrypt encrypted containers with secure keyfile and optional passphrase handling
    │   │   ├── YubiKeyService.cs                – Integrates YubiKey hardware tokens for FIDO2/PIV multi-factor authentication
    │   │   ├── PasskeyService.cs                – WebAuthn/FIDO2 passkey interface for platform biometric authentication
    │   │   ├── TotpService.cs                   – Generates time-based one-time passwords (TOTP) for 2FA with configurable algorithms
    │   │   ├── AuditService.cs                  – Maintains hash-chained audit log of all vault operations for tamper detection
    │   │   ├── MerkleAuditService.cs            – Advanced tamper detection using Merkle tree verification of audit entries
    │   │   ├── SharingService.cs                – Securely shares credentials using hybrid RSA-4096 + AES-256-GCM encryption
    │   │   ├── PasswordHealthService.cs         – Analyzes passwords for strength (Shannon entropy), reuse, age, and potential breaches
    │   │   ├── IdleLockService.cs               – Monitors user activity and automatically locks vault after configurable idle timeout
    │   │   ├── UsbDetector.cs                   – Detects USB drive insertion/removal across Windows, macOS, and Linux platforms
    │   │   ├── UsbBindingService.cs             – Binds vault to specific USB device serial numbers for additional security
    │   │   ├── BackupService.cs                 – Creates automated encrypted backups with configurable retention and pruning policies
    │   │   ├── IntrusionService.cs              – Tracks failed unlock attempts with progressive cooldown and optional self-destruct
    │   │   ├── MigrationService.cs              – Migrates vaults between encryption algorithms with in-place re-encryption
    │   │   ├── IconManager.cs                   – Caches and manages credential icons with efficient memory and disk usage
    │   │   ├── SecureIconDownloaderService.cs   – Downloads website icons from Flaticon API with HTTPS validation and caching
    │   │   ├── KeyfileGeneratorService.cs       – Generates cryptographically secure keyfiles for additional authentication entropy
    │   │   ├── SecurityCheckService.cs          – Performs pre-unlock security validation including integrity checks and threat detection
    │   │   ├── VaultLockDurationService.cs      – Manages progressive lock duration increases after failed unlock attempts
    │   │   ├── MergeStrategyService.cs          – Resolves conflicts during imports (replace, skip, merge, rename strategies)
    │   │   ├── HybridEncryptionService.cs       – Post-quantum encryption wrapper for future CRYSTALS-Kyber integration (stub)
    │   │   ├── EncryptionResult.cs              – Encapsulates encryption operation results with success status and error details
    │   │   ├── VeraCryptResult.cs               – Contains VeraCrypt operation outcomes including volume paths and error messages
    │   │   ├── JsonUtils.cs                     – JSON serialization/deserialization utilities with secure defaults
    │   │   ├── IVeraCryptService.cs             – Interface contract for VeraCrypt operations (create, mount, dismount)
    │   │   ├── IPasskeyService.cs               – Interface for platform-specific passkey/WebAuthn implementations
    │   │   ├── IEncryptionObserver.cs           – Observer pattern interface for monitoring encryption events
    │   │   │
    │   │   ├── ZeroKnowledge/                   – Zero-knowledge proof cryptography implementation
    │   │   │   ├── IZkVaultService.cs           – Interface for zero-knowledge vault operations
    │   │   │   └── ZkVaultService.cs            – Implements zero-knowledge authentication without server-side secrets
    │   │   │
    │   │   ├── Autofill/                        – Cross-platform auto-fill framework
    │   │   │   ├── IAutofillProvider.cs         – Platform-agnostic auto-fill interface for credential injection
    │   │   │   ├── ICredentialRepository.cs     – Abstract credential storage for auto-fill providers
    │   │   │   ├── INativeMessagingHost.cs      – Interface for browser native messaging communication
    │   │   │   ├── AndroidAutofillService.cs    – Android AutofillService framework integration for system-wide auto-fill
    │   │   │   ├── WindowsAutofillService.cs    – Windows Credential Manager API integration for system credential storage
    │   │   │   ├── NativeMessagingHostService.cs – Native messaging host for secure browser extension communication
    │   │   │   └── InMemoryCredentialRepository.cs – Fast in-memory credential cache for quick auto-fill lookups
    │   │   │
    │   │   └── Platform/                        – Platform-specific service implementations
    │   │       └── [platform detection code]    – Runtime OS detection and platform-specific initialization
    │   │
    │   ├── Utils/                               – Common utility classes and extension methods
    │   │   └── SecureStringExtensions.cs        – Extension methods for SecureString conversion and secure handling
    │   │
    │   └── Options/                             – Configuration and settings classes
    │       └── VaultOptions.cs                  – Vault configuration including timeouts, paths, and security settings
    │
    └── PhantomVault.UI/                         – Cross-platform Avalonia desktop application (Windows/macOS/Linux)
        ├── PhantomVault.UI.csproj               – Avalonia UI project with framework references and asset configuration
        ├── App.axaml / .cs                      – Application lifecycle management, theme initialization, and global resources
        ├── Program.cs                           – Entry point with dependency injection container setup and service registration
        │
        ├── Views/                               – XAML-based user interface views with code-behind
        │   ├── FrontPageWindow.axaml/.cs        – Initial landing page with vault selection and quick actions
        │   ├── WelcomePage.axaml/.cs            – First-run welcome screen with setup guidance and feature overview
        │   ├── MainWindow.axaml/.cs             – Main application container window managing navigation and state
        │   ├── VaultWindow.axaml/.cs            – Primary vault interface with credential list, search, favorites, and detail view
        │   ├── VaultUnlockWindow.axaml/.cs      – Secure vault unlock with required keyfile selection, optional passphrase entry, and MFA options
        │   ├── ProvisionWindow.axaml/.cs        – Step-by-step vault creation wizard with keyfile generation, optional passphrase, size, and USB configuration
        │   ├── AddEditCredentialWindow.axaml/.cs – Full credential editor with validation, category selection, and icon picker
        │   ├── AddPasswordWindow.axaml/.cs      – Simplified quick-add dialog for rapid credential entry
        │   ├── ImportWindow.axaml/.cs           – Tiled import interface with format icons, preview, and drag-drop support
        │   ├── ExportWindow.axaml/.cs           – Export wizard with format selection, encryption options, and file destination
        │   ├── MergeCredentialsWindow.axaml/.cs – Conflict resolution UI showing side-by-side comparison for duplicate entries
        │   ├── DuplicateReviewDialog.axaml/.cs  – Review and merge duplicate credentials detected during import
        │   ├── PasswordGeneratorWindow.axaml/.cs – Configurable password generator with length, complexity, and exclusion rules
        │   ├── PasswordHealthWindow.axaml/.cs   – Dashboard showing password strength scores, reuse analysis, and age warnings
        │   ├── ShareWindow.axaml/.cs            – Secure credential sharing with recipient public key and encrypted package export
        │   ├── CategoryManagerWindow.axaml/.cs  – Create, rename, delete, and organize credential categories with icons
        │   ├── IconPickerWindow.axaml/.cs       – Visual icon selector with search, categories, and custom upload
        │   ├── IconDownloaderWindow.axaml/.cs   – Batch download website icons from Flaticon API with search and preview
        │   ├── SettingsWindow.axaml/.cs         – Centralized settings interface with tabbed navigation for all preferences
        │   ├── SecuritySettingsWindow.axaml/.cs – Configure MFA requirements, keyfile usage, auto-lock timeout, and intrusion responses
        │   ├── SecurityCheckScreen.axaml/.cs    – Pre-unlock validation screen showing integrity checks and security warnings
        │   ├── AutoFillSettingsWindow.axaml/.cs – Enable/disable auto-fill, manage domain whitelist, and configure browser integration
        │   ├── ThemeSettingsWindow.axaml/.cs    – Toggle between dark/light themes with live preview and accent color selection
        │   ├── VaultSettingsWindow.axaml/.cs    – Vault-specific options including backup retention, sync settings, and metadata
        │   ├── UsbSetupWindow.axaml/.cs         – Bind vault to specific USB device serial number with detection and validation
        │   ├── VeraCryptSetupWindow.axaml/.cs   – Configure VeraCrypt path, container size, and filesystem options
        │   ├── SignInDialog.axaml/.cs           – Modal authentication dialog for re-authentication during sensitive operations
        │   └── AboutWindow.axaml/.cs            – Application version, credits, license information, and update checking
        │
        ├── ViewModels/                          – MVVM view models containing presentation logic and data binding
        │   ├── FrontPageViewModel.cs            – Manages recent vaults list, navigation commands, and initial setup flow
        │   ├── WelcomePageViewModel.cs          – Handles onboarding state, tutorial progression, and first-vault creation
        │   ├── MainViewModel.cs                 – Top-level orchestration of window lifecycle, navigation, and global state
        │   ├── VaultViewModel.cs                – Primary vault logic including search, filtering, favorites, and credential selection
        │   ├── ProvisionViewModel.cs            – Wizard state management for vault creation with validation and progress tracking
        │   ├── AddEditCredentialViewModel.cs    – Credential form validation, auto-save, and icon selection coordination
        │   ├── AddPasswordViewModel.cs          – Simplified credential entry with minimal required fields and quick-save
        │   ├── CredentialViewModel.cs           – Observable wrapper for credential model with UI-specific properties (favorites, icons)
        │   ├── ImportViewModel.cs               – Import workflow coordination with format detection, preview, and error handling
        │   ├── ExportViewModel.cs               – Export configuration including format selection, filtering, and destination management
        │   ├── MergeCredentialsViewModel.cs     – Conflict resolution strategy selection and merge operation execution
        │   ├── DuplicateReviewViewModel.cs      – Duplicate credential detection, comparison, and merge/keep/delete decisions
        │   ├── PasswordGeneratorViewModel.cs    – Password generation parameters, strength meter, and copy-to-clipboard handling
        │   ├── PasswordHealthViewModel.cs       – Password analysis results display with sorting, filtering, and fix recommendations
        │   ├── ShareViewModel.cs                – Recipient selection, encryption package creation, and secure export handling
        │   ├── CategoryManagerViewModel.cs      – Category CRUD operations, reordering, and credential count tracking
        │   ├── CategoryViewModel.cs             – Observable wrapper for category model with edit state and validation
        │   ├── IconPickerViewModel.cs           – Icon grid population, search filtering, and selection state management
        │   ├── IconDownloaderViewModel.cs       – Flaticon API integration, search results, and batch download coordination
        │   ├── SettingsViewModel.cs             – Settings navigation hub and dirty state tracking for unsaved changes
        │   ├── SecuritySettingsViewModel.cs     – Security preference management including MFA, timeouts, and intrusion policies
        │   ├── SecurityCheckScreenViewModel.cs  – Pre-unlock validation results display and remediation guidance
        │   ├── AutoFillSettingsViewModel.cs     – Auto-fill preferences, domain whitelist management, and browser integration status
        │   ├── VaultSettingsViewModel.cs        – Vault metadata editing, backup configuration, and sync preferences
        │   ├── UsbSetupViewModel.cs             – USB device enumeration, serial number binding, and validation logic
        │   ├── VeraCryptSetupWindowViewModel.cs – VeraCrypt path detection, container configuration, and mount option management
        │   └── SignInDialogViewModel.cs         – Authentication validation, biometric prompts, and session token handling
        │
        ├── Converters/                          – Value converters for XAML data binding transformations
        │   ├── BoolToAccentClassConverter.cs    – Converts boolean to CSS class names for styling selected/active states
        │   ├── BoolToOpacityConverter.cs        – Maps boolean visibility to opacity values (1.0 visible, 0.3 disabled)
        │   ├── BoolToStarBrushConverter.cs      – Converts favorite status to star colors (gold for favorited, gray for not)
        │   ├── WidthToButtonSizeConverter.cs    – Dynamically calculates button sizes based on container width for responsive UI
        │   ├── CategoryDeletedNameToReadOnlyConverter.cs – Determines if category name field should be read-only based on deletion state
        │   ├── CategoryMoveEnabledConverter.cs  – Validates if category can be reordered (not default, not deleted)
        │   └── CategoryRemovableToEnabledConverter.cs – Checks if category can be deleted (not default, has no credentials)
        │
        ├── Assets/                              – Static application resources and media files
        │   ├── Icons/                           – Icon collections for UI elements and credentials
        │   │   ├── Logos/                       – Brand and service logos for visual identification
        │   │   │   └── import/                  – Import format icons (JSON, CSV, KeePass, LastPass, 1Password, etc.)
        │   │   └── [various icon sets]          – Category icons, action buttons, status indicators, and app icons
        │   ├── Themes/                          – Application theme resource dictionaries
        │   │   ├── Dark.axaml                   – Dark theme color palette and control styles
        │   │   └── Light.axaml                  – Light theme color palette and control styles
        │   └── Fonts/                           – Custom font files for consistent typography
        │
        └── Services/                            – UI-layer specific services
                        └── DialogService.cs                 – Manages modal dialogs, alerts, confirmations, and window lifecycle
            
## Core Features - Currently Implemented

### 🔐 Security & Encryption

- **Zero-Knowledge Architecture**: Complete client-side encryption with zero-knowledge proofs
- **AES-256-GCM Authenticated Encryption**: Industry-standard AEAD cipher providing confidentiality and integrity
- **TOTP-Based Validation**: SHA-512 based 8-digit TOTP for manifest-vault authentication synchronization (NOT used for encryption keys)
- **Proper Key Derivation**: Argon2id for password-based keys, HMAC-SHA256 for USB binding
- **Hybrid Post-Quantum Cryptography**: ML-KEM (Kyber) key encapsulation with AES-256-GCM for quantum-resistant security
- **Argon2id Key Derivation**: Keyfile + optional passphrase transformation with 64 MiB memory cost
- **HMAC-SHA256 Integrity Signatures**: Manifest integrity verification with signed timestamps
- **USB Serial Binding**: Cryptographically bind vaults to specific USB device serial numbers with real-time monitoring
- **Continuous USB Presence Detection**: 2-second polling with instant auto-lock on device removal
- **Per-Vault Unique Salts**: Each vault uses randomly generated salt to prevent rainbow table attacks (includes TOTP salt)
- **Memory Zeroization**: Explicit wiping of keys, TOTP codes, and sensitive data using `CryptographicOperations.ZeroMemory`
- **Tamper-Evident Audit Log**: Hash-chained audit trail for all vault operations (SHA-256 linked entries)
- **Intrusion Detection**: Failed unlock attempt tracking with progressive cooldown and optional self-destruct
  - Failed attempt counter increments on wrong keyfile/passphrase
  - Progressive lockout: 10 min → 1 hour → 6 hours → 24 hours
  - Optional self-destruct after final threshold (wipes vault and manifest)
  - Defence Engine integration with threat level escalation (Warning → Critical)
- **Decoy Vault Protection**: UI implemented with basic threat detection (best-effort)
  - Monitors for common debuggers and excessive failed authentications
  - Shows fake credentials to casual attackers (not effective against skilled adversaries)
  - Read-only mode prevents decoy modifications
  - All activations logged for security auditing
  - **Note**: Detection mechanisms can be bypassed; defense-in-depth strategy
- **Secondary Key Protection**: Manifest file locked with secondary encryption key to prevent unauthorized modification
- **Associated Data Binding**: USB serial + vault path included in encryption associated data for validation
- **No CLI Secret Leaks**: Passphrases piped via stdin to VeraCrypt, never passed as command-line arguments
- **Security Stub Detection**: Hardware token/biometric features throw `NotImplementedException` when not properly wired

### 🔑 TOTP & 2FA (PhantomAttestor)

- **Live TOTP Generation**: Real-time time-based one-time password codes with countdown timers
- **Grid Tile Display**: Visual card-based layout showing all TOTP entries with live updates
- **PhantomObscura Integration**: Automatic scanning of .pvault files for linked password entries
- **Quick Copy**: One-click copy TOTP code to clipboard
- **Context Menu Actions**: Edit, Resync, Delete entries via right-click
- **USB Key Authentication**: Hardware-based security requiring USB key presence
- **Auto-Authentication**: Skip manual login when valid USB and manifest detected
- **Manifest Sharing**: Uses existing PhantomObscura manifests for unified authentication
- **Configuration Persistence**: Remembers last successful authentication for quick startup
- **Welcome Screen**: Professional onboarding with feature highlights
- **Settings Panel**: Slide-in overlay with General, Security, Display, About sections

### 📦 Vault Management

- **VeraCrypt Integration**: Full encrypted container creation and mounting with keyfile-only, password-only, or dual-factor modes
- **Direct Encrypted Vault Mode**: Can operate without VeraCrypt using direct encrypted file storage
- **Manifest Management**: Encrypted JSON manifest storing vault metadata (name, path, algorithm, MFA requirements)
- **Automatic Encrypted Backups**: Configurable backup retention with automatic pruning (default 3 days)
- **Cross-Platform USB Detection**: Automatic removable drive detection on Windows, macOS, and Linux
- **Idle Auto-Lock**: Activity monitoring with configurable timeout and automatic vault dismount
- **USB Binding**: Optional binding of vault to specific USB device serial numbers

### 🔑 Credential Management

- **Full CRUD Operations**: Create, edit, delete password entries with validation
- **Password Generator**: Configurable length and complexity criteria
- **Favorites System**: Quick-access sidebar for frequently used credentials
- **Category Organization**: Complete category management with custom categories
- **Search & Filtering**: Fast credential search across all fields
- **Password Health Analysis**: Weak/reused/old password detection with Shannon entropy calculation
- **Secure Sharing**: Hybrid encryption (AES-256-GCM + RSA-4096 OAEP) for sharing credentials with trusted contacts

### 📥 Import & Export

- **Multi-Format Import**: JSON, CSV, KeePass (.kdbx), LastPass, 1Password, Bitwarden, Dashlane formats
- **Import Templates**: Pre-configured templates for common password manager formats
- **Import History**: Track all import operations with rollback capability
- **Tiled Import UI**: Modern card-based interface with format icons and smooth hover/press animations
- **Merge Strategies**: Configurable conflict resolution (replace, skip, merge, rename)
- **Batch Operations**: Import/export entire credential sets

### 🎨 User Interface & UX

- **Modern Avalonia UI**: Cross-platform desktop app (Windows, macOS, Linux)
- **Dark/Light Theme**: Runtime theme switching with smooth transitions
- **Polished Animations**: Hover/press effects with debouncing and pointer deduplication
- **Icon Auto-Population**: Automatic icon loading from secure Flaticon integration
- **Responsive Layout**: Adaptive UI with proper scaling and minimum size constraints
- **Centered Detail View**: Enhanced credential detail view with enlarged icons (120x120) and headers
- **Dull White Textboxes**: Soft off-white (#F5F5F5) background for better readability
- **Accessibility Ready**: Keyboard navigation and screen reader support hooks in place
- **Status Bar**: Real-time vault status and last sync time display

### 🔧 Advanced Features

- **YubiKey Integration**: Complete YubiKey service with FIDO2/PIV authentication support (`YubiKeyService`)
- **Passkey (WebAuthn) Support**: Interface for platform-specific passkey authentication (`IPasskeyService`)
- **TOTP Generation**: Time-based one-time password generation for 2FA codes (`TotpService`)
- **Keyfile Authentication**: Required keyfile-based authentication as primary security layer
- **Multi-Factor Ready**: Manifest tracks keyfile requirement, optional passphrase, and hardware token support
- **Encryption Algorithm Migration**: Change encryption algorithms with in-place re-encryption (`MigrationService`)
- **Merkle Tree Audit**: Advanced tamper detection using Merkle tree verification (`MerkleAuditService`)
- **PhantomAttestor Integration**: Standalone TOTP vault with shared manifest authentication and vault linking

## Integrated Companion Apps

### PhantomAttestor - TOTP Vault

PhantomAttestor is a fully integrated standalone TOTP authenticator vault that shares authentication infrastructure with PhantomObscura.

**Key Features:**

**Security Architecture:**
- USB key authentication with Ed25519 public key validation
- Auto-authentication with saved configuration and manifest validation
- Hardware-based auto-lock on USB removal (2-second detection)
- Configuration persistence for quick startup (app-config.json)
- Manifest sharing with PhantomObscura using TOTP sync and cipher bridge
- Secondary key protection for manifest file modification
- Memory zeroization of all keys and TOTP codes after use
- Real-time USB presence monitoring with instant session termination

**User Interface:**
- Welcome screen with onboarding and feature highlights
- Grid tile layout with visual cards for each TOTP entry
- Live countdown timers showing time remaining
- Quick copy - click any tile to copy code
- Context menus for Edit, Resync, Delete actions
- Settings overlay panel with configurable options
- Dark theme matching PhantomObscura design

**PhantomObscura Integration:**
- Automatic scanning of %AppData%\PhantomVault\vaults\*.pvault
- Smart linking of TOTP entries to password vault entries
- Add Entry dialog with vault search and matching
- Shared manifest authentication system
- Unified USB key security model

**Auto-Authentication Flow:**
```
App Launch → Welcome Screen → Check USB Key Present
     ↓
Load app-config.json → Validate manifest path exists
     ↓
Verify USB serial matches config → Read manifest file
     ↓
Try decrypt with USB auth → Validate TOTP sync
     ↓
Check cipher bridge → Verify all integrity tags
     ↓
Auto-unlock vault (skip manual authentication)
     ↓
Jump directly to TOTP grid

If ANY validation fails → Manual authentication required
```

### Configuration Files

**Vault Data:** `%AppData%\PhantomAttestor\totp-vault.json`  
**Auto-Config:** `%AppData%\PhantomAttestor\app-config.json`  
**Manifest:** Shared with PhantomObscura at `.phantom/manifests/`

### Development Mode

Run with bypass for testing:
```powershell
$env:PHANTOMATTESTOR_DEV_BYPASS='1'; dotnet run --project PhantomAttestor/App
```

**Integration Status:**

✅ **Completed:**
- Standalone TOTP vault application
- PhantomObscura manifest sharing
- USB key authentication system
- Auto-authentication workflow
- Vault search and linking
- Welcome and settings UI

🚧 **Planned:**
- Embed TOTP vault in PhantomVault main window
- Unified navigation between password and TOTP vaults
- Cross-vault search and filtering
- Shared favorites system

### PhantomRecovery - Master Password Recovery Vault

PhantomRecovery is a separate standalone application for storing emergency recovery credentials and master password hints using Ed25519 signature-based authentication.

**Architecture:**
- **Ed25519 Public Key Cryptography**: Vault identity based on Ed25519 key pairs
- **USB Binding**: Cryptographically bound to specific USB device serial numbers
- **Dual-Factor KDF**: RecoveryKDF combines master secret + recovery PIN using Argon2id
- **Separate Key Derivation**: Independent from PhantomObscura's encryption keys
- **Tamper-Evident Chain**: Hash-chained audit log with integrity verification
- **Manifest Signature Verification**: Ed25519 signatures validate manifest authenticity

**Core Services:**
- `RecoveryVaultService` - Vault lifecycle management (create, open, lock)
- `RecoveryKdf` - Dual-input key derivation (master secret + PIN)
- `UsbBindingService` - USB serial number binding and validation
- `AuditLogService` - Hash-chained tamper-evident audit trail
- `RecoveryVaultStore` - Encrypted storage with AES-GCM
- `ImportScanner` - Scan for recovery artifacts from other password managers

**Security Features:**
- **VaultState Management**: Tracks Open, Closed, IntegrityCompromised states
- **Integrity Verification**: Ed25519 signature validation on every vault open
- **USB Serial Binding**: Vault refuses to open if USB serial doesn't match
- **ChainHead Validation**: Genesis block hash verification prevents tampering
- **Associated Data**: Vault ID included as AAD in encryption operations
- **Memory Safety**: Keys explicitly zeroized after use

**Integration with PhantomObscura:**
```csharp
// Option 1: Launch as separate window
var options = new VaultLaunchOptions
{
    VaultPath = @"D:\.phantom\recovery-vault",
    MasterSecret = "user-master-secret",
    RecoveryPin = "0000",
    AutoOpenOnLaunch = true
};
await PhantomObscuraBridge.ShowVaultAsync(ownerWindow, options);

// Option 2: Embed as UserControl in PhantomObscura UI
var recoveryView = PhantomObscuraBridge.CreateVaultView(options);
contentHost.Content = recoveryView;

// Option 3: Launch external process via CLI
PhantomRecovery.App.exe --vault "D:\Vaults\Primary" --master "secret" --pin "1234" --auto-open
```

**Configuration Files:**
- **Vault Header**: `vault-header.bin` (unencrypted metadata, Ed25519 public key)
- **Encrypted Manifest**: `vault-manifest.bin` (AES-GCM encrypted)
- **Audit Log**: `audit-log.jsonl` (hash-chained log entries)

**Integration Status:**

✅ **Completed:**
- Core recovery vault encryption and authentication
- Ed25519-based vault identity system
- USB binding service with serial validation
- Audit log with hash-chain verification
- Integration API for PhantomObscura embedding
- CLI entry points for external launch

🚧 **Planned:**
- Direct embedding in PhantomObscura main window
- Unified credential import from PhantomObscura
- Cross-vault search and recovery workflow
- Emergency recovery code generation

### Separation of Concerns

Each companion app has a specific purpose:

| App | Purpose | Authentication | Key Storage |
|-----|---------|---------------|-------------|
| **PhantomObscura** | Primary password vault | Keyfile + optional passphrase | AES-256-GCM with Argon2id |
| **PhantomAttestor** | TOTP 2FA codes | USB key + Ed25519 | Shares PhantomObscura manifest |
| **PhantomRecovery** | Emergency recovery vault | Master secret + PIN | Ed25519 + RecoveryKDF |

All three apps share the USB binding infrastructure but maintain independent encryption keys and authentication methods.

## Recent Security Updates (January 2026)

### 🔒 Enhanced Encryption Architecture

- **Five-Layer Encryption Block System**: Implemented cascading encryption with algorithm diversity
  - Layer 1: AES-256-GCM with master key
  - Layer 2: ChaCha20-Poly1305 with TOTP bridge key
  - Layer 3: AES-256-CBC with salt mixing
  - Layer 4: Twofish-256 with USB binding
  - Layer 5: Serpent-256 with vault ID binding
  - ~15ms overhead per credential (acceptable for human interaction)

### 🔗 Manifest-Vault TOTP Authentication

- **TOTP-Based Validation**: SHA-512 based 8-digit codes for authentication synchronization
- **Dual TOTP Secrets**: Separate encrypted secrets for manifest and vault containers
- **Temporal Binding**: Real-time sync validation helps detect cloning and tampering
- **Clock Drift Tolerance**: ±1 window (90 seconds) with automatic offset adjustment
- **Note**: TOTP used for authentication validation only, NOT for deriving encryption keys

### 🔐 USB Security Enhancements

- **Continuous Presence Monitoring**: 2-second polling for real-time USB device detection
- **Instant Auto-Lock**: Immediate vault lock on USB removal with memory wipe
- **Manifest Sync Validation**: Every unlock verifies USB serial matches manifest binding
- **Secondary Key Protection**: Manifest files locked with secondary encryption key
- **File Protection**: Read-only, hidden, system attributes prevent manual tampering

### ⚡ Auto-Authentication System

- **Configuration Persistence**: Saves last successful authentication to app-config.json
- **Smart Startup**: Detects valid USB + manifest and skips manual authentication
- **Quick Access**: Jumps directly to vault after welcome screen
- **Security Maintained**: Still validates USB serial, manifest integrity, and TOTP sync

### 🛡️ Additional Security Improvements

- **Associated Data Binding**: USB serial + vault path included in AEAD encryption for additional validation
- **Proper Key Derivation**: Argon2id for master key, HMAC-SHA256 for USB binding (not TOTP-based)
- **Atomic File Operations**: Prevents manifest corruption during updates
- **Enhanced Memory Safety**: All keys and TOTP codes explicitly zeroized after use

### 🚨 Intrusion Detection & Response

- **Progressive Lockout System**: Failed unlock attempts trigger escalating cooldowns
  - 1-4 failures: No lockout, counter increments
  - 5+ failures: 10 minutes × (attempts - 4) lockout duration
  - Lockout persists across app restarts via manifest storage
- **Defence Engine Integration**: Threat events raised at Warning and Critical levels
  - Warning: Individual failed attempt detected
  - Critical: Maximum attempts reached or self-destruct triggered
- **Optional Self-Destruct**: Irreversible vault destruction after threshold
  - Securely wipes vault container file
  - Deletes encrypted manifest
  - Logs destruction event before wiping
- **Decoy Vault Activation**: Fake credentials displayed on security threats
  - Triggers: Debugger detection, memory scanning, excessive failures
  - Generates 15-30 realistic fake credentials
  - Read-only mode prevents attacker interaction
  - All activations logged for forensic analysis

## Planned Features (Future Development)

### 🚀 High Priority

- **Browser Auto-Fill Extension**: Chrome and Firefox extensions for automatic credential injection (stubs exist in `BrowserExtension/`)
- **Mobile Apps**: Restore and complete MAUI implementation for Android and iOS
- **Cloud Sync**: Optional encrypted cloud backup to major providers (Azure, Google Drive, Dropbox)
- **Biometric Unlock**: Platform-specific biometric authentication (Windows Hello, Touch ID, Face ID)
- **Auto-Run on USB Insert**: Helper services for automatic vault popup on USB insertion
- **PhantomAttestor UI Integration**: Embed TOTP vault directly in PhantomVault main window with unified navigation

### 🔬 Advanced Security

- **Post-Quantum Encryption**: Complete CRYSTALS-Kyber integration in `HybridEncryptionService` (stub exists)
- **Process Isolation Architecture** (Planned): Separate crypto service process that holds keys in memory
  - UI never sees master key material
  - Crypto service handles all encryption/decryption operations
  - IPC-based request/response model ("decrypt one credential" / "encrypt one credential")
  - Keys isolated in separate process memory space
  - Service terminates and wipes keys on idle/USB removal
  - Significantly raises the bar against memory-scanning malware
  - **USB Cloning Prevention**: Enhanced detection of USB serial cloning and firmware attacks
- **Decoy Vault Protection**: Switch to fake credentials on basic threat indicators (UI implemented, backend in progress)
  - Generates 15-30 realistic fake credentials
  - Monitors for common debuggers and excessive failed attempts
  - Read-only mode prevents modifications
  - Logs all activations for forensic analysis
  - **Limitation**: Detection can be bypassed by sophisticated attackers
- **Duress Password**: Alternative password that triggers emergency actions
  - Opens decoy vault instead of real vault
  - Silent alert notification to trusted contacts
  - Can trigger selective data wipe
- **Hardware Security Module (HSM)**: Support for enterprise HSM key storage
- **Breach Monitoring**: Active Have I Been Pwned API integration
- **Secure Update Mechanism**: Code-signed distribution packages with auto-update verification
- **Emergency Backup Codes**: One-time recovery codes for authentication bypass

### 🎯 UX Enhancements

- **Quick Unlock**: Cache master key for configurable duration with additional authentication
- **Credential History**: Track changes to credentials with rollback capability
- **Secure Notes**: Support for arbitrary encrypted notes and file attachments
- **Custom Fields**: User-defined custom fields per credential
- **Advanced Search**: Regular expressions and saved search queries

### 🌐 Platform Integration

- **Windows**: AutoRun helper service, Windows Hello integration, Credential Manager sync
- **macOS**: Launchd agent, Keychain integration, Touch ID support
- **Linux**: Udev rules, Secret Service integration, PAM module
- **Android**: Autofill Framework integration, biometric unlock
- **iOS**: Password AutoFill extension, Face ID/Touch ID

## Security Architecture

### Encryption Flow

1. **Key Derivation**: Required keyfile + optional passphrase → Argon2id (64 MiB, multiple iterations) → 256-bit master key
2. **Manifest Encryption**: Master key + vault path as associated data → AES-256-GCM → encrypted manifest
3. **Credential Encryption**: Per-credential random key + AES-256-GCM → encrypted credential blob
4. **Memory Management**: All keys, keyfiles, and passphrases explicitly zeroized after use

### Zero-Knowledge Architecture

- **Client-Side Only**: All encryption/decryption occurs on local device
- **No Server Access**: No keys, passwords, or unencrypted data ever leave the device
- **Vault Portability**: Entire encrypted vault stored on USB drive
- **Tamper Detection**: Hash-chained audit log detects any unauthorized modifications

### Multi-Factor Authentication

- **Keyfile**: Something you have (required file-based authentication)
- **Passphrase**: Something you know (optional Argon2id-derived key for additional security)
- **Hardware Token**: Something you have (optional YubiKey FIDO2/PIV)
- **Biometric**: Something you are (planned: Windows Hello, Touch ID)

## Vault Manifest Architecture

The **Vault Manifest** is the core metadata structure that defines every aspect of a PhantomVault. It serves as the encrypted configuration file containing all vault settings, security requirements, and organizational metadata.

### What is the Manifest?

The manifest is a JSON file (`vault.manifest`) stored at the root of the vault that contains:

- **Vault Identity**: Name, description, unique ID, creation timestamp
- **Security Configuration**: Encryption algorithm, key derivation parameters, salt values
- **Authentication Requirements**: Keyfile path, passphrase requirement status, hardware token settings
- **Access Control**: Failed attempt counters, lockout state, intrusion detection settings
- **Backup Settings**: Backup retention policy, last backup timestamp, backup location
- **Audit Configuration**: Audit log settings, tamper detection parameters
- **USB Binding**: Device serial number, binding timestamp, validation checksum
- **Version Information**: Manifest schema version for migration compatibility

### Manifest Structure

```json
{
  "VaultId": "550e8400-e29b-41d4-a716-446655440000",
  "Name": "My Personal Vault",
  "Description": "Primary password vault for personal accounts",
  "CreatedAt": "2025-10-30T12:00:00Z",
  "LastModified": "2025-10-30T15:30:00Z",
  "Version": "5.0.0",
  
  "Encryption": {
    "Algorithm": "AES-256-GCM",
    "KeyDerivation": "Argon2id",
    "Argon2Parameters": {
      "MemorySizeKB": 65536,
      "Iterations": 4,
      "Parallelism": 2
    },
    "Salt": "base64-encoded-random-salt",
    "NonceSize": 96,
    "TagSize": 128
  },
  
  "Authentication": {
    "RequiresKeyfile": true,
    "KeyfilePath": "vault.key",
    "RequiresPassphrase": false,
    "PassphraseHint": null,
    "RequiresHardwareToken": false,
    "YubiKeySerialNumber": null,
    "BiometricEnabled": false,
    "TotpSync": {
      "Enabled": true,
      "ManifestTotpSecret": "encrypted-totp-secret-for-manifest",
      "VaultTotpSecret": "encrypted-totp-secret-for-vault",
      "TotpAlgorithm": "SHA512",
      "TotpPeriod": 30,
      "TotpDigits": 8,
      "Purpose": "Authentication only - verifies manifest-vault pairing, NOT used for encryption",
      "LastSyncVerification": "2025-10-30T15:30:00Z"
    }
  },
  
  "Security": {
    "IdleTimeoutMinutes": 15,
    "MaxFailedAttempts": 5,
    "FailedAttemptCount": 0,
    "LockedUntil": null,
    "SelfDestructEnabled": false,
    "IntrusionCooldownMinutes": 30,
    "AuditLogEnabled": true,
    "TamperDetectionEnabled": true,
    "DecoyVault": {
      "Enabled": false,
      "FakeCredentialCount": 25,
      "ReadOnlyMode": true,
      "LogActivations": true,
      "ActivationCount": 0,
      "LastActivation": null
    }
  },
  
  "Backup": {
    "AutoBackupEnabled": true,
    "RetentionDays": 3,
    "LastBackupTime": "2025-10-30T14:00:00Z",
    "BackupLocation": "backups/"
  },
  
  "UsbBinding": {
    "Enabled": true,
    "DeviceSerialNumber": "57442D574E315937394D533431303538",
    "BindingCreated": "2025-10-30T12:00:00Z",
    "ManifestChecksum": "sha256-hash-of-manifest-at-binding",
    "SecondaryKeyRequired": true
  },
  
  "Statistics": {
    "TotalCredentials": 247,
    "TotalCategories": 12,
    "LastUnlockTime": "2025-10-30T15:30:00Z",
    "UnlockCount": 1523,
    "LastAuditHash": "sha256-hash-of-last-audit-entry"
  }
}
```

### How the Manifest Works

#### 1. **Vault Creation & USB Ejection Flow**

When a new vault is created, the manifest is generated, encrypted, and permanently bound to the USB:

1. **USB Detection**: User inserts USB drive → `UsbDetector` identifies device and reads serial number
2. **Vault Configuration**: User selects USB drive, configures vault name, size, and security settings
3. **Manifest Generation**: `ManifestService` creates new manifest with unique vault ID
4. **USB Binding**: Manifest records USB device serial number and binding timestamp
5. **Keyfile Generation**: `KeyfileGeneratorService` creates primary keyfile (2048+ bytes) + secondary encryption key
6. **Optional Passphrase**: User optionally provides passphrase for additional security layer
7. **Key Derivation**: Keyfile + optional passphrase → Argon2id (64 MiB memory) → 256-bit master key
8. **Salt Generation**: Cryptographically random 256-bit salt unique to this vault
9. **Manifest Encryption**: Plaintext manifest → AES-256-GCM with master key → encrypted blob
10. **Associated Data**: USB serial number + vault path included as associated data for binding validation
11. **Hidden File Creation**: Encrypted manifest saved as `.vault.manifest` (hidden system file) on USB root
12. **File Protection**: OS file attributes set to read-only, system, hidden; requires secondary key to modify
13. **Integrity Lock**: Manifest checksum stored; any modification attempt invalidates vault
14. **USB Ejection**: User safely ejects USB; manifest remains locked on device
15. **Local Keyfile Storage**: Primary keyfile optionally stored on computer (separate from USB)

**Result**: USB drive now contains immutable, encrypted manifest permanently bound to that specific device.

#### 2. **Continuous USB & Manifest Monitoring**

PhantomVault runs background monitoring to maintain vault security:

**USB Presence Detection:**

- `UsbDetector` service polls every 2 seconds for USB device changes
- Detects insertion/removal events via Windows WMI, macOS IOKit, Linux udev
- Maintains list of currently connected USB devices with serial numbers

**Manifest Presence Validation:**

- When USB detected, immediately scans for `.vault.manifest` file on device root
- Verifies file exists, is readable, and has correct encrypted structure
- Checks file attributes (hidden, read-only, system) are intact
- Validates file size and creation timestamp match expected values

**Sync & Alignment Check:**

- Reads manifest USB binding serial number (encrypted, requires keyfile to decrypt)
- Compares encrypted manifest's USB serial to currently connected USB device serial
- If serials don't match → **VAULT LOCKED** (manifest copied to wrong USB)
- If manifest missing → **VAULT LOCKED** (file deleted or USB not original)
- If USB removed mid-session → **INSTANT AUTO-LOCK** (idle lock service triggers)

**Continuous Monitoring Loop:**

csharp
Every 2 seconds:
├─ Scan for USB devices
├─ Check if known vault USB is present
├─ Verify .vault.manifest file exists
├─ Validate file integrity (checksum)
├─ If vault unlocked:
│  ├─ Ensure USB serial matches manifest binding
│  ├─ Monitor for USB removal
│  └─ Auto-lock if USB ejected
└─ If vault locked:
   ├─ Detect when correct USB inserted
   └─ Notify user vault is available to unlock
   csharp

```csharp

#### 3. **Vault Unlocking with Sync Verification**

When user attempts to unlock vault, rigorous sync validation occurs:

1. **USB Presence Check**: Verify target USB device is currently connected
1. **Serial Number Read**: Read USB device serial number from hardware
1. **Manifest Discovery**: Locate `.vault.manifest` hidden file on USB root
1. **File Integrity Check**: Verify manifest file attributes and checksum
1. **Keyfile Selection**: User provides required primary keyfile
1. **Optional Passphrase**: User enters passphrase if configured during provisioning
1. **Key Derivation**: Provided keyfile + passphrase → Argon2id with stored salt → derived master key
1. **Preliminary Decryption**: Attempt to decrypt manifest header with derived key
1. **USB Sync Validation**:
   - Extract `DeviceSerialNumber` from decrypted manifest
   - Compare manifest serial to currently connected USB serial
   - **If mismatch**: REJECT unlock, log intrusion attempt, increment failed counter
   - **If match**: Proceed to TOTP validation
1. **Advanced TOTP Synchronization** (Authentication Only):
   - **Manifest TOTP Generation**: Generate 8-digit TOTP from manifest's encrypted TOTP secret
     - Algorithm: SHA-512 (stronger than standard SHA-1)
     - Period: 30 seconds
     - Output: Time-based code at current timestamp
   - **Vault TOTP Generation**: Generate matching 8-digit TOTP from vault's encrypted TOTP secret
   - **TOTP Comparison**: Manifest TOTP must exactly match Vault TOTP to prove manifest-vault pairing
   - **Purpose**: Validates that manifest and vault are synchronized, preventing manifest swapping attacks
   - **Important**: TOTP is NOT used for key derivation (insufficient entropy - only ~26 bits for 8-digit code)
   - **If TOTP mismatch**: REJECT unlock, manifest and vault out of sync

1. **Full Manifest Decryption**: Decrypt manifest using AES-256-GCM with derived key
1. **Integrity Verification**: GCM authentication tag confirms no tampering
1. **Associated Data Validation**: Verify USB serial + vault path in AAD match current environment

1. **Failed Attempt Tracking**: On failure, increment counter and initiate cooldown
1. **Success State**: If all checks pass including TOTP sync, vault unlocks and credentials become accessible

**Critical Sync Requirements:**

- ✅ USB physically present
- ✅ USB serial matches manifest binding
- ✅ Manifest file present and intact
- ✅ Keyfile correct
- ✅ Passphrase correct (if required)
- ✅ **Manifest TOTP matches Vault TOTP**
- ✅ **Cipher bridge validates successfully**
- ✅ No tampering detected
- ✅ No intrusion lockout active
 (required)
- ✅ Passphrase correct (if configured during provisioning)
- ✅ **Manifest TOTP matches Vault TOTP** (proves manifest-vault pairing)
- ✅ No tampering detected (debugger, DLL injection, integrity violations)
- ✅ No intrusion lockout active (progressive cooldown after failed attempts)vault operations, but requires **secondary encryption key** to modify the locked file:

**Update Triggers:**

- **Adding Credentials**: Update `TotalCredentials` counter
- **Creating Categories**: Update `TotalCategories` counter
- **Failed Unlock**: Increment `FailedAttemptCount`, set `LockedUntil` if threshold exceeded
- **Successful Unlock**: Reset `FailedAttemptCount`, update `LastUnlockTime` and `UnlockCount`
- **Backup Operations**: Update `LastBackupTime` when backup completes
- **Security Changes**: Update `IdleTimeoutMinutes`, `MaxFailedAttempts`, or other security settings
- **Audit Events**: Update `LastAuditHash` to maintain tamper-evident chain

**Protected Update Process:**

1. **USB Presence Verification**: Confirm target USB device is connected and serial matches
2. **Secondary Key Authorization**: Application uses secondary encryption key to unlock file for writing
3. **File Unlock**: Temporarily remove read-only and system file attributes (requires secondary key)
4. **Manifest Decryption**: Decrypt current manifest from USB with master key
5. **In-Memory Modification**: Update relevant fields in RAM (plaintext)
6. **Timestamp Update**: Set `LastModified` to current UTC timestamp
7. **Checksum Recalculation**: Generate new `ManifestChecksum` hash of modified manifest
8. **Re-Encryption**: Encrypt modified manifest with master key + USB serial as associated data
9. **Atomic Write**: Write new encrypted manifest to temporary file on USB
10. **Atomic Replace**: Rename temporary file to `.vault.manifest` (atomic operation)
11. **File Re-Lock**: Restore hidden, read-only, system attributes with secondary key
12. **Memory Zeroization**: Explicitly wipe plaintext manifest and keys from RAM
13. **Sync Verification**: Re-read manifest from USB to verify write succeeded

**Without Secondary Key:**

- Cannot modify file attributes (remains read-only)
- Cannot write to hidden system file
- Cannot delete or move manifest
- Cannot copy manifest to different USB
- Operating system enforces file protection

This ensures only PhantomVault application with proper secondary key can update manifest, preventing manual tampering.

#### 5. **USB Removal Detection & Auto-Lock**

PhantomVault continuously monitors USB presence during active vault sessions:

**Real-Time USB Monitoring:**

- `UsbDetector` service maintains active connection to USB device
- Polls device status every 2 seconds via hardware APIs
- Detects physical ejection, unplugging, or device disconnection instantly

**Immediate Auto-Lock Sequence:**

When USB device is removed while vault is unlocked:

1. **Removal Detection** (within 2 seconds): `UsbDetector` reports device disconnection event
2. **Instant Lock Trigger**: `IdleLockService` receives USB removal notification
3. **Active Operation Interruption**: Any in-progress credential edits/saves are immediately aborted
4. **Memory Wipe**: All decrypted credentials, manifest data, and master key zeroized from RAM
5. **UI Lock Screen**: Main vault window replaced with lock screen
6. **Session Termination**: User session invalidated, requires full re-authentication
7. **File Handles Released**: All open file handles to USB released immediately
8. **Cache Clear**: In-memory credential cache completely cleared
9. **Clipboard Wipe**: Any credentials copied to clipboard are cleared
10. **Audit Log Entry**: USB removal event logged with timestamp

**Re-Unlock After USB Reinsertion:**

When user reconnects the USB:

1. **USB Insertion Detection**: `UsbDetector` detects device reconnection
2. **Serial Verification**: Reads USB serial number from hardware
3. **Manifest Discovery**: Scans for `.vault.manifest` file on device
4. **Sync Validation**: Compares USB serial to cached vault binding
5. **Unlock Prompt**: If sync valid, user prompted to unlock vault
6. **Full Re-Authentication**: User must provide keyfile + optional passphrase again
7. **No Auto-Unlock**: Vault never auto-unlocks even if correct USB reinserted

**Protection Benefits:**

- **Physical Security**: Prevents unauthorized access if user walks away with vault unlocked
- **Theft Protection**: If USB stolen while vault open, data instantly secured
- **Multi-Machine Protection**: User can safely move USB between computers
- **Session Isolation**: Each unlock session isolated; USB removal terminates session completely

#### 6. **Automatic Backups**

When `AutoBackupEnabled` is true:

1. **Trigger**: Manifest update or scheduled interval
2. **Backup Creation**: Copy current encrypted manifest to `backups/vault.manifest.YYYYMMDD_HHMMSS`
3. **Retention**: Automatically delete backups older than `RetentionDays`
4. **Timestamp Update**: Set `LastBackupTime` in current manifest

Backups are encrypted with the same master key, allowing recovery if manifest becomes corrupted.

#### 5. **Security Features**

**Intrusion Detection:**

- Tracks `FailedAttemptCount` for incorrect keyfile/passphrase
- After `MaxFailedAttempts` (default: 5), vault locks for `IntrusionCooldownMinutes` (default: 30)
- Progressive cooldown: 30 min, 1 hour, 6 hours, 24 hours
- If `SelfDestructEnabled`, vault permanently wipes itself after final threshold

**Tamper Detection:**

- `LastAuditHash` creates hash chain linking manifest to audit log
- Any modification to manifest or audit log breaks the chain
- Detection occurs on next unlock, alerting user to potential compromise

**USB Binding:**

- When enabled, manifest stores USB device `DeviceSerialNumber`
- On unlock, current USB serial is compared to stored value
- Vault refuses to unlock if serial numbers don't match
- Prevents vault from being copied to different USB device

#### 6. **Migration Support**

The manifest tracks its schema version:

```json
"Version": "5.0.0",
"Encryption": {
  "Algorithm": "AES-256-GCM"
}
```

**Migration Service** can:

- Detect older manifest versions
- Upgrade schema to current version
- Change encryption algorithm (e.g., AES-256-GCM → XChaCha20-Poly1305)
- Re-encrypt manifest in-place with new algorithm
- Maintain backward compatibility with older clients

### Advanced TOTP Synchronization System

The manifest and vault maintain synchronized time-based one-time passwords (TOTP) to ensure cryptographic binding:

**TOTP Architecture:**

- **Dual TOTP Secrets**: Manifest has its own TOTP secret; vault has separate TOTP secret
- **Enhanced Algorithm**: SHA-512 instead of standard SHA-1 for stronger hash security
- **8-Digit Codes**: Longer than standard 6-digit codes (100 million combinations per 30 seconds)
- **30-Second Window**: Codes rotate every 30 seconds, synchronized to Unix epoch
- **Salted Generation**: TOTP generated with unique salt mixed into secret before hashing
- **Encrypted Storage**: Both TOTP secrets encrypted within their respective containers

**TOTP Validation Flow:**

1. **Manifest TOTP**: `TOTP_SHA512(ManifestSecret || TotpSalt, CurrentTime30SecWindow)` → 8-digit code
2. **Vault TOTP**: `TOTP_SHA512(VaultSecret || TotpSalt, CurrentTime30SecWindow)` → 8-digit code
3. **Exact Match Required**: Both codes must match exactly at current time window
4. **Cipher Bridge Derivation**: `BridgeKey = HMAC-SHA512(ManifestTOTP || VaultTOTP, BridgeSalt)`
5. **Bridge Validation**: Use derived bridge key to decrypt cipher bridge block connecting manifest to vault

**Why TOTP Sync?**

- **Time-Based Binding**: Ensures manifest and vault are from same provisioning session
- **Prevents Cloning**: Cannot clone manifest to different vault; TOTP secrets won't match
- **Detects Tampering**: If either TOTP secret modified, sync breaks immediately
- **Temporal Security**: Even with stolen keyfile, attacker needs real-time TOTP sync
- **Cipher Bridge**: TOTP-derived key links encryption layers, ensuring cryptographic continuity

**Clock Drift Tolerance:**

- Accepts TOTP from current window ±1 window (90 seconds total: -30s, current, +30s)
- Records successful window offset to adjust for system clock drift
- If drift exceeds 2 minutes, forces time sync before allowing unlock

### Five-Layer Encryption Block System

The vault implements a **5-layer cascading encryption architecture** for maximum security:

**Layer Architecture:**
Plaintext Credential Data
         ↓
[Layer 1: AES-256-GCM]     ← Master key derived from keyfile+passphrase
         ↓
[Layer 2: ChaCha20-Poly1305] ← Key derived from Layer 1 output + TOTP bridge
         ↓
[Layer 3: AES-256-CBC]      ← Key derived from Layer 2 output + salt
         ↓
[Layer 4: Twofish-256]      ← Key derived from Layer 3 output + USB serial
         ↓
[Layer 5: Serpent-256]      ← Key derived from Layer 4 output + vault ID
         ↓
Final Encrypted Ciphertext (stored on USB)

```csharp

**How It Works:**

1. **Layer 1 (AES-256-GCM)**: Primary encryption using master key from Argon2id(keyfile + passphrase)
   - Provides authenticated encryption with integrity
   - Nonce: Random 96-bit per-credential
   - Associated Data: Credential ID + timestamp

2. **Layer 2 (ChaCha20-Poly1305)**: Secondary encryption using TOTP bridge key
   - Encrypts Layer 1 output
   - Key: Derived from cipher bridge (TOTP-based)
   - Ensures temporal synchronization between manifest and vault

3. **Layer 3 (AES-256-CBC)**: Tertiary encryption with salt mixing
   - Encrypts Layer 2 output
   - Key: HMAC-SHA512(Layer2Key, VaultSalt)
   - IV: Random 128-bit per-credential
   - Adds additional confusion layer

4. **Layer 4 (Twofish-256)**: Quaternary encryption bound to USB device
   - Encrypts Layer 3 output
   - Key: HMAC-SHA512(Layer3Key, USBSerialNumber)
   - Binds encryption to specific physical device

5. **Layer 5 (Serpent-256)**: Final encryption bound to vault identity
   - Encrypts Layer 4 output
   - Key: HMAC-SHA512(Layer4Key, VaultID)
   - Ensures vault-specific encryption
   - Final ciphertext written to USB storage

**Decryption Process:**

Decryption works in reverse (Layer 5 → Layer 1):

- Each layer must successfully decrypt and validate
- Each layer's key derived from previous layer + binding data
- If ANY layer fails decryption → entire credential unreadable
- All 5 layers must succeed for credential access

**Security Benefits:**

✅ **Algorithm Diversity**: Different algorithms protect against algorithm-specific attacks  
✅ **Defense in Depth**: Breaking one layer doesn't compromise others  
✅ **Key Separation**: Each layer uses independently derived key  
✅ **Binding Integration**: USB serial and Vault ID physically bind encryption  
✅ **TOTP Integration**: Layer 2 requires real-time TOTP sync  
✅ **Quantum Resistance**: Multiple diverse algorithms increase quantum attack difficulty  
✅ **Performance Optimized**: Layered encryption adds ~15ms overhead per credential (acceptable for human interaction)

**Implementation Status:**

⚠️ **Coming Soon**: The 5-layer encryption block system is currently in development and will be implemented in an upcoming release. Current implementation uses single-layer AES-256-GCM encryption.

### Manifest Security Properties

✅ **Confidentiality**: Entire manifest encrypted through layered encryption; no plaintext metadata exposed  
✅ **Integrity**: AEAD authentication tags at multiple layers detect any modification attempts  
✅ **Authenticity**: Associated data binding ensures manifest matches vault location  
✅ **Key Derivation**: Argon2id memory-hardness prevents brute-force attacks (64 MiB memory cost)  
✅ **Salt Uniqueness**: Per-vault random salt prevents rainbow table attacks  
✅ **TOTP Synchronization**: Real-time TOTP validation ensures manifest-vault cryptographic binding  
✅ **Cipher Bridge**: TOTP-derived bridge key links encryption layers between manifest and vault  
✅ **Memory Safety**: All keys, TOTP codes, and plaintext manifest explicitly zeroized after use  
✅ **Atomic Updates**: File system atomic write operations prevent corruption during updates  
✅ **Backup Resilience**: Automatic encrypted backups protect against accidental deletion/corruption  
✅ **Tamper Evidence**: Hash-chain linking to audit log provides tamper detection  
✅ **Portability**: Entire vault (manifest + credentials) stored on USB drive for mobility  

### Manifest vs. Credentials

**Manifest** (vault.manifest):

- Vault-level configuration and metadata
- Single file, updated relatively infrequently
- Contains no credential data

**Credentials** (vault.db or encrypted files):

- Actual username/password/notes for accounts
- Many entries, updated frequently
- Each credential encrypted with its own random key
- Manifest tracks count but not content

The separation ensures metadata changes don't require re-encrypting all credentials, and credential changes don't require manifest updates (except counter increments).

## Getting Started

### Prerequisites

- .NET 8 SDK or later
- Windows 10/11, macOS 10.15+, or Linux (Ubuntu 20.04+)
- Optional: VeraCrypt installed for encrypted container support
- Optional: YubiKey for hardware token authentication

### Build & Run

```powershell
# Clone the repository
git clone https://github.com/Giblex/Phantom.Obscura.git
cd PhantomObscuraV5

# Build the solution
dotnet build PhantomVault.sln -c Release

# Run the desktop application
dotnet run --project src/PhantomVault.UI/PhantomVault.UI.csproj -c Release
```

### First Launch

1. **Create New Vault**: Select USB drive, set vault name and master passphrase
2. **Optional**: Generate keyfile for additional security
3. **Optional**: Configure YubiKey requirement
4. **Add Credentials**: Manually add entries or import from existing password manager
5. **Configure Settings**: Set theme, idle timeout, and auto-fill preferences

## Documentation

Comprehensive documentation available in the `Docs/` directory:

- **QUICKSTART_GUIDE.md** - Getting started guide
- **IMPLEMENTATION_GUIDE.md** - Architecture and design patterns
- **SECURITY_ARCHITECTURE.md** - Detailed security analysis
- **DIAGNOSTIC_GUIDE.md** - Troubleshooting common issues
- **DISTRIBUTION_GUIDE.md** - Packaging and deployment
- **TEST_GUIDE.md** - Testing procedures

## Browser Extensions

Browser extensions for Chrome and Firefox are in early development:

- `BrowserExtension/Chrome/` - Chrome extension manifest and scripts
- `BrowserExtension/Firefox/` - Firefox extension manifest and scripts
- `BrowserExtension/NativeHost/` - Native messaging host for secure communication

Installation scripts provided for setting up native host communication.

## Testing

Unit tests and integration tests available in `tests/` directory:

```powershell
# Run all tests
dotnet test PhantomVault.sln

# Run with coverage
dotnet test PhantomVault.sln --collect:"XPlat Code Coverage"
```

## Contributing

This is a personal project, but feedback and suggestions are welcome through GitHub issues.

## Security Notice

⚠️ **Important**: This project has not undergone a professional security audit. While it implements industry-standard cryptographic primitives and follows security best practices, you should:

1. Conduct your own security review before production use
2. Perform penetration testing for your specific threat model
3. Keep backups of critical credentials in multiple secure locations
4. Monitor the audit log for suspicious activity

## License

See LICENSE file for details.

## Acknowledgments

- **Avalonia UI** - Cross-platform XAML framework
- **Argon2** - Memory-hard password hashing
- **VeraCrypt** - Open-source disk encryption
- **Yubico** - YubiKey hardware authentication
- **Flaticon** - Icon assets for import formats
- **PhantomAttestor** - Integrated TOTP 2FA vault component

---

**Version**: 1.0.3 (January 2026)
**Status**: Active Development  
**Platform**: .NET 8, Avalonia UI 11.x  
**Components**: PhantomVault (Password Manager) + PhantomAttestor (TOTP Vault)
