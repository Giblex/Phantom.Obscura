# Window State Persistence Feature

**Date:** January 26, 2026  
**Status:** ✅ Complete

## Overview

The PhantomVault application now includes comprehensive window state persistence, allowing the MainWindow to remember its position, size, and state (maximized/normal) across application restarts. This provides a seamless user experience where the application opens exactly where the user left it.

## Features Implemented

### 1. **Window Position Memory**

- Saves the X and Y coordinates of the window
- Validates saved position is still on-screen (handles multi-monitor setup changes)
- Falls back to center screen if saved position is off-screen

### 2. **Window Size Memory**

- Saves window width and height
- Enforces minimum dimensions (400x300) for usability
- Restores exact size from previous session

### 3. **Window State Memory**

- Remembers if window was maximized or normal
- Automatically skips restoring minimized state on startup (would be confusing)
- Saves state when user maximizes/restores window

### 4. **Automatic Save on Changes**

- Window position changes trigger automatic save
- Window resize triggers automatic save
- Window state changes (maximize/restore) trigger automatic save
- Final save on window close ensures nothing is lost

## Technical Implementation

### Files Modified

1. **[SettingsService.cs](../src/UI.Desktop/Services/SettingsService.cs)**
   - Added `MainWindowX`, `MainWindowY` properties for position
   - Added `MainWindowWidth`, `MainWindowHeight` properties for size
   - Added `MainWindowState` property for window state

2. **[MainWindow.axaml.cs](../src/UI.Desktop/Views/MainWindow.axaml.cs)**
   - Loads settings on initialization
   - Calls `WindowStateManager.RestoreMainWindowState()` to restore state
   - Attaches event handlers via `WindowStateManager.AttachStateChangeHandlers()`
   - Saves state automatically when window changes

3. **[MainWindow.axaml](../src/UI.Desktop/Views/MainWindow.axaml)**
   - Added `MinWidth="400"` and `MinHeight="300"` to prevent too-small windows
   - Default width/height of 600x200 used only on first launch

### Files Created

1. **[WindowStateManager.cs](../src/UI.Desktop/Services/WindowStateManager.cs)**
   - Static utility class for window state management
   - `RestoreMainWindowState()` - Restores window from settings
   - `SaveMainWindowState()` - Saves window to settings
   - `AttachStateChangeHandlers()` - Monitors window for changes
   - `IsPositionOnScreen()` - Validates position is visible on any monitor

## Architecture

### Design Decisions

1. **Reusable Service Pattern**: `WindowStateManager` is designed as a static utility that can be extended to support other windows (VaultWindow, SettingsWindow, etc.)

2. **Settings Integration**: Window state is stored in the same `UserSettings` object used for other app preferences, ensuring consistent persistence

3. **Safety First**: Multiple validation checks ensure the window always opens in a visible location, even if:
   - Monitor configuration changed
   - Saved position is off-screen
   - Settings are corrupted

4. **Automatic Persistence**: Uses Avalonia's reactive observables to automatically save state without manual intervention

## Usage Example

### For MainWindow (Already Implemented)

```csharp
public partial class MainWindow : Window
{
    private UserSettings? _settings;

    public MainWindow()
    {
        InitializeComponent();
        
        // Load and restore window state
        _settings = SettingsService.Load();
        WindowStateManager.RestoreMainWindowState(this, _settings);
        
        // Attach auto-save handlers
        WindowStateManager.AttachStateChangeHandlers(this, SaveWindowState);
    }

    private void SaveWindowState()
    {
        if (_settings != null)
        {
            WindowStateManager.SaveMainWindowState(this, _settings);
            SettingsService.Save(_settings);
        }
    }
}
```

### Extending to Other Windows

The `WindowStateManager` can be extended to support other windows by:

1. Adding properties to `UserSettings` (e.g., `VaultWindowX`, `VaultWindowY`)
2. Creating `RestoreVaultWindowState()` and `SaveVaultWindowState()` methods
3. Following the same pattern in the window's constructor

## Settings File Location

Window state is persisted in:

```csharp
%APPDATA%\PhantomVault\settings.json
```csharp

Example settings file:

```json
{
  "MainWindowX": 100,
  "MainWindowY": 150,
  "MainWindowWidth": 800,
  "MainWindowHeight": 600,
  "MainWindowState": "Maximized",
  "IsDarkTheme": true,
  "RenderScale": 1.0,
  ...
}
```

## Testing

### Test Scenarios

✅ **Test 1: Basic Position/Size Persistence**

1. Launch app
2. Move window to a specific position
3. Resize window
4. Close app
5. Relaunch app
6. **Expected:** Window opens in same position and size

✅ **Test 2: Maximized State Persistence**

1. Launch app
2. Maximize window
3. Close app
4. Relaunch app
5. **Expected:** Window opens maximized

✅ **Test 3: Multi-Monitor Handling**

1. Launch app on secondary monitor
2. Move window to secondary monitor
3. Close app
4. Disconnect secondary monitor
5. Relaunch app
6. **Expected:** Window opens centered on primary monitor (not off-screen)

✅ **Test 4: Minimized State Handling**

1. Launch app
2. Minimize window
3. Close app via taskbar
4. Relaunch app
5. **Expected:** Window opens in Normal state (not minimized)

## Benefits

1. **Better UX**: Application remembers user's preferred layout
2. **Multi-Monitor Support**: Handles monitor configuration changes gracefully
3. **Professional Feel**: Modern applications always remember window state
4. **Extensible**: Easy to add window state memory to other windows
5. **Reliable**: Multiple fallbacks ensure window is always visible

## Future Enhancements

Potential improvements for the future:

1. **Per-Window Settings**: Add state persistence for VaultWindow, SettingsWindow, etc.
2. **Layout Profiles**: Allow users to save multiple window layouts
3. **Monitor Awareness**: Remember which monitor window was on and prefer that monitor
4. **Split Screen Support**: Detect and remember split-screen positions
5. **State Import/Export**: Allow backing up window preferences with other settings

## Related Documentation

- [User Settings System](./USER_SETTINGS_SYSTEM.md) (if exists)
- [Settings Service Architecture](./SETTINGS_SERVICE.md) (if exists)
- [Avalonia Window Management](https://docs.avaloniaui.net/docs/controls/window)

## Notes

- Window state is only saved when window is in Normal state (not while maximized) to avoid saving incorrect sizes
- Minimum window dimensions are enforced to prevent unusable tiny windows
- The system gracefully handles corrupted settings by falling back to defaults
- Settings are persisted to disk immediately on any state change (no delays)
