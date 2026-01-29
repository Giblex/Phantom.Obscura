# Theme Settings Migration

## Changes Made

### Overview

Moved all theme-related settings from the Accessibility tab to the Theme tab for better organization and logical grouping.

## Files Modified

### 1. **New ViewModel Created**

**File:** `src/UI.Desktop/ViewModels/Settings/ThemeSettingsViewModel.cs`

**Properties:**

- `IsDarkTheme` - Toggle between light/dark mode
- `SelectedThemeSkin` - Choose from 6 theme skins (Default, Midnight Blue, Forest Green, Royal Purple, Sunset Orange, Ocean Teal)
- `EnableHighContrast` - High contrast mode for accessibility
- `SelectedDisplayScale` - UI scale (80%, 90%, 100%, 110%, 125%, 150%)
- `ReduceAnimations` - Minimize motion effects
- `ReduceTransparency` - Use solid colors instead of transparency

**Commands:**

- `ToggleThemeCommand` - Switch between light and dark themes

**Methods:**

- `ApplyThemeSkin()` - Apply selected theme skin
- `ApplyHighContrast()` - Toggle high contrast mode
- `ApplyDisplayScale()` - Adjust UI scaling
- `ApplyAnimationSettings()` - Control animation behavior
- `ApplyTransparencySettings()` - Control transparency effects

### 2. **Theme View Enhanced**

**File:** `src/UI.Desktop/Views/Settings/ThemeSettingsView.axaml`

**New Sections:**

#### Appearance Section

- Dark mode toggle switch
- Theme skin dropdown (6 skins)
- High contrast checkbox
- Display scale dropdown (6 levels)
- Apply Theme button
- Live preview panel showing:
  - Accent color
  - Primary text
  - Secondary text
  - Muted text

#### Motion & Effects Section

- Reduce animations checkbox
- Reduce transparency effects checkbox

**Code-Behind:**

- Wired up `ThemeSettingsViewModel` as DataContext
- Added proper namespace imports

### 3. **Accessibility View Cleaned Up**

**File:** `src/UI.Desktop/Views/Settings/AccessibilitySettingsView.axaml`

**Removed Sections:**

- ❌ Theme & Appearance section (moved to Theme tab)
- ❌ Motion & Effects section (moved to Theme tab)

**Remaining Sections:**

- ✅ Language & Region
- ✅ View Preferences (entry view, icons, colors, font size)
- ✅ Keyboard & Navigation

### 4. **Accessibility ViewModel Simplified**

**File:** `src/UI.Desktop/ViewModels/Settings/AccessibilitySettingsViewModel.cs`

**Removed Properties:**

- `_selectedThemeSkin`
- `_enableHighContrast`
- `_selectedDisplayScale`
- `_reduceAnimations`
- `_reduceTransparency`

**Removed Methods:**

- `ApplyThemeSkin()`
- `ApplyHighContrast()`
- `ApplyDisplayScale()`
- `ApplyAnimationSettings()`
- `ApplyTransparencySettings()`

**Remaining Focus:**

- Language settings
- View preferences (icons, colors, font)
- Keyboard shortcuts
- Screen reader support

## Benefits

### Better Organization

- **Theme Tab:** All visual appearance settings in one place
  - Theme mode (light/dark)
  - Theme skins
  - Display scaling
  - Contrast settings
  - Motion effects
  
- **Accessibility Tab:** Focus on true accessibility features
  - Language/localization
  - View preferences
  - Keyboard navigation
  - Screen reader support

### Logical Grouping

- Theme settings = "How does it look?"
- Accessibility settings = "How do I interact with it?"

### User Experience

- Easier to find theme-related settings
- Accessibility tab is less cluttered
- Clear separation of concerns

## Testing

### Before Running

1. Build succeeded with 0 warnings, 0 errors ✅
2. All bindings properly wired ✅
3. ViewModels initialized in code-behind ✅

### What to Test

1. **Theme Tab:**
   - Toggle dark mode switch
   - Select different theme skins from dropdown
   - Enable/disable high contrast
   - Change display scale
   - Click "Apply Theme" button
   - Verify live preview updates
   - Toggle animation/transparency settings

2. **Accessibility Tab:**
   - Verify theme sections are gone
   - Check remaining sections work correctly
   - Test language selection
   - Test view preferences
   - Test keyboard shortcuts button
   - Verify checkboxes for screen reader, animations, etc.

3. **Scrolling:**
   - Both tabs should scroll properly (nested ScrollViewer fixed)
   - Bottom content should be fully accessible

## Build Status

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All changes compile successfully and are ready for testing.
