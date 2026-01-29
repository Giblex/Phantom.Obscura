# PhantomVault Development Roadmap - Phase 1

**Timeline**: 3-6 months
**Focus**: Market Competitiveness - Browser Integration & Testing
**Status**: Planning
**Last Updated**: December 27, 2025

---

## PRIORITY 1: Browser Extension Completion ⭐⭐⭐⭐⭐

**Impact**: CRITICAL - Major competitive feature
**Effort**: HIGH (3-4 months)
**Current State**: Partially implemented in `BrowserExtension/` directory
**Target**: Chrome Web Store + Firefox Add-ons publication

### Architecture Overview

```
Browser Extension (Content Script)
  ↓ Detects login forms
  ↓ Sends message via chrome.runtime.sendNativeMessage()
Native Messaging Host (PhantomVault.Autofill)
  ↓ Receives JSON message
  ↓ Queries vault for credentials
  ↓ Returns encrypted response
Browser Extension (Content Script)
  ↓ Receives credentials
  ↓ Displays autofill overlay
  ↓ Fills form on user selection
```

### Implementation Tasks

#### 1. Native Messaging Host Enhancement (2 weeks)

**File**: `src/Autofill/Autofill/NativeMessagingHostService.cs`

**Current State**: Basic message handling exists
**Required Enhancements**:

```csharp
// Add message types
public enum MessageType
{
    Ping,                    // Health check
    GetCredentials,          // Fetch credentials for domain
    SaveCredential,          // Save new credential
    UpdateCredential,        // Update existing credential
    GeneratePassword,        // Generate strong password
    GetVaultStatus,          // Check if vault is unlocked
    UnlockVault,             // Trigger vault unlock
    LockVault                // Lock vault
}

// Enhanced message handler
public async Task<NativeMessage> HandleMessageAsync(NativeMessage request)
{
    try
    {
        return request.Type switch
        {
            MessageType.Ping => HandlePing(),
            MessageType.GetCredentials => await HandleGetCredentials(request),
            MessageType.SaveCredential => await HandleSaveCredential(request),
            MessageType.UpdateCredential => await HandleUpdateCredential(request),
            MessageType.GeneratePassword => HandleGeneratePassword(request),
            MessageType.GetVaultStatus => HandleGetVaultStatus(),
            MessageType.UnlockVault => await HandleUnlockVault(request),
            MessageType.LockVault => HandleLockVault(),
            _ => new NativeMessage { Status = "error", Message = "Unknown message type" }
        };
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Native message handling failed");
        return new NativeMessage { Status = "error", Message = ex.Message };
    }
}
```

**Security Enhancements**:
- Origin validation (only accept messages from registered extension IDs)
- Rate limiting (max 100 messages/minute per extension)
- Credential encryption in transit (ephemeral session keys)
- Audit logging for all credential access

#### 2. Chrome Extension Implementation (4 weeks)

**Directory**: `BrowserExtension/Chrome/`

**Manifest V3** (`manifest.json`):
```json
{
  "manifest_version": 3,
  "name": "PhantomVault",
  "version": "1.0.0",
  "description": "Secure password manager with zero-knowledge encryption",
  "permissions": [
    "nativeMessaging",
    "storage",
    "activeTab",
    "tabs"
  ],
  "host_permissions": [
    "http://*/*",
    "https://*/*"
  ],
  "background": {
    "service_worker": "background.js",
    "type": "module"
  },
  "content_scripts": [
    {
      "matches": ["http://*/*", "https://*/*"],
      "js": ["content.js"],
      "css": ["autofill.css"],
      "run_at": "document_idle"
    }
  ],
  "action": {
    "default_popup": "popup.html",
    "default_icon": {
      "16": "icons/icon16.png",
      "48": "icons/icon48.png",
      "128": "icons/icon128.png"
    }
  },
  "icons": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  }
}
```

**Content Script** (`content.js`):
```javascript
// Form detection and field identification
class FormDetector {
  constructor() {
    this.forms = [];
    this.passwordFields = [];
    this.usernameFields = [];
  }

  detectForms() {
    // Detect password fields
    this.passwordFields = Array.from(
      document.querySelectorAll('input[type="password"]')
    );

    // Detect username fields (email, text before password)
    this.usernameFields = this.passwordFields.map(pwField => {
      const form = pwField.closest('form');
      if (!form) return null;

      // Look for email input
      let emailField = form.querySelector('input[type="email"]');
      if (emailField) return emailField;

      // Look for text input before password
      const inputs = Array.from(form.querySelectorAll('input[type="text"]'));
      const pwIndex = Array.from(form.querySelectorAll('input')).indexOf(pwField);
      return inputs.find(input => {
        const inputIndex = Array.from(form.querySelectorAll('input')).indexOf(input);
        return inputIndex < pwIndex;
      });
    }).filter(Boolean);

    return this.passwordFields.length > 0;
  }

  attachAutofillListeners() {
    this.passwordFields.forEach((field, index) => {
      field.addEventListener('focus', () => this.showAutofillOverlay(field, index));
    });

    this.usernameFields.forEach((field, index) => {
      field.addEventListener('focus', () => this.showAutofillOverlay(field, index));
    });
  }

  async showAutofillOverlay(field, index) {
    const domain = window.location.hostname;

    // Request credentials from native host
    const response = await chrome.runtime.sendMessage({
      type: 'getCredentials',
      domain: domain
    });

    if (response.status === 'success' && response.credentials.length > 0) {
      this.displayOverlay(field, response.credentials);
    }
  }

  displayOverlay(field, credentials) {
    // Create autofill overlay
    const overlay = document.createElement('div');
    overlay.className = 'phantomvault-overlay';
    overlay.style.cssText = `
      position: absolute;
      background: white;
      border: 1px solid #47E0B8;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      z-index: 999999;
      max-height: 300px;
      overflow-y: auto;
    `;

    // Position overlay below field
    const rect = field.getBoundingClientRect();
    overlay.style.top = `${rect.bottom + window.scrollY + 4}px`;
    overlay.style.left = `${rect.left + window.scrollX}px`;
    overlay.style.minWidth = `${rect.width}px`;

    // Add credential items
    credentials.forEach(cred => {
      const item = document.createElement('div');
      item.className = 'phantomvault-item';
      item.style.cssText = `
        padding: 12px 16px;
        cursor: pointer;
        border-bottom: 1px solid #eee;
      `;
      item.innerHTML = `
        <div style="font-weight: 600; color: #333;">${cred.title}</div>
        <div style="font-size: 12px; color: #666;">${cred.username}</div>
      `;

      item.addEventListener('click', () => {
        this.fillCredential(cred);
        overlay.remove();
      });

      item.addEventListener('mouseenter', () => {
        item.style.background = '#f5f5f5';
      });

      item.addEventListener('mouseleave', () => {
        item.style.background = 'white';
      });

      overlay.appendChild(item);
    });

    document.body.appendChild(overlay);

    // Remove overlay when clicking outside
    const removeOverlay = (e) => {
      if (!overlay.contains(e.target) && e.target !== field) {
        overlay.remove();
        document.removeEventListener('click', removeOverlay);
      }
    };
    setTimeout(() => document.addEventListener('click', removeOverlay), 100);
  }

  fillCredential(credential) {
    // Fill username field
    const usernameField = this.usernameFields[0];
    if (usernameField) {
      usernameField.value = credential.username;
      usernameField.dispatchEvent(new Event('input', { bubbles: true }));
      usernameField.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // Fill password field
    const passwordField = this.passwordFields[0];
    if (passwordField) {
      passwordField.value = credential.password;
      passwordField.dispatchEvent(new Event('input', { bubbles: true }));
      passwordField.dispatchEvent(new Event('change', { bubbles: true }));
    }
  }
}

// Initialize form detector
const detector = new FormDetector();
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    if (detector.detectForms()) {
      detector.attachAutofillListeners();
    }
  });
} else {
  if (detector.detectForms()) {
    detector.attachAutofillListeners();
  }
}

// Watch for dynamically added forms (SPAs)
const observer = new MutationObserver(() => {
  if (detector.detectForms()) {
    detector.attachAutofillListeners();
  }
});
observer.observe(document.body, { childList: true, subtree: true });
```

**Background Service Worker** (`background.js`):
```javascript
// Native messaging port
let nativePort = null;
let pendingRequests = new Map();
let requestId = 0;

// Connect to native host
function connectNativeHost() {
  if (nativePort) return;

  try {
    nativePort = chrome.runtime.connectNative('com.phantomvault.autofill');

    nativePort.onMessage.addListener((message) => {
      console.log('Received from native host:', message);

      // Resolve pending request
      const requestCallback = pendingRequests.get(message.requestId);
      if (requestCallback) {
        requestCallback(message);
        pendingRequests.delete(message.requestId);
      }
    });

    nativePort.onDisconnect.addListener(() => {
      console.log('Native host disconnected:', chrome.runtime.lastError);
      nativePort = null;

      // Reject all pending requests
      pendingRequests.forEach((callback) => {
        callback({ status: 'error', message: 'Native host disconnected' });
      });
      pendingRequests.clear();
    });

    console.log('Connected to native host');
  } catch (error) {
    console.error('Failed to connect to native host:', error);
  }
}

// Send message to native host
function sendNativeMessage(message) {
  return new Promise((resolve) => {
    if (!nativePort) {
      connectNativeHost();
    }

    if (!nativePort) {
      resolve({ status: 'error', message: 'Native host not available' });
      return;
    }

    const id = ++requestId;
    message.requestId = id;

    pendingRequests.set(id, resolve);
    nativePort.postMessage(message);

    // Timeout after 10 seconds
    setTimeout(() => {
      if (pendingRequests.has(id)) {
        pendingRequests.delete(id);
        resolve({ status: 'error', message: 'Request timeout' });
      }
    }, 10000);
  });
}

// Handle messages from content script
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.type === 'getCredentials') {
    sendNativeMessage({
      type: 'GetCredentials',
      data: { domain: request.domain }
    }).then(sendResponse);
    return true; // Async response
  }

  if (request.type === 'saveCredential') {
    sendNativeMessage({
      type: 'SaveCredential',
      data: request.credential
    }).then(sendResponse);
    return true;
  }

  if (request.type === 'generatePassword') {
    sendNativeMessage({
      type: 'GeneratePassword',
      data: { length: request.length || 16 }
    }).then(sendResponse);
    return true;
  }
});

// Connect on startup
connectNativeHost();
```

**Popup UI** (`popup.html`):
```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>PhantomVault</title>
  <style>
    body {
      width: 320px;
      min-height: 200px;
      margin: 0;
      padding: 16px;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: linear-gradient(135deg, #0C111E 0%, #05070F 100%);
      color: #E0E0E0;
    }
    .header {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      padding-bottom: 12px;
      border-bottom: 1px solid #47E0B8;
    }
    .logo {
      width: 32px;
      height: 32px;
    }
    .title {
      font-size: 18px;
      font-weight: 600;
      color: #47E0B8;
    }
    .status {
      padding: 12px;
      background: rgba(71, 224, 184, 0.1);
      border-radius: 8px;
      margin-bottom: 16px;
    }
    .status.locked {
      background: rgba(255, 122, 106, 0.1);
    }
    .btn {
      width: 100%;
      padding: 10px;
      background: #47E0B8;
      border: none;
      border-radius: 6px;
      color: #05070F;
      font-weight: 600;
      cursor: pointer;
      margin-bottom: 8px;
    }
    .btn:hover {
      background: #3BC9A3;
    }
    .btn.secondary {
      background: #1A2332;
      color: #47E0B8;
      border: 1px solid #47E0B8;
    }
    .credentials {
      max-height: 300px;
      overflow-y: auto;
    }
    .credential-item {
      padding: 10px;
      background: #1A2332;
      border-radius: 6px;
      margin-bottom: 8px;
      cursor: pointer;
    }
    .credential-item:hover {
      background: #2D3F56;
    }
    .credential-title {
      font-weight: 600;
      margin-bottom: 4px;
    }
    .credential-username {
      font-size: 12px;
      color: #B0B0B0;
    }
  </style>
</head>
<body>
  <div class="header">
    <img src="icons/icon48.png" alt="PhantomVault" class="logo">
    <div class="title">PhantomVault</div>
  </div>

  <div id="status" class="status locked">
    <div id="statusText">Vault is locked</div>
  </div>

  <button id="unlockBtn" class="btn">Unlock Vault</button>
  <button id="generateBtn" class="btn secondary">Generate Password</button>
  <button id="openVaultBtn" class="btn secondary">Open Vault</button>

  <div id="credentials" class="credentials"></div>

  <script src="popup.js"></script>
</body>
</html>
```

#### 3. Firefox Extension (1 week)

**Manifest V2** (Firefox still uses V2):
```json
{
  "manifest_version": 2,
  "name": "PhantomVault",
  "version": "1.0.0",
  "description": "Secure password manager with zero-knowledge encryption",
  "permissions": [
    "nativeMessaging",
    "storage",
    "activeTab",
    "tabs",
    "http://*/*",
    "https://*/*"
  ],
  "background": {
    "scripts": ["background.js"]
  },
  "content_scripts": [
    {
      "matches": ["http://*/*", "https://*/*"],
      "js": ["content.js"],
      "css": ["autofill.css"],
      "run_at": "document_idle"
    }
  ],
  "browser_action": {
    "default_popup": "popup.html",
    "default_icon": {
      "16": "icons/icon16.png",
      "48": "icons/icon48.png",
      "128": "icons/icon128.png"
    }
  },
  "icons": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  }
}
```

**Note**: Content and background scripts can be largely reused from Chrome with minor API adaptations (`chrome` → `browser`).

#### 4. Native Messaging Manifest Installation (1 week)

**Windows** (`com.phantomvault.autofill.json`):
```json
{
  "name": "com.phantomvault.autofill",
  "description": "PhantomVault Native Messaging Host",
  "path": "C:\\Program Files\\PhantomVault\\PhantomVault.Autofill.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://YOUR_CHROME_EXTENSION_ID/",
    "chrome-extension://YOUR_FIREFOX_EXTENSION_ID/"
  ]
}
```

**Registry Installation** (Windows):
```
HKEY_LOCAL_MACHINE\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.phantomvault.autofill
HKEY_LOCAL_MACHINE\SOFTWARE\Mozilla\NativeMessagingHosts\com.phantomvault.autofill
```

**macOS** (`~/Library/Application Support/Google/Chrome/NativeMessagingHosts/com.phantomvault.autofill.json`):
```json
{
  "name": "com.phantomvault.autofill",
  "description": "PhantomVault Native Messaging Host",
  "path": "/Applications/PhantomVault.app/Contents/MacOS/PhantomVault.Autofill",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://YOUR_CHROME_EXTENSION_ID/"
  ]
}
```

**Linux** (`~/.config/google-chrome/NativeMessagingHosts/com.phantomvault.autofill.json`):
```json
{
  "name": "com.phantomvault.autofill",
  "description": "PhantomVault Native Messaging Host",
  "path": "/usr/bin/phantomvault-autofill",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://YOUR_CHROME_EXTENSION_ID/"
  ]
}
```

#### 5. Password Capture Implementation (1 week)

**Content Script Enhancement**:
```javascript
// Detect form submission
class PasswordCapture {
  constructor() {
    this.capturedData = null;
  }

  attachSubmitListeners() {
    document.addEventListener('submit', (e) => {
      const form = e.target;
      if (form.tagName !== 'FORM') return;

      const passwordField = form.querySelector('input[type="password"]');
      if (!passwordField || !passwordField.value) return;

      const usernameField = this.findUsernameField(form, passwordField);

      this.capturedData = {
        domain: window.location.hostname,
        url: window.location.href,
        username: usernameField?.value || '',
        password: passwordField.value,
        timestamp: Date.now()
      };

      // Show save prompt
      this.showSavePrompt();
    }, true);
  }

  findUsernameField(form, passwordField) {
    // Try email field first
    const emailField = form.querySelector('input[type="email"]');
    if (emailField) return emailField;

    // Try text field before password
    const inputs = Array.from(form.querySelectorAll('input'));
    const pwIndex = inputs.indexOf(passwordField);

    for (let i = pwIndex - 1; i >= 0; i--) {
      if (inputs[i].type === 'text' || inputs[i].type === 'email') {
        return inputs[i];
      }
    }

    return null;
  }

  showSavePrompt() {
    const toast = document.createElement('div');
    toast.className = 'phantomvault-toast';
    toast.style.cssText = `
      position: fixed;
      top: 20px;
      right: 20px;
      background: white;
      border: 2px solid #47E0B8;
      border-radius: 12px;
      padding: 16px;
      box-shadow: 0 8px 24px rgba(0,0,0,0.2);
      z-index: 999999;
      min-width: 300px;
      animation: slideIn 0.3s ease-out;
    `;

    toast.innerHTML = `
      <style>
        @keyframes slideIn {
          from { transform: translateX(100%); opacity: 0; }
          to { transform: translateX(0); opacity: 1; }
        }
      </style>
      <div style="font-weight: 600; margin-bottom: 8px; color: #333;">
        Save password for ${this.capturedData.domain}?
      </div>
      <div style="font-size: 13px; color: #666; margin-bottom: 12px;">
        Username: ${this.capturedData.username || 'Not detected'}
      </div>
      <div style="display: flex; gap: 8px;">
        <button id="saveBtn" style="
          flex: 1;
          padding: 8px;
          background: #47E0B8;
          border: none;
          border-radius: 6px;
          color: white;
          font-weight: 600;
          cursor: pointer;
        ">Save</button>
        <button id="cancelBtn" style="
          flex: 1;
          padding: 8px;
          background: #f5f5f5;
          border: none;
          border-radius: 6px;
          color: #333;
          font-weight: 600;
          cursor: pointer;
        ">Not Now</button>
      </div>
    `;

    document.body.appendChild(toast);

    // Save button
    toast.querySelector('#saveBtn').addEventListener('click', async () => {
      const response = await chrome.runtime.sendMessage({
        type: 'saveCredential',
        credential: this.capturedData
      });

      if (response.status === 'success') {
        toast.innerHTML = '<div style="color: #47E0B8; font-weight: 600;">✓ Saved!</div>';
        setTimeout(() => toast.remove(), 2000);
      } else {
        toast.innerHTML = `<div style="color: #FF6B6B; font-weight: 600;">Failed: ${response.message}</div>`;
        setTimeout(() => toast.remove(), 3000);
      }
    });

    // Cancel button
    toast.querySelector('#cancelBtn').addEventListener('click', () => {
      toast.remove();
    });

    // Auto-remove after 10 seconds
    setTimeout(() => toast.remove(), 10000);
  }
}

// Initialize password capture
const capture = new PasswordCapture();
capture.attachSubmitListeners();
```

#### 6. Testing & Publication (2 weeks)

**Testing Checklist**:
- [ ] Form detection on major sites (Google, Facebook, GitHub, etc.)
- [ ] Autofill functionality
- [ ] Password capture
- [ ] Popup UI interaction
- [ ] Native messaging reliability
- [ ] Multi-tab support
- [ ] Vault unlock/lock
- [ ] Password generation
- [ ] Cross-browser compatibility

**Chrome Web Store Submission**:
1. Create developer account ($5 one-time fee)
2. Prepare store listing (description, screenshots, privacy policy)
3. Upload extension package (.zip)
4. Submit for review (typically 1-3 days)

**Firefox Add-ons Submission**:
1. Create developer account (free)
2. Prepare store listing
3. Upload extension package (.zip)
4. Submit for review (typically 1-7 days)

---

## PRIORITY 2: Advanced Autofill Features ⭐⭐⭐⭐

**Impact**: MEDIUM - Improves daily usability
**Effort**: MEDIUM (2-3 months)
**Dependencies**: Browser extension completion

### Features

#### 1. Identity Autofill

**Support for**:
- Full name (first, middle, last)
- Email address
- Phone number
- Date of birth
- Address (street, city, state, ZIP, country)
- Company/organization

**Credential Model Extension**:
```csharp
public class IdentityCredential : Credential
{
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }

    public Address? HomeAddress { get; set; }
    public Address? WorkAddress { get; set; }

    public string Company { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
}

public class Address
{
    public string Street1 { get; set; } = string.Empty;
    public string Street2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
```

#### 2. Credit Card Autofill

**Already implemented in Core** (`CreditCardDetailView`, `CreditCardEditForm`)
**Extend for browser autofill**:

```javascript
// Content script enhancement
class CreditCardFiller {
  detectCreditCardForm() {
    const cardNumberField = document.querySelector(
      'input[autocomplete="cc-number"], ' +
      'input[name*="card"], ' +
      'input[id*="card"]'
    );

    if (cardNumberField) {
      return {
        number: cardNumberField,
        expiry: this.findExpiryField(cardNumberField),
        cvv: this.findCVVField(cardNumberField),
        name: this.findNameField(cardNumberField)
      };
    }

    return null;
  }

  fillCreditCard(card) {
    const fields = this.detectCreditCardForm();
    if (!fields) return;

    if (fields.number) {
      fields.number.value = card.number;
      fields.number.dispatchEvent(new Event('input', { bubbles: true }));
    }

    if (fields.expiry) {
      fields.expiry.value = `${card.expiryMonth}/${card.expiryYear}`;
      fields.expiry.dispatchEvent(new Event('input', { bubbles: true }));
    }

    if (fields.cvv) {
      fields.cvv.value = card.cvv;
      fields.cvv.dispatchEvent(new Event('input', { bubbles: true }));
    }

    if (fields.name) {
      fields.name.value = card.cardholderName;
      fields.name.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }
}
```

#### 3. Multi-Page Form Support

**Challenge**: Forms split across multiple pages (e.g., checkout flows)
**Solution**: Session storage for form state

```javascript
class MultiPageFormHandler {
  constructor() {
    this.sessionKey = 'phantomvault_form_session';
  }

  saveFormState(data) {
    const session = {
      domain: window.location.hostname,
      url: window.location.href,
      data: data,
      timestamp: Date.now()
    };

    sessionStorage.setItem(this.sessionKey, JSON.stringify(session));
  }

  getFormState() {
    const stored = sessionStorage.getItem(this.sessionKey);
    if (!stored) return null;

    const session = JSON.parse(stored);

    // Expire after 30 minutes
    if (Date.now() - session.timestamp > 30 * 60 * 1000) {
      sessionStorage.removeItem(this.sessionKey);
      return null;
    }

    // Same domain check
    if (session.domain !== window.location.hostname) {
      return null;
    }

    return session.data;
  }

  clearFormState() {
    sessionStorage.removeItem(this.sessionKey);
  }
}
```

#### 4. Custom Field Mapping

**Allow users to map custom fields**:

```csharp
// In PhantomVault.Core
public class CustomFieldMapping
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string FieldSelector { get; set; } = string.Empty; // CSS selector
    public string CredentialProperty { get; set; } = string.Empty;
    public string FillValue { get; set; } = string.Empty;
}

// Example:
// Domain: "example.com"
// FieldSelector: "#employee-id"
// CredentialProperty: "CustomFields['EmployeeID']"
```

---

## PRIORITY 3: Enhanced Testing Coverage ⭐⭐⭐⭐

**Impact**: MEDIUM - Prevents regressions
**Effort**: MEDIUM (1-2 months)

### Testing Strategy

#### 1. Unit Tests (2 weeks)

**Expand existing tests** (`PhantomVault.Core.Tests`):

```csharp
// New test files
VaultServiceTests.cs
ImportExportServiceTests.cs
PasswordHealthServiceTests.cs
BackupServiceTests.cs
DefenceEngineTests.cs
TamperDetectionTests.cs
AntiKeyloggingTests.cs
MemoryProtectionTests.cs
```

**Example test**:
```csharp
[Fact]
public async Task VaultService_AddCredential_EncryptsData()
{
    // Arrange
    var service = new VaultService(mockEncryption, mockLogger);
    var credential = new Credential
    {
        Title = "Test",
        Username = "user@example.com",
        Password = "SecurePassword123!"
    };

    // Act
    await service.AddCredentialAsync(credential);

    // Assert
    var stored = await service.GetCredentialAsync(credential.Id);
    Assert.NotNull(stored);
    Assert.Equal("Test", stored.Title);
    Assert.Equal("user@example.com", stored.Username);

    // Verify encryption was called
    mockEncryption.Verify(x => x.Encrypt(
        It.IsAny<byte[]>(),
        It.IsAny<byte[]>()
    ), Times.Once);
}
```

#### 2. Avalonia UI Tests (3 weeks)

**Install** `Avalonia.Headless` package:
```xml
<PackageReference Include="Avalonia.Headless" Version="11.3.9" />
<PackageReference Include="Avalonia.Headless.XUnit" Version="11.3.9" />
```

**Create test project**: `PhantomVault.UI.Tests`

```csharp
// VaultUnlockWindowTests.cs
public class VaultUnlockWindowTests
{
    [AvaloniaFact]
    public async Task VaultUnlock_WithCorrectCredentials_Succeeds()
    {
        // Arrange
        var window = new VaultUnlockWindow
        {
            DataContext = new VaultUnlockViewModel(mockVaultService)
        };

        // Act
        var keyfileTextBox = window.FindControl<TextBox>("KeyfilePathTextBox");
        keyfileTextBox.Text = "path/to/valid/keyfile";

        var passphraseTextBox = window.FindControl<TextBox>("PassphraseTextBox");
        passphraseTextBox.Text = "correct_passphrase";

        var unlockButton = window.FindControl<Button>("UnlockButton");
        unlockButton.Command.Execute(null);

        await Task.Delay(100); // Wait for async operations

        // Assert
        var vm = (VaultUnlockViewModel)window.DataContext;
        Assert.True(vm.IsVaultUnlocked);
        Assert.Null(vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task VaultUnlock_WithIncorrectPassword_ShowsError()
    {
        // Arrange
        var window = new VaultUnlockWindow
        {
            DataContext = new VaultUnlockViewModel(mockVaultService)
        };

        // Act
        var keyfileTextBox = window.FindControl<TextBox>("KeyfilePathTextBox");
        keyfileTextBox.Text = "path/to/valid/keyfile";

        var passphraseTextBox = window.FindControl<TextBox>("PassphraseTextBox");
        passphraseTextBox.Text = "wrong_passphrase";

        var unlockButton = window.FindControl<Button>("UnlockButton");
        unlockButton.Command.Execute(null);

        await Task.Delay(100);

        // Assert
        var vm = (VaultUnlockViewModel)window.DataContext;
        Assert.False(vm.IsVaultUnlocked);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Invalid", vm.ErrorMessage);
    }
}
```

#### 3. Integration Tests (2 weeks)

```csharp
// VaultLifecycleIntegrationTests.cs
public class VaultLifecycleIntegrationTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly VaultService _vaultService;

    public VaultLifecycleIntegrationTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"test_vault_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        var encryptionService = new EncryptionService();
        var manifestService = new ManifestService();
        _vaultService = new VaultService(encryptionService, manifestService, null);
    }

    [Fact]
    public async Task CompleteVaultWorkflow_CreateUnlockAddCredentialLock_Succeeds()
    {
        // 1. Create vault
        var keyfile = Path.Combine(_testVaultPath, "vault.key");
        var keyfileData = new byte[2048];
        RandomNumberGenerator.Fill(keyfileData);
        await File.WriteAllBytesAsync(keyfile, keyfileData);

        var manifest = new VaultManifest
        {
            VaultName = "Test Vault",
            KeyfilePath = keyfile
        };

        await _vaultService.CreateVaultAsync(_testVaultPath, manifest, "test_passphrase");

        // 2. Unlock vault
        var unlocked = await _vaultService.UnlockVaultAsync(_testVaultPath, keyfile, "test_passphrase");
        Assert.True(unlocked);

        // 3. Add credential
        var credential = new Credential
        {
            Title = "Test Gmail",
            Username = "test@gmail.com",
            Password = "SecurePassword123!",
            Url = "https://mail.google.com"
        };

        await _vaultService.AddCredentialAsync(credential);

        // 4. Retrieve credential
        var retrieved = await _vaultService.GetCredentialAsync(credential.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test Gmail", retrieved.Title);
        Assert.Equal("test@gmail.com", retrieved.Username);
        Assert.Equal("SecurePassword123!", retrieved.Password);

        // 5. Lock vault
        _vaultService.LockVault();
        Assert.False(_vaultService.IsVaultUnlocked);

        // 6. Verify cannot access credentials when locked
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _vaultService.GetCredentialAsync(credential.Id);
        });
    }

    [Fact]
    public async Task Import_KeePassXML_CreatesCredentials()
    {
        // Create test KeePass XML file
        var xmlContent = @"<?xml version='1.0' encoding='utf-8'?>
<KeePassFile>
  <Root>
    <Group>
      <Name>General</Name>
      <Entry>
        <String><Key>Title</Key><Value>Gmail</Value></String>
        <String><Key>UserName</Key><Value>user@gmail.com</Value></String>
        <String><Key>Password</Key><Value>password123</Value></String>
        <String><Key>URL</Key><Value>https://mail.google.com</Value></String>
      </Entry>
    </Group>
  </Root>
</KeePassFile>";

        var xmlFile = Path.Combine(_testVaultPath, "test_import.xml");
        await File.WriteAllTextAsync(xmlFile, xmlContent);

        // Import
        var importService = new ImportExportService();
        var credentials = await importService.ImportFromKeePassXmlAsync(xmlFile);

        // Verify
        Assert.Single(credentials);
        Assert.Equal("Gmail", credentials[0].Title);
        Assert.Equal("user@gmail.com", credentials[0].Username);
        Assert.Equal("password123", credentials[0].Password);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
        {
            Directory.Delete(_testVaultPath, true);
        }
    }
}
```

#### 4. Performance Benchmarks (1 week)

**Install** `BenchmarkDotNet`:
```xml
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
```

```csharp
// EncryptionBenchmarks.cs
[MemoryDiagnoser]
public class EncryptionBenchmarks
{
    private EncryptionService _service;
    private byte[] _data;
    private byte[] _key;

    [GlobalSetup]
    public void Setup()
    {
        _service = new EncryptionService();
        _data = new byte[1024]; // 1KB
        _key = new byte[32];
        RandomNumberGenerator.Fill(_data);
        RandomNumberGenerator.Fill(_key);
    }

    [Benchmark]
    public byte[] Argon2id_KeyDerivation()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);
        return _service.DeriveKey("password", salt, 32);
    }

    [Benchmark]
    public byte[] AES_GCM_Encrypt()
    {
        return _service.Encrypt(_data, _key);
    }

    [Benchmark]
    public byte[] AES_GCM_Decrypt()
    {
        var encrypted = _service.Encrypt(_data, _key);
        return _service.Decrypt(encrypted, _key);
    }

    [Benchmark]
    public byte[] Layered_Encryption_5Layers()
    {
        var layeredService = new LayeredEncryptionService();
        return layeredService.Encrypt(_data, _key, layers: 5);
    }
}
```

**Run benchmarks**:
```bash
dotnet run -c Release --project PhantomVault.Benchmarks
```

---

## PRIORITY 4: Advanced Password Health Features ⭐⭐⭐⭐

**Impact**: MEDIUM - Proactive security
**Effort**: LOW (2-4 weeks)

### Implementation

#### 1. Have I Been Pwned Integration (1 week)

```csharp
// BreachMonitoringService.cs
public class BreachMonitoringService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BreachMonitoringService>? _logger;

    public BreachMonitoringService(ILogger<BreachMonitoringService>? logger = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhantomVault-PasswordManager");
        _logger = logger;
    }

    public async Task<bool> IsPasswordPwnedAsync(string password)
    {
        try
        {
            // Hash password with SHA-1
            var sha1 = SHA1.HashData(Encoding.UTF8.GetBytes(password));
            var hashHex = Convert.ToHexString(sha1);

            // k-Anonymity: Only send first 5 characters
            var hashPrefix = hashHex[..5];
            var hashSuffix = hashHex[5..];

            // Query API
            var response = await _httpClient.GetAsync($"range/{hashPrefix}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var hashes = content.Split('\n');

            // Check if our hash suffix appears in the list
            foreach (var line in hashes)
            {
                var parts = line.Split(':');
                if (parts.Length != 2) continue;

                var suffix = parts[0].Trim();
                if (suffix.Equals(hashSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var count = int.Parse(parts[1].Trim());
                    _logger?.LogWarning("Password found in {Count} breaches", count);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Breach check failed");
            throw;
        }
    }

    public async Task<int> GetPasswordBreachCountAsync(string password)
    {
        try
        {
            var sha1 = SHA1.HashData(Encoding.UTF8.GetBytes(password));
            var hashHex = Convert.ToHexString(sha1);
            var hashPrefix = hashHex[..5];
            var hashSuffix = hashHex[5..];

            var response = await _httpClient.GetAsync($"range/{hashPrefix}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var hashes = content.Split('\n');

            foreach (var line in hashes)
            {
                var parts = line.Split(':');
                if (parts.Length != 2) continue;

                var suffix = parts[0].Trim();
                if (suffix.Equals(hashSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return int.Parse(parts[1].Trim());
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Breach count check failed");
            return -1; // Error indicator
        }
    }
}
```

**Integration into Password Health**:
```csharp
// In PasswordHealthService.cs
public async Task<PasswordHealthReport> GenerateReportAsync(List<Credential> credentials)
{
    var report = new PasswordHealthReport();
    var breachService = new BreachMonitoringService(_logger);

    foreach (var credential in credentials)
    {
        if (string.IsNullOrEmpty(credential.Password))
            continue;

        var analysis = new CredentialAnalysis
        {
            Credential = credential,
            EntropyScore = CalculateEntropy(credential.Password),
            IsReused = IsPasswordReused(credential, credentials),
            AgeInDays = (DateTime.UtcNow - credential.CreatedUtc).Days
        };

        // Check breach status
        try
        {
            var breachCount = await breachService.GetPasswordBreachCountAsync(credential.Password);
            analysis.BreachCount = breachCount;
            analysis.IsBreached = breachCount > 0;
        }
        catch
        {
            analysis.BreachCount = -1; // Error/unknown
        }

        report.Analyses.Add(analysis);
    }

    return report;
}
```

#### 2. Password Strength Meter (3 days)

```csharp
// PasswordStrengthCalculator.cs
public enum PasswordStrength
{
    VeryWeak,    // 0-25
    Weak,        // 26-50
    Fair,        // 51-75
    Strong,      // 76-90
    VeryStrong   // 91-100
}

public class PasswordStrengthCalculator
{
    public (PasswordStrength Strength, int Score) CalculateStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (PasswordStrength.VeryWeak, 0);

        int score = 0;

        // Length (max 30 points)
        score += Math.Min(password.Length * 2, 30);

        // Character variety (max 40 points)
        if (password.Any(char.IsLower)) score += 10;
        if (password.Any(char.IsUpper)) score += 10;
        if (password.Any(char.IsDigit)) score += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score += 10;

        // Entropy (max 30 points)
        var entropy = CalculateEntropy(password);
        score += Math.Min((int)(entropy / 3), 30);

        // Penalize common patterns
        if (ContainsCommonPatterns(password)) score -= 20;
        if (ContainsSequentialCharacters(password)) score -= 10;
        if (ContainsRepeatedCharacters(password)) score -= 10;

        // Ensure score is in range [0, 100]
        score = Math.Clamp(score, 0, 100);

        var strength = score switch
        {
            <= 25 => PasswordStrength.VeryWeak,
            <= 50 => PasswordStrength.Weak,
            <= 75 => PasswordStrength.Fair,
            <= 90 => PasswordStrength.Strong,
            _ => PasswordStrength.VeryStrong
        };

        return (strength, score);
    }

    private bool ContainsCommonPatterns(string password)
    {
        var commonPatterns = new[]
        {
            "password", "123456", "qwerty", "abc123", "letmein",
            "monkey", "dragon", "master", "welcome", "login"
        };

        return commonPatterns.Any(pattern =>
            password.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private bool ContainsSequentialCharacters(string password)
    {
        for (int i = 0; i < password.Length - 2; i++)
        {
            if (password[i] + 1 == password[i + 1] && password[i + 1] + 1 == password[i + 2])
                return true;
        }
        return false;
    }

    private bool ContainsRepeatedCharacters(string password)
    {
        for (int i = 0; i < password.Length - 2; i++)
        {
            if (password[i] == password[i + 1] && password[i + 1] == password[i + 2])
                return true;
        }
        return false;
    }

    private double CalculateEntropy(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        var frequencies = new Dictionary<char, int>();
        foreach (var c in password)
        {
            frequencies[c] = frequencies.GetValueOrDefault(c, 0) + 1;
        }

        double entropy = 0;
        foreach (var freq in frequencies.Values)
        {
            var probability = (double)freq / password.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy * password.Length;
    }
}
```

**UI Integration** (in `AddEditCredentialWindow.axaml`):
```xml
<!-- Password Strength Indicator -->
<StackPanel Grid.Row="3" Grid.Column="1" Spacing="4">
  <TextBox x:Name="PasswordTextBox"
           Text="{Binding Password}"
           Watermark="Password"
           TextChanged="Password_TextChanged"/>

  <StackPanel Orientation="Horizontal" Spacing="8">
    <Border Width="50" Height="4" Background="{Binding StrengthColor1}" CornerRadius="2"/>
    <Border Width="50" Height="4" Background="{Binding StrengthColor2}" CornerRadius="2"/>
    <Border Width="50" Height="4" Background="{Binding StrengthColor3}" CornerRadius="2"/>
    <Border Width="50" Height="4" Background="{Binding StrengthColor4}" CornerRadius="2"/>
    <Border Width="50" Height="4" Background="{Binding StrengthColor5}" CornerRadius="2"/>
  </StackPanel>

  <TextBlock Text="{Binding StrengthText}"
             FontSize="11"
             Foreground="{Binding StrengthColor}"/>
</StackPanel>
```

#### 3. Password History Tracking (2 days)

```csharp
// Add to Credential model
public class Credential
{
    // Existing properties...

    public List<PasswordHistory> PasswordHistory { get; set; } = new();
}

public class PasswordHistory
{
    public string PasswordHash { get; set; } = string.Empty; // SHA256 hash for comparison
    public DateTimeOffset ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "User"; // Or system, import, etc.
}
```

**Track password changes**:
```csharp
// In VaultService.UpdateCredentialAsync()
public async Task UpdateCredentialAsync(Credential credential)
{
    var existing = await GetCredentialAsync(credential.Id);
    if (existing == null)
        throw new InvalidOperationException("Credential not found");

    // Track password change
    if (existing.Password != credential.Password)
    {
        var passwordHash = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(existing.Password))
        );

        existing.PasswordHistory.Add(new PasswordHistory
        {
            PasswordHash = passwordHash,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = "User"
        });

        // Keep only last 10 password changes
        if (existing.PasswordHistory.Count > 10)
        {
            existing.PasswordHistory.RemoveAt(0);
        }
    }

    // Update credential
    existing.Title = credential.Title;
    existing.Username = credential.Username;
    existing.Password = credential.Password;
    existing.LastUpdatedUtc = DateTimeOffset.UtcNow;

    await SaveDatabaseAsync();
}
```

---

## Timeline & Milestones

**Month 1-2**: Browser Extension
- Week 1-2: Native messaging host enhancement
- Week 3-6: Chrome extension development
- Week 7: Firefox extension adaptation
- Week 8: Testing & bug fixes

**Month 3**: Autofill Features
- Week 9-10: Identity autofill
- Week 11: Credit card autofill
- Week 12: Multi-page forms & custom mappings

**Month 4**: Testing & Health Features
- Week 13-14: Unit tests expansion
- Week 15: UI tests with Avalonia.Headless
- Week 16: Integration tests & benchmarks

**Month 5**: Password Health
- Week 17: Have I Been Pwned integration
- Week 18: Password strength meter
- Week 19: Password history tracking
- Week 20: Testing & documentation

**Month 6**: Polish & Publication
- Week 21-22: Bug fixes, performance optimization
- Week 23: Store listing preparation
- Week 24: Chrome Web Store + Firefox Add-ons submission

---

## Success Metrics

- [ ] Browser extension published to Chrome Web Store
- [ ] Browser extension published to Firefox Add-ons
- [ ] 1000+ extension installations in first month
- [ ] <5% crash rate
- [ ] 90%+ test coverage on core services
- [ ] <100ms autofill response time
- [ ] Positive user reviews (4+ stars average)

---

**Next Phase**: See `ROADMAP_PHASE2.md` for mobile app development plan.
