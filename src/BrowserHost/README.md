# BrowserHost Folder

## Status: Deprecated / Placeholder

This folder was originally intended to contain browser extension host infrastructure for PhantomVault's browser integration features.

## Current Implementation Location

Browser extension functionality has been moved to:

- **Root Project Folder**: `BrowserExtension/`
  - `BrowserExtension/Chrome/` - Chrome extension manifest and scripts
  - `BrowserExtension/Firefox/` - Firefox extension manifest and scripts  
  - `BrowserExtension/NativeHost/` - Native messaging host executables
  - `BrowserExtension/Install-NativeHost.ps1` - Installation script

- **Autofill Project**: `src/Autofill/`
  - `NativeMessagingHostService.cs` - Native messaging protocol implementation
  - `INativeMessagingHost.cs` - Interface for browser-app communication
  - `WindowsAutofillService.cs` - Windows autofill integration
  - `AndroidAutofillService.cs` - Android autofill framework integration

## Browser Integration Features

PhantomVault includes browser integration through:

1. **Chrome Extension** - WebExtension API for Chrome/Edge/Brave
2. **Firefox Extension** - WebExtension API for Firefox
3. **Native Messaging Host** - Secure stdin/stdout JSON protocol for browser-to-app communication
4. **Autofill Services** - Platform-specific credential injection

## Future Use

This folder may be repurposed for:

- Browser extension development workspace
- Browser-specific test fixtures
- Extension build artifacts

Or it may be removed entirely in a future cleanup.

## Related Documentation

- See `README.md` root section "Browser Extensions" for installation instructions
- See `Docs/IMPLEMENTATION_GUIDE.md` for autofill architecture details
- See `src/UI.Desktop/ViewModels/VaultSettingsViewModel.cs` for extension configuration code

---

**Note**: This folder is currently empty and not referenced by any project files. It can be safely deleted without affecting build or functionality.
