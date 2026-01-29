# PhantomVault Autofill System

## Overview

PhantomVault's autofill system provides intelligent form detection, credential suggestions, and automatic password capture for web browsers. The system consists of backend services, UI components, and a native messaging protocol for browser extension communication.

## Architecture

### Components

1. **Backend Services** (`src/Autofill/Autofill/`)
   - `FormFieldDetector.cs` - Pattern-based form field type detection
   - `AutofillSuggestionProvider.cs` - Domain-based credential matching with relevance scoring
   - `PasswordCaptureService.cs` - Password capture and change detection
   - `NativeMessagingHostService.cs` - Browser extension communication protocol

2. **UI Components** (`src/UI.Desktop/`)
   - `AutofillMiniWindow.axaml/.cs` - Compact overlay window for suggestions
   - `AutofillMiniWindowViewModel.cs` - ViewModel with commands and state management
   - `FieldOverlay.cs` - Visual indicator around detected input fields
   - `PasswordCaptureToast.axaml/.cs` - Toast notification for password capture
   - `AutofillCoordinator.cs` - Service coordinating browser, services, and UI

3. **Converters**
   - `MatchTypeColorConverter.cs` - Visual differentiation for match types

## Features

### Form Field Detection

Detects 6 field types across 5 form scenarios:

**Field Types:**

- Email (`email`, `e-mail`, `mail`)
- Username (`username`, `user`, `login`, `account`)
- Password (`password`, `passwd`, `pass`, `pwd`)
- Password Confirmation (`confirm`, `verify`, `repeat`, `retype`)
- Passkey (`passkey`, `webauthn`, `fido`)
- Two-Factor (`2fa`, `mfa`, `otp`, `token`, `code`)

**Form Types:**

- Login (username/email + password)
- Registration (password + confirm password)
- Password Change (password without username)
- Two-Factor Authentication (2FA codes)
- Passkey/WebAuthn (FIDO2)

### Credential Suggestions

**Domain Matching with Relevance Scoring:**

- Exact domain match: **100 points** (e.g., `example.com` = `example.com`)
- Exact without www: **95 points** (e.g., `example.com` = `www.example.com`)
- Subdomain match: **80 points** (e.g., `login.example.com` → `example.com`)
- Base domain match: **60 points** (e.g., `api.example.com` → `example.com`)

**Visual Indicators:**

- 🟢 Green dot: Exact match
- 🔵 Blue dot: Subdomain match
- ⚫ Gray dot: Base domain match

### Password Capture

**Three Capture Scenarios:**

1. **New Login** (CaptureType.NewLogin)
   - User submits login form with credentials not in vault
   - Toast: "Save login for {domain}?"
   - Action: Creates new vault entry with Group="Captured Logins"

2. **Registration** (CaptureType.Registration)
   - Detected when password + confirm password fields match
   - Toast: "Save credentials for {username} at {domain}?"
   - Action: Creates new vault entry

3. **Password Change** (CaptureType.PasswordChange)
   - Detected when existing credential password differs from submitted
   - Toast: "Update password for {username}@{domain}?"
   - Action: Updates existing credential's password + LastUpdatedUtc

## Native Messaging Protocol

Browser extension communicates via Chrome/Firefox native messaging:

### Message Format

**Request:**

```json
{
  "type": "detectForm|submitForm|getCredentials|saveCredential|ping",
  "origin": "chrome-extension://[extension-id]",
  "data": { /* message-specific data */ }
}
```

**Response:**

```json
{
  "success": true|false,
  "data": { /* response data */ },
  "error": "error message if success=false"
}
```

### Message Types

#### 1. `detectForm` - Form Field Detection

**Request:**

```json
{
  "type": "detectForm",
  "origin": "chrome-extension://abc123",
  "data": {
    "url": "https://example.com/login",
    "fields": [
      {
        "id": "username",
        "name": "user",
        "type": "text",
        "placeholder": "Email address",
        "label": "Username",
        "autocomplete": "username",
        "value": "",
        "x": 100,
        "y": 200,
        "width": 300,
        "height": 40
      },
      {
        "id": "password",
        "name": "pass",
        "type": "password",
        "placeholder": "Password",
        "label": "Password",
        "autocomplete": "current-password",
        "value": "",
        "x": 100,
        "y": 260,
        "width": 300,
        "height": 40
      }
    ]
  }
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "detected": true,
    "fieldCount": 2
  }
}
```

**Effect:** Raises `FormDetected` event → `AutofillCoordinator` shows mini-window with suggestions

#### 2. `submitForm` - Form Submission (Password Capture)

**Request:**

```json
{
  "type": "submitForm",
  "origin": "chrome-extension://abc123",
  "data": {
    "url": "https://example.com/login",
    "fields": [
      {
        "id": "username",
        "name": "user",
        "type": "text",
        "value": "john@example.com",
        /* ...field properties... */
      },
      {
        "id": "password",
        "name": "pass",
        "type": "password",
        "value": "SecurePassword123!",
        /* ...field properties... */
      }
    ]
  }
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "submitted": true
  }
}
```

**Effect:** Raises `FormSubmitted` event → `PasswordCaptureService` analyzes → Raises `PasswordCaptured` or `PasswordChanged` event → `AutofillCoordinator` shows toast

#### 3. `getCredentials` - Retrieve Credentials for Domain

**Request:**

```json
{
  "type": "getCredentials",
  "origin": "chrome-extension://abc123",
  "data": {
    "domain": "example.com"
  }
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "credentials": [
      {
        "id": "Example Login",
        "username": "john@example.com",
        "title": "Example Login",
        "domain": "example.com"
      }
    ]
  }
}
```

**Note:** Actual passwords are never sent via native messaging for security reasons.

#### 4. `saveCredential` - Save New Credential

**Request:**

```json
{
  "type": "saveCredential",
  "origin": "chrome-extension://abc123",
  "data": {
    "domain": "example.com",
    "username": "john@example.com",
    "password": "SecurePassword123!",
    "title": "Example Login"
  }
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "saved": true
  }
}
```

#### 5. `ping` - Health Check

**Request:**

```json
{
  "type": "ping",
  "origin": "chrome-extension://abc123",
  "data": {}
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "status": "ok"
  }
}
```

## User Experience Flow

### 1. Page Load with Login Form

```csharp
Browser Extension detects form
    ↓
Sends "detectForm" message
    ↓
NativeMessagingHostService.FormDetected event
    ↓
AutofillCoordinator.HandleFormDetection()
    ↓
FormFieldDetector analyzes fields
    ↓
AutofillMiniWindow appears near input field
    ↓
Shows relevant credentials with match scores
    ↓
User clicks credential
    ↓
AutofillCoordinator.AutofillRequested event
    ↓
Browser extension fills form fields
```

### 2. New Password Registration

```csharp
User fills registration form
    ↓
User clicks "Register" button
    ↓
Browser extension sends "submitForm" message
    ↓
NativeMessagingHostService.FormSubmitted event
    ↓
PasswordCaptureService.DetectPasswordSubmission()
    ↓
Detects: password + confirm match → Registration
    ↓
PasswordCaptureService.PasswordCaptured event
    ↓
PasswordCaptureToast appears
    ↓
User clicks "Save"
    ↓
New Credential created with Group="Captured Logins"
```

### 3. Password Change Detection

```csharp
User updates password on website
    ↓
Browser extension sends "submitForm" message
    ↓
PasswordCaptureService.DetectPasswordSubmission()
    ↓
FindExistingCredential() matches domain + username
    ↓
Password comparison: old ≠ new
    ↓
PasswordCaptureService.PasswordChanged event
    ↓
PasswordCaptureToast appears: "Update password?"
    ↓
User clicks "Update"
    ↓
Credential.Password + LastUpdatedUtc updated
```

## Security Considerations

### Origin Validation

- All messages validate `origin` against allowlist
- Only whitelisted browser extensions can communicate
- Defaults to empty allowlist (fail-closed)

### Vault Lock Enforcement

- `getCredentials` and `saveCredential` require vault unlocked
- Checks `IAutofillVaultContext.IsUnlocked`
- Checks `VaultManifest.AutoFillEnabled` flag

### Credential Security

- Passwords never sent in `getCredentials` response (metadata only)
- Clipboard auto-clears sensitive data after 15 seconds
- Toast notifications auto-hide after 15 seconds

### Input Validation

- Message length limited to 1MB
- JSON structure validated before processing
- Required fields checked for all operations
- Domain, username, password sanitized

## Browser Extension Integration

### Required Capabilities

1. **DOM Inspection**
   - Detect form fields (input elements)
   - Extract attributes: id, name, type, placeholder, autocomplete
   - Get bounding box coordinates for UI positioning
   - Monitor form submission events

2. **Native Messaging**
   - Establish connection to PhantomVault native host
   - Send/receive JSON messages per Chrome/Firefox protocol
   - Handle connection failures gracefully

3. **Form Filling**
   - Set input field values programmatically
   - Trigger input events for frameworks (React, Angular, Vue)
   - Handle single-page application (SPA) routing

### Extension Manifest Example (Chrome)

```json
{
  "name": "PhantomVault Autofill",
  "version": "1.0.0",
  "manifest_version": 3,
  "permissions": [
    "nativeMessaging",
    "activeTab"
  ],
  "host_permissions": [
    "<all_urls>"
  ],
  "content_scripts": [
    {
      "matches": ["<all_urls>"],
      "js": ["content.js"],
      "run_at": "document_end"
    }
  ],
  "background": {
    "service_worker": "background.js"
  }
}
```

### Native Host Manifest (Windows)

```json
{
  "name": "com.phantomvault.autofill",
  "description": "PhantomVault Autofill Native Messaging Host",
  "path": "C:\\Program Files\\PhantomVault\\PhantomVault.Autofill.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://[your-extension-id]/"
  ]
}
```

**Registry Location:**

```csharp
HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\com.phantomvault.autofill
```

## Configuration

### Autofill Settings (VaultManifest)

```csharp
public sealed class VaultManifest
{
    // ... existing properties ...
    
    public bool AutoFillEnabled { get; set; } = true;
    public List<string> AutoFillDomainWhitelist { get; set; } = new();
    public List<string> AutoFillDomainBlacklist { get; set; } = new();
    public int AutoFillSuggestionLimit { get; set; } = 10;
    public bool AutoCaptureNewPasswords { get; set; } = true;
    public bool AutoUpdateChangedPasswords { get; set; } = true;
}
```

### Extension Allowlist

Configure in `appsettings.json`:

```json
{
  "Autofill": {
    "AllowedOrigins": [
      "chrome-extension://abcdefghijklmnopqrstuvwxyz123456",
      "moz-extension://01234567-89ab-cdef-0123-456789abcdef"
    ]
  }
}
```

## Testing

### Unit Tests

1. **FormFieldDetector Tests**
   - Verify pattern matching for all 6 field types
   - Test FormType classification (Login, Registration, etc.)
   - Edge cases: empty labels, conflicting patterns

2. **AutofillSuggestionProvider Tests**
   - Domain matching accuracy (exact, subdomain, base)
   - Relevance scoring correctness
   - Username filtering with partial matches

3. **PasswordCaptureService Tests**
   - Registration detection (password + confirm)
   - Login detection (password change)
   - Password change detection (existing credential)
   - Event raising for all capture types

### Integration Tests

1. **Native Messaging Protocol**
   - Message serialization/deserialization
   - Error handling for malformed messages
   - Origin validation
   - Message length limits

2. **UI Coordination**
   - FormDetected → AutofillMiniWindow shown
   - PasswordCaptured → Toast notification shown
   - Credential selection → AutofillRequested event

### Manual Testing Checklist

- [ ] Form detection on login.example.com
- [ ] Credential suggestions with exact match
- [ ] Credential suggestions with subdomain match
- [ ] Mini-window positioning near input field
- [ ] Keyboard navigation (ESC, Enter, Arrow keys)
- [ ] New password capture on registration
- [ ] Password change detection on existing credential
- [ ] Toast auto-hide after 15 seconds
- [ ] Clipboard auto-clear after 15 seconds
- [ ] Origin validation rejects unauthorized extensions

## Future Enhancements

1. **Passkey/WebAuthn Support**
   - FIDO2 credential storage
   - Biometric authentication integration

2. **OTP/2FA Integration**
   - TOTP generation (RFC 6238)
   - SMS code autofill

3. **Credit Card Autofill**
   - Card number, CVV, expiration detection
   - Secure card storage

4. **Identity Autofill**
   - Name, address, phone number
   - Custom field mapping

5. **Cross-Device Sync**
   - Encrypted credential sync
   - Conflict resolution

6. **Machine Learning Enhancements**
   - Adaptive pattern matching
   - Domain similarity scoring
   - User behavior learning

## License

Part of PhantomVault - Zero-Knowledge Password Manager
