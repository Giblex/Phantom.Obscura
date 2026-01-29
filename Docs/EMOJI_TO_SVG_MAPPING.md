# Emoji to SVG Icon Mapping

## Available SVG Icons in Assets\SVG

### Navigation & UI
- `chevron-down-icon.svg` - Down arrow
- `chevron-up-icon.svg` - Up arrow
- `chevron-left-icon.svg` - Left arrow
- `chevron-right-icon.svg` - Right arrow
- `menu-icon.svg` - Menu/hamburger
- `more-icon.svg` - More options (3 dots)
- `panel-icon.svg` - Panel/sidebar

### Actions
- `add-icon.svg` - Add/plus
- `delete-icon.svg` - Delete/trash
- `edit-icon.svg` - Edit/pencil
- `close-icon.svg` - Close/X
- `refresh-icon.svg` - Refresh/reload
- `search-icon.svg` - Search/magnifying glass
- `filter-icon.svg` - Filter
- `sort-icon.svg` - Sort

### Security
- `lock-icon.svg` - Locked
- `key-icon.svg` - Key
- `shield-icon.svg` - Shield/protection
- `fingerprint-icon.svg` - Biometric/fingerprint
- `eye-visible-icon.svg` - Show/visible
- `eye-hidden-icon.svg` - Hide/invisible

### Data & Files
- `folder-icon.svg` - Folder
- `database-icon.svg` - Database
- `clipboard-icon.svg` - Clipboard/copy
- `download-icon.svg` - Download
- `upload-icon.svg` - Upload
- `export-icon.svg` - Export
- `import-icon.svg` - Import
- `backup-icon.svg` - Backup

### Status & Feedback
- `success-icon.svg` - Success/checkmark
- `warning-icon.svg` - Warning/alert
- `info-icon.svg` - Information
- `notification-icon.svg` - Notification/bell

### User & Content
- `user-icon.svg` - User/person
- `favourites-icon.svg` - Favorite (outline)
- `favourites-active-icon.svg` - Favorite (filled)
- `tag-icon.svg` - Tag/label
- `notes-icon.svg` - Notes/document

### Features
- `totp-icon.svg` - TOTP/2FA
- `qrcode-icon.svg` - QR code
- `card-icon.svg` - Payment card
- `link-icon.svg` - Link/URL
- `share-icon.svg` - Share
- `usb-icon.svg` - USB drive
- `browser-icon.svg` - Browser
- `recovery-icon.svg` - Recovery
- `health-icon.svg` - Health/status
- `history-icon.svg` - History
- `generate-icon.svg` - Generate

### Settings
- `settings-icon.svg` - Settings/gear
- `gear-svgrepo-com.svg` - Settings gear (alt)
- `logout-icon.svg` - Logout/sign out

## Emoji Replacement Map

### Common Replacements

| Emoji | SVG Icon | Usage |
|-------|----------|-------|
| 🔍 | `search-icon.svg` | Search functionality |
| 🔒 | `lock-icon.svg` | Locked/secure |
| 🔓 | `lock-icon.svg` (with unlock state) | Unlocked |
| 🔑 | `key-icon.svg` | Key/password |
| ⚙️ | `settings-icon.svg` | Settings |
| 👁️ | `eye-visible-icon.svg` | Show/visible |
| 📋 | `clipboard-icon.svg` | Copy/clipboard |
| ❤️/⭐ | `favourites-active-icon.svg` | Favorite (filled) |
| ☆ | `favourites-icon.svg` | Favorite (outline) |
| 🗑️ | `delete-icon.svg` | Delete/trash |
| 📁 | `folder-icon.svg` | Folder/category |
| ⚡ | `shield-icon.svg` | Power/protection |
| 🛡️ | `shield-icon.svg` | Shield/security |
| 📊 | `health-icon.svg` | Stats/health |
| 🔄 | `refresh-icon.svg` | Refresh/sync |
| ✓/✔️ | `success-icon.svg` | Success/done |
| ❌ | `close-icon.svg` | Error/close |
| ➕ | `add-icon.svg` | Add/create |
| 📝 | `edit-icon.svg` or `notes-icon.svg` | Edit/notes |
| 💾 | `backup-icon.svg` | Save/backup |
| 📤 | `export-icon.svg` | Export/upload |
| 📥 | `import-icon.svg` | Import/download |
| ⚠️ | `warning-icon.svg` | Warning/alert |
| ℹ️ | `info-icon.svg` | Information |
| 🔗 | `link-icon.svg` | Link/URL |
| 👤 | `user-icon.svg` | User/profile |
| 🏷️ | `tag-icon.svg` | Tag/label |
| 📈 | `health-icon.svg` | Chart/stats |

### Status Messages
Replace emoji prefixes in status messages with icon components:
- `✓` → Use SvgIcon with `success-icon.svg`
- `⚠` → Use SvgIcon with `warning-icon.svg`
- `❌` → Use SvgIcon with `close-icon.svg`
- `ℹ` → Use SvgIcon with `info-icon.svg`

## Implementation Pattern

### For AXAML Files
```xaml
<!-- Old -->
<TextBlock Text="🔍"/>

<!-- New -->
<controls:SvgIcon Source="avares://PhantomVault.UI/Assets/SVG/search-icon.svg"
                  Width="20" Height="20"/>
```

### For Status Messages (ViewModels)
```csharp
// Old
StatusMessage = "✓ Success!";

// New - Let UI handle icon display
StatusMessage = "Success!";
StatusType = StatusType.Success; // Enum for UI to show appropriate icon
```

### For Dialog Titles
```csharp
// Old
Text = "⚠️ " + title

// New - Use icon parameter
ShowDialog(title, icon: "warning-icon.svg")
```
