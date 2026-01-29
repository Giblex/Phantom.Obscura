# Emoji to SVG Icon Replacement Guide

## Summary

This guide documents how to replace all emoji icons in the PhantomVault UI with professional SVG icons from `Assets/SVG`.

## Progress

- ✅ **DialogService.cs** - Emoji prefixes removed from dialog titles
- ⏳ **ViewModel Status Messages** - Need to remove emoji prefixes
- ⏳ **AXAML Views** - Need to replace TextBlocks with SvgIcon controls
- ⏳ **Data Templates** - Need to update credential templates

## Quick Reference: Available SVG Icons

```
add-icon.svg          - Add/Plus (➕)
backup-icon.svg       - Backup (💾)
browser-icon.svg      - Browser
card-icon.svg         - Payment Card
clipboard-icon.svg    - Clipboard/Copy (📋)
close-icon.svg        - Close/X (❌)
database-icon.svg     - Database
delete-icon.svg       - Delete/Trash (🗑️)
download-icon.svg     - Download (📥)
edit-icon.svg         - Edit/Pencil (📝)
export-icon.svg       - Export (📤)
eye-hidden-icon.svg   - Hide Password
eye-visible-icon.svg  - Show Password (👁️)
favourites-icon.svg   - Favorite Outline (☆)
favourites-active-icon.svg - Favorite Filled (⭐)
filter-icon.svg       - Filter
fingerprint-icon.svg  - Biometric
folder-icon.svg       - Folder (📁)
generate-icon.svg     - Generate
health-icon.svg       - Health/Stats (📊)
history-icon.svg      - History
import-icon.svg       - Import (📥)
info-icon.svg         - Information (ℹ️)
key-icon.svg          - Key (🔑)
link-icon.svg         - Link/URL (🔗)
lock-icon.svg         - Lock (🔒/🔓)
menu-icon.svg         - Menu
more-icon.svg         - More Options
notes-icon.svg        - Notes
notification-icon.svg - Notification
qrcode-icon.svg       - QR Code
recovery-icon.svg     - Recovery
refresh-icon.svg      - Refresh/Sync (🔄)
search-icon.svg       - Search (🔍)
settings-icon.svg     - Settings (⚙️)
share-icon.svg        - Share
shield-icon.svg       - Shield/Protection (🛡️)
sort-icon.svg         - Sort
success-icon.svg      - Success/Checkmark (✓)
tag-icon.svg          - Tag/Label (🏷️)
totp-icon.svg         - TOTP/2FA
upload-icon.svg       - Upload
usb-icon.svg          - USB Drive
user-icon.svg         - User/Profile (👤)
warning-icon.svg      - Warning/Alert (⚠️)
```

## Step-by-Step Replacement Plan

### 1. Update CredentialViewModel (Favorite Icon)

**File**: `ViewModels/CredentialViewModel.cs:182`

Current:
```csharp
public string FavoriteIcon => IsFavorite ? "⭐" : "☆";
```

**Option A - Keep property, change to icon path**:
```csharp
public string FavoriteIconPath => IsFavorite
    ? "avares://PhantomVault.UI/Assets/SVG/favourites-active-icon.svg"
    : "avares://PhantomVault.UI/Assets/SVG/favourites-icon.svg";
```

**Option B - Use boolean directly in XAML** (Recommended):
Remove the property entirely and bind directly to `IsFavorite` with a converter.

### 2. Update CredentialDataTemplates.axaml

**File**: `Resources/Templates/DataTemplates/CredentialDataTemplates.axaml`

**Locations**: Lines 116, 365

Current:
```xaml
<TextBlock Text="{Binding FavoriteIcon}" FontSize="..." />
```

New:
```xaml
<controls:SvgIcon Width="18" Height="18" VerticalAlignment="Center">
    <controls:SvgIcon.Source>
        <Binding Path="IsFavorite">
            <Binding.Converter>
                <StaticResource x:Key="FavoriteIconConverter"/>
            </Binding.Converter>
        </Binding>
    </controls:SvgIcon.Source>
</controls:SvgIcon>
```

**Create Converter** in `Converters/FavoriteIconConverter.cs`:
```csharp
public class FavoriteIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isFavorite = value is bool b && b;
        return isFavorite
            ? "avares://PhantomVault.UI/Assets/SVG/favourites-active-icon.svg"
            : "avares://PhantomVault.UI/Assets/SVG/favourites-icon.svg";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

### 3. Update VaultWindow.axaml

**File**: `Views/VaultWindow.axaml:507`

Current:
```xaml
<TextBlock Text="{Binding SelectedCredential.FavoriteIcon}" FontSize="16"/>
```

New:
```xaml
<controls:SvgIcon Source="{Binding SelectedCredential.IsFavorite, Converter={StaticResource FavoriteIconConverter}}"
                  Width="20" Height="20"/>
```

### 4. Update AddEditCredentialViewModel

**File**: `ViewModels/AddEditCredentialViewModel.cs:97,105`

Current:
```csharp
"🔐", "🔒", "🔓", "🔑", "🛡️", "⚠️",
...
"⚙️", "🔧", "🔨", "🏠", "🏢", "🏪", "🏥", "✈️", "🚗", "🎓", "📚"
```

These are emoji icon choices for credentials. Replace with:

```csharp
// Map to SVG icon names instead
private readonly string[] _availableIcons = new[]
{
    "lock-icon.svg", "key-icon.svg", "shield-icon.svg", "warning-icon.svg",
    "settings-icon.svg", "folder-icon.svg", "card-icon.svg", "user-icon.svg",
    "browser-icon.svg", "database-icon.svg", "link-icon.svg"
};
```

### 5. Update Status Message ViewModels

Remove emoji prefixes from status messages. These files need updating:

**PasswordGeneratorViewModel.cs**:
```csharp
// Before
StatusMessage = "⚠ Select at least one character type";
StatusMessage = "✓ Generated {PasswordLength}-character password";

// After
StatusMessage = "Select at least one character type";
StatusType = MessageType.Warning;

StatusMessage = $"Generated {PasswordLength}-character password";
StatusType = MessageType.Success;
```

**Files to update**:
- IconDownloaderViewModel.cs (26 instances)
- ImportViewModel.cs (3 instances)
- PasswordGeneratorViewModel.cs (5 instances)
- ProvisionViewModel.cs (15 instances)
- UsbSetupViewModel.cs (8 instances)
- VaultSettingsViewModel.cs (7 instances)
- TotpSettingsViewModel.cs (1 instance)
- SecurityCheckScreenViewModel.cs (8 instances)
- InstallerViewModel.cs (3 instances)

### 6. Create StatusType Enum (If Needed)

**File**: `Models/StatusType.cs`
```csharp
public enum StatusType
{
    None,
    Success,
    Warning,
    Error,
    Info
}
```

Add property to ViewModels:
```csharp
private StatusType _statusType;
public StatusType StatusType
{
    get => _statusType;
    set => this.RaiseAndSetIfChanged(ref _statusType, value);
}
```

### 7. Update UI to Show Status Icons

Where status messages are displayed, add an icon:

```xaml
<StackPanel Orientation="Horizontal" Spacing="8">
    <controls:SvgIcon Width="16" Height="16" VerticalAlignment="Center">
        <controls:SvgIcon.IsVisible>
            <Binding Path="StatusType" Converter="{StaticResource NotEqualConverter}" ConverterParameter="None"/>
        </controls:SvgIcon.IsVisible>
        <controls:SvgIcon.Source>
            <Binding Path="StatusType" Converter="{StaticResource StatusTypeToIconConverter}"/>
        </controls:SvgIcon.Source>
    </controls:SvgIcon>
    <TextBlock Text="{Binding StatusMessage}" TextWrapping="Wrap"/>
</StackPanel>
```

**Create Converter** `Converters/StatusTypeToIconConverter.cs`:
```csharp
public class StatusTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StatusType statusType)
        {
            return statusType switch
            {
                StatusType.Success => "avares://PhantomVault.UI/Assets/SVG/success-icon.svg",
                StatusType.Warning => "avares://PhantomVault.UI/Assets/SVG/warning-icon.svg",
                StatusType.Error => "avares://PhantomVault.UI/Assets/SVG/close-icon.svg",
                StatusType.Info => "avares://PhantomVault.UI/Assets/SVG/info-icon.svg",
                _ => null
            };
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

### 8. Update InstallerViewModel Status Icons

**File**: `ViewModels/InstallerViewModel.cs:173,179,185`

Current:
```csharp
public string VeraCryptStatusIcon => IsVeraCryptInstalled ? "✓" : "⚠";
public string WindowsHelloStatusIcon => _isWindowsHelloAvailable ? "✓" : "○";
public string YubiKeyStatusIcon => _isYubiKeyDetected ? "✓" : "○";
```

New:
```csharp
public string VeraCryptStatusIconPath => IsVeraCryptInstalled
    ? "avares://PhantomVault.UI/Assets/SVG/success-icon.svg"
    : "avares://PhantomVault.UI/Assets/SVG/warning-icon.svg";

public string WindowsHelloStatusIconPath => _isWindowsHelloAvailable
    ? "avares://PhantomVault.UI/Assets/SVG/success-icon.svg"
    : "avares://PhantomVault.UI/Assets/SVG/info-icon.svg";

public string YubiKeyStatusIconPath => _isYubiKeyDetected
    ? "avares://PhantomVault.UI/Assets/SVG/success-icon.svg"
    : "avares://PhantomVault.UI/Assets/SVG/info-icon.svg";
```

## Testing Checklist

After making changes:

- [ ] Build succeeds with no errors
- [ ] Favorite icon displays correctly (filled/outline)
- [ ] Favorite icon toggles when clicked
- [ ] Status messages show appropriate icons
- [ ] Credential icons display in list/grid view
- [ ] Dialog titles no longer have emoji prefixes
- [ ] All icon SVGs load correctly
- [ ] Icons scale properly at different sizes
- [ ] Icons respect theme colors (if applicable)

## Automated Cleanup (Completed)

✅ **DialogService.cs** - Removed emoji prefixes from:
- `"ℹ️ " + title` → `title`
- `"⚠️ " + title` → `title`
- `"❌ " + (title ?? "Error")` → `title ?? "Error"`

## Manual Tasks Remaining

1. Create FavoriteIconConverter
2. Create StatusTypeToIconConverter
3. Update CredentialDataTemplates.axaml (3 locations)
4. Update VaultWindow.axaml
5. Update AddEditCredentialViewModel icon choices
6. Clean up status messages in 11 ViewModels
7. Update InstallerViewModel status icons
8. Add StatusType enum and properties
9. Update UI templates to show status icons

## Estimated Impact

- **Files Modified**: ~15-20 files
- **Lines Changed**: ~100-150 lines
- **New Files**: 2-3 converters
- **Build Impact**: Should compile cleanly
- **Visual Impact**: More professional, consistent icon system

## Benefits

✅ Professional appearance
✅ Consistent icon sizing
✅ Theme-aware colors
✅ Scalable vector graphics
✅ No emoji font dependencies
✅ Better accessibility
