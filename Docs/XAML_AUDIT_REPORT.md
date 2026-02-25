# PhantomVault UI – XAML Style & Animation Audit Report

> **Scope:** `src/UI.Desktop/` — All major view XAML files, style sheets, animations, behaviors, controls, and data templates.  
> **Token System Reference:** SpacingTokens (`Space2`–`Space32`), SizingTokens, ThicknessTokens, Typography tokens (`Type.H1.Size` through `Type.Tiny.Size`), Radius tokens (`Radius.Sm`–`Radius.Pill`), DynamicResource brush system.

---

## 1. Animation Inconsistencies

### 1.1 Pulse Animation – DUPLICATE DEFINITION (Different Durations)

**`Border.pulse`** is defined in **two** files with **different durations**:

| File | Line | Duration |
|------|------|----------|
| [Animations.axaml](src/UI.Desktop/Styles/Animations.axaml#L242) | 242–255 | `0:0:2` |
| [GlobalStyles.axaml](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L741) | 741–754 | `0:0:1` |

```xml
<!-- Animations.axaml L244 -->
<Animation Duration="0:0:2" IterationCount="Infinite">

<!-- GlobalStyles.axaml L743 -->
<Animation Duration="0:0:1" IterationCount="Infinite">
```

Whichever loads last wins. This is a **critical conflict**.

### 1.2 Skeleton Pulse – DUPLICATE DEFINITION (Same Duration, Wasteful)

**`Border.skeleton.pulse`** and **`Border.skeleton-text.pulse`** are defined identically in both files:

| File | Lines | Duration |
|------|-------|----------|
| [Animations.axaml](src/UI.Desktop/Styles/Animations.axaml#L464) | 464–497 | `0:0:1.5` |
| [GlobalStyles.axaml](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L704) | 704–736 | `0:0:1.5` |

Both use `0:0:1.5` so behavior is the same, but the duplication is unnecessary and error-prone.

### 1.3 Inconsistent Transition Durations Across Similar Elements

The codebase uses **12+ different transition durations** with no clear pattern:

| Duration | Usage | File(s) |
|----------|-------|---------|
| `0:0:0.1` | Button RenderTransform | [GlobalStyles L70](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L70) |
| `0:0:0.12` | QuickFilter highlight opacity | [SidebarView L440](src/UI.Desktop/Views/SidebarView.axaml#L440) |
| `0:0:0.15` | Button Background, icon Background, TextBox border, scrollbar auto-hide border | [Animations L12](src/UI.Desktop/Styles/Animations.axaml#L12), [Animations L58](src/UI.Desktop/Styles/Animations.axaml#L58), [GlobalStyles L69](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L69), [GlobalStyles L194](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L194) |
| `0:0:0.18` | LiquidGlass most properties, QuickFilter margin | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml), [SidebarView L438](src/UI.Desktop/Views/SidebarView.axaml#L438) |
| `0:0:0.2` | Accent button background, `icon-animated` hover, search clear opacity, BoxShadow on detailBorderInteractive | [Animations L35](src/UI.Desktop/Styles/Animations.axaml#L35), [Animations L139](src/UI.Desktop/Styles/Animations.axaml#L139), [Animations L157](src/UI.Desktop/Styles/Animations.axaml#L157), [Animations L507](src/UI.Desktop/Styles/Animations.axaml#L507) |
| `0:0:0.25` | LiquidGlass sheen margin, search-bar opacity, Toggle switch hover background, MFA card entrance | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml), [Animations L123](src/UI.Desktop/Styles/Animations.axaml#L123), [Animations L229](src/UI.Desktop/Styles/Animations.axaml#L229), [Animations L383](src/UI.Desktop/Styles/Animations.axaml#L383) |
| `0:0:0.3` | Copy feedback, fade-in, strength-meter value, scrollbar opacity, TOTP-success bg, biometric auth bg | [Animations L199](src/UI.Desktop/Styles/Animations.axaml#L199), [Animations L273](src/UI.Desktop/Styles/Animations.axaml#L273), [Animations L426](src/UI.Desktop/Styles/Animations.axaml#L426), [Animations L440](src/UI.Desktop/Styles/Animations.axaml#L440), [Animations L582](src/UI.Desktop/Styles/Animations.axaml#L582), [GlobalStyles L656](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L656) |
| `0:0:0.35` | Slide-in opacity | [Animations L216](src/UI.Desktop/Styles/Animations.axaml#L216) |
| `0:0:0.4` | List item stagger, MFA card stagger, strength-meter foreground, sidebar fade, biometric notify | [Animations L79](src/UI.Desktop/Styles/Animations.axaml#L79), [Animations L288-289](src/UI.Desktop/Styles/Animations.axaml#L288), [GlobalStyles L657](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L657), [Animations L600](src/UI.Desktop/Styles/Animations.axaml#L600) |
| `0:0:0.6` | MFA card entrance animation, TOTP toggle animation, fingerprint scan | [Animations L348](src/UI.Desktop/Styles/Animations.axaml#L348), [Animations L365](src/UI.Desktop/Styles/Animations.axaml#L365), [Animations L538](src/UI.Desktop/Styles/Animations.axaml#L538) |
| `0:0:1.0` | Biometric icon pulse | [Animations L405](src/UI.Desktop/Styles/Animations.axaml#L405) |
| `0:0:1.2` | TOTP timer opacity, biometric auth pulse | [Animations L459](src/UI.Desktop/Styles/Animations.axaml#L459), [Animations L609](src/UI.Desktop/Styles/Animations.axaml#L609) |
| `0:0:1.5` | Skeleton shimmer (x3 definitions) | [Animations L466](src/UI.Desktop/Styles/Animations.axaml#L466), [Animations L489](src/UI.Desktop/Styles/Animations.axaml#L489), [Animations L564](src/UI.Desktop/Styles/Animations.axaml#L564) |
| `0:0:2.0` | Pulse in Animations.axaml | [Animations L244](src/UI.Desktop/Styles/Animations.axaml#L244) |

**Recommendation:** Standardize on a small set of duration tokens (e.g., `Anim.Fast=0.15s`, `Anim.Normal=0.25s`, `Anim.Slow=0.4s`, `Anim.Slower=0.6s`).

### 1.4 Inconsistent Easing Functions

| Easing | Used In |
|--------|---------|
| `CubicEaseOut` | Most transitions in Animations.axaml, SidebarView highlight |
| `CubicEaseInOut` | MFA toggle switch ([Animations L383](src/UI.Desktop/Styles/Animations.axaml#L383)), biometric auth ([Animations L582](src/UI.Desktop/Styles/Animations.axaml#L582)), sidebar collapse |
| `SineEaseInOut` | TOTP timer opacity ([Animations L459](src/UI.Desktop/Styles/Animations.axaml#L459)) |
| (none / Linear) | LiquidGlass transitions, strength-meter value, scrollbar opacity |
| `LinearEasing` | LoadingSpinner rotation |

Buttons use **no easing** on LiquidGlass transitions but **CubicEaseOut** on accent-animated - inconsistent feel.

### 1.5 SecurityDashboard Has Non-Standard Animation Timing

[SecurityDashboard.axaml](src/UI.Desktop/Controls/SecurityDashboard.axaml) defines inline `.sec-stat-card` hover lift at `0:0:0.25` while the HoverLiftBehavior default is `0.2s` and GlobalStyles strength-meter at `0:0:0.3/0:0:0.4`. The `.health-score` progress bar uses `0:0:0.6` duration which matches no other progress bar timing in the app.

---

## 2. Hardcoded Colors

### 2.1 Hardcoded `Foreground="White"` (Should use `Brush.TextOnDark` or similar)

| File | Line | Snippet |
|------|------|---------|
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L426) | 426 | `Foreground="White"` (warning banner icon) |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L572) | 572 | `Foreground="White"` (credential icon text) |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L705) | 705 | `Foreground="White"` (trash item icon text) |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L1008) | 1008 | `Foreground="White"` (detail panel icon) |
| [TotpSettingsWindow.axaml](src/UI.Desktop/Views/TotpSettingsWindow.axaml#L167) | 167, 218, 276, 438 | `Foreground="White"` (status text, processing text) |
| [WindowsHelloSettingsWindow.axaml](src/UI.Desktop/Views/WindowsHelloSettingsWindow.axaml#L225) | 225 | `Foreground="White"` (authenticating text) |
| [RecoveryWindow.axaml](src/UI.Desktop/Views/RecoveryWindow.axaml#L116) | 116, 296, 327, 360, 402, 427 | `Foreground="White"` (result icons) |
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml#L330) | 330 | `Foreground="White"` (icon text on colored bg) |
| [ThemePreviewWindow.axaml](src/UI.Desktop/Views/ThemePreviewWindow.axaml) | 43, 91, 111, 155, 355, 373, 415, 491, 509, 551, 619, 637, 679, 736 | Multiple `Foreground="White"` |

### 2.2 Hardcoded `Foreground="Gray"` 

| File | Line | Snippet |
|------|------|---------|
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | (subtitle) | `Foreground="Gray"` — should use `MutedTextBrush` |

### 2.3 Hardcoded Hex Colors in View XAML (not brush definitions)

| File | Lines | Colors | Context |
|------|-------|--------|---------|
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml) | ~440–490+ | `#40FFFFFF`, `#00FFFFFF` | Shimmer gradient on every dashboard card (repeated 12+ times) |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L249) | 249–284 | `#40004258`, `#30004258`, `#25004258`, `#2C004258`, `#24004258` | Quick-filter toggle button gradient (inline styles) |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L341) | 341–389 | `#18000000`, `#10000000`, `#20000000`, `#14000000`, `#28000000`, `#1C000000` | Category-filter gradient states |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L51) | 51 | `#00FFFFFF` | TileOverlayBrush resource |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L1048) | 1048 | `ButtonBackground="#199c8e"` | Inline button color override |
| [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) | ~460–480 | `#40FFFFFF`, `#08FFFFFF`, `#28FFFFFF`, `#88FFFFFF`, `#C6FFFFFF`, `#00FFFFFF` | QuickFilter highlight glass effect |
| [TotpSettingsWindow.axaml](src/UI.Desktop/Views/TotpSettingsWindow.axaml) | 34–36, 69, 75–77, 90–91, 184, 187, 190, 240–241, 297–298, 345, 349, 389–390, 418–419 | ~20 unique hex colors | Glass gradients, TOTP state colors, backgrounds |
| [CategoryManagerView.axaml](src/UI.Desktop/Views/CategoryManagerView.axaml) | 205–236 | `#FF6B6B`, `#4ECDC4`, `#FFE66D`, `#A78BFA`, `#FDE68A`, `#BFDBFE`, `#C7D2FE`, `#FBCFE8`, `#A7F3D0`, `#E5E7EB`, `#FECACA`, `#D1FAE5`, `#E9D5FF` | Color palette swatches * |
| [AutofillSuggestionsWindow.axaml](src/UI.Desktop/Views/AutofillSuggestionsWindow.axaml#L135) | 135 | `#4078C0` | Inline background color |
| [SecurityDashboard.axaml](src/UI.Desktop/Controls/SecurityDashboard.axaml) | Inline styles | `#18FFFFFF`, `#10FFFFFF` | Stat card hover effects |
| [CredentialListView.axaml](src/UI.Desktop/Views/CredentialListView.axaml#L367) | 367–368 | `#12000000`, `#00000000` | Gradient shadow border |

\* *Note: Color palette swatches in CategoryManagerView are intentionally hardcoded for the color picker UI, which is acceptable.*

### 2.4 Hardcoded Colors in Animations.axaml

| Line | Color | Context |
|------|-------|---------|
| [Animations.axaml L70](src/UI.Desktop/Styles/Animations.axaml#L70) | `#10FFFFFF` | `icon-animated:pointerover` background |
| [Animations.axaml L74](src/UI.Desktop/Styles/Animations.axaml#L74) | `#20FFFFFF` | `icon-animated:pressed` background |

### 2.5 Hardcoded Colors in LiquidGlass.axaml

Extensive hardcoded colors throughout (not using brush tokens):

| Lines | Colors | Context |
|-------|--------|---------|
| All button backgrounds | `#04FFFFFF`, `#08FFFFFF`, `#12FFFFFF` | Base/hover/press states |
| Primary variant | `#60A855F7`, `#70A855F7`, `#80A855F7` | Primary button gradients |
| Danger variant | `#70DC3545`, `#80DC3545` | Danger button states |
| Tile hover | `#101F2D`, `#1A2A38` | Tile-button hover/press bg |
| Foreground | `#FFFFFF` | Hardcoded white foreground |

**Impact:** Themes cannot override LiquidGlass button colors without re-defining the entire style.

---

## 3. Hardcoded Sizes & Spacing

### 3.1 Hardcoded `FontSize` Values (Should Use Typography Tokens)

**VaultWindow.axaml** — **55+ instances** of hardcoded FontSize:

| FontSize | Count | Sample Lines |
|----------|-------|-------------|
| `"10"` | 3 | [1541](src/UI.Desktop/Views/VaultWindow.axaml#L1541), [1545](src/UI.Desktop/Views/VaultWindow.axaml#L1545), [1549](src/UI.Desktop/Views/VaultWindow.axaml#L1549) |
| `"11"` | 15+ | [1225](src/UI.Desktop/Views/VaultWindow.axaml#L1225), [1251](src/UI.Desktop/Views/VaultWindow.axaml#L1251), [1334](src/UI.Desktop/Views/VaultWindow.axaml#L1334), [1336](src/UI.Desktop/Views/VaultWindow.axaml#L1336), [1351](src/UI.Desktop/Views/VaultWindow.axaml#L1351), [1364](src/UI.Desktop/Views/VaultWindow.axaml#L1364), [1373](src/UI.Desktop/Views/VaultWindow.axaml#L1373), [1399](src/UI.Desktop/Views/VaultWindow.axaml#L1399), [1436](src/UI.Desktop/Views/VaultWindow.axaml#L1436), [1469](src/UI.Desktop/Views/VaultWindow.axaml#L1469), [1525](src/UI.Desktop/Views/VaultWindow.axaml#L1525), [1657](src/UI.Desktop/Views/VaultWindow.axaml#L1657) |
| `"12"` | 5+ | [439](src/UI.Desktop/Views/VaultWindow.axaml#L439), [1567](src/UI.Desktop/Views/VaultWindow.axaml#L1567), [1582](src/UI.Desktop/Views/VaultWindow.axaml#L1582) |
| `"13"` | 15+ | [435](src/UI.Desktop/Views/VaultWindow.axaml#L435), [453](src/UI.Desktop/Views/VaultWindow.axaml#L453), [690](src/UI.Desktop/Views/VaultWindow.axaml#L690), [765](src/UI.Desktop/Views/VaultWindow.axaml#L765), [790](src/UI.Desktop/Views/VaultWindow.axaml#L790), [815](src/UI.Desktop/Views/VaultWindow.axaml#L815), [840](src/UI.Desktop/Views/VaultWindow.axaml#L840), [856](src/UI.Desktop/Views/VaultWindow.axaml#L856), [872](src/UI.Desktop/Views/VaultWindow.axaml#L872), [904](src/UI.Desktop/Views/VaultWindow.axaml#L904), [1053](src/UI.Desktop/Views/VaultWindow.axaml#L1053), [1167](src/UI.Desktop/Views/VaultWindow.axaml#L1167), [1207](src/UI.Desktop/Views/VaultWindow.axaml#L1207), [1233](src/UI.Desktop/Views/VaultWindow.axaml#L1233), [1261](src/UI.Desktop/Views/VaultWindow.axaml#L1261), [1381](src/UI.Desktop/Views/VaultWindow.axaml#L1381), [1433](src/UI.Desktop/Views/VaultWindow.axaml#L1433), [1475](src/UI.Desktop/Views/VaultWindow.axaml#L1475), [1513](src/UI.Desktop/Views/VaultWindow.axaml#L1513), [1531](src/UI.Desktop/Views/VaultWindow.axaml#L1531), [1593](src/UI.Desktop/Views/VaultWindow.axaml#L1593), [1615](src/UI.Desktop/Views/VaultWindow.axaml#L1615), [1653](src/UI.Desktop/Views/VaultWindow.axaml#L1653), [1683](src/UI.Desktop/Views/VaultWindow.axaml#L1683) |  
| `"14"` | 8+ | [747](src/UI.Desktop/Views/VaultWindow.axaml#L747), [757](src/UI.Desktop/Views/VaultWindow.axaml#L757), [1022](src/UI.Desktop/Views/VaultWindow.axaml#L1022), [1213](src/UI.Desktop/Views/VaultWindow.axaml#L1213), [1239](src/UI.Desktop/Views/VaultWindow.axaml#L1239), [1297](src/UI.Desktop/Views/VaultWindow.axaml#L1297), [1387](src/UI.Desktop/Views/VaultWindow.axaml#L1387), [1599](src/UI.Desktop/Views/VaultWindow.axaml#L1599), [1604](src/UI.Desktop/Views/VaultWindow.axaml#L1604) |
| `"16"` | 5+ | [425](src/UI.Desktop/Views/VaultWindow.axaml#L425), [685](src/UI.Desktop/Views/VaultWindow.axaml#L685), [899](src/UI.Desktop/Views/VaultWindow.axaml#L899), [981](src/UI.Desktop/Views/VaultWindow.axaml#L981), [984](src/UI.Desktop/Views/VaultWindow.axaml#L984), [1057](src/UI.Desktop/Views/VaultWindow.axaml#L1057) |
| `"18"` | 3 | [468](src/UI.Desktop/Views/VaultWindow.axaml#L468), [593](src/UI.Desktop/Views/VaultWindow.axaml#L593), [725](src/UI.Desktop/Views/VaultWindow.axaml#L725) |
| `"20"` | 1 | [1163](src/UI.Desktop/Views/VaultWindow.axaml#L1163) |
| `"22"` | 1 | [1014](src/UI.Desktop/Views/VaultWindow.axaml#L1014) |
| `"32"` | 2 | [580](src/UI.Desktop/Views/VaultWindow.axaml#L580), [712](src/UI.Desktop/Views/VaultWindow.axaml#L712) |
| `"34"` | 1 | [1008](src/UI.Desktop/Views/VaultWindow.axaml#L1008) |
| `"48"` | 2 | [572](src/UI.Desktop/Views/VaultWindow.axaml#L572), [705](src/UI.Desktop/Views/VaultWindow.axaml#L705) |
| `"80"` | 2 | [681](src/UI.Desktop/Views/VaultWindow.axaml#L681), [895](src/UI.Desktop/Views/VaultWindow.axaml#L895) |

**Other files with significant hardcoded FontSize:**

| File | Hardcoded FontSizes | Count |
|------|---------------------|-------|
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml) | `12`, `13`, `14` | 20+ (all quick-action checkboxes use `FontSize="13"`, card labels `14`) |
| [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) | `12`, `13` | 10+ (all filter labels use `13`, section headers `12`) |
| [HeaderView.axaml](src/UI.Desktop/Views/HeaderView.axaml) | `11`, `12`, `13`, `14`, `16` | 8+ |
| [SettingsWindow.axaml](src/UI.Desktop/Views/SettingsWindow.axaml) | `12`, `16`, `20` | 10+ |
| [PasswordGeneratorWindow.axaml](src/UI.Desktop/Views/PasswordGeneratorWindow.axaml) | `13`, `20` | 5+ |
| [VeraCryptSetupWindow.axaml](src/UI.Desktop/Views/VeraCryptSetupWindow.axaml) | `15` | 11 instances |
| [CredentialListView.axaml](src/UI.Desktop/Views/CredentialListView.axaml) | `12`, `18`, `20` | 3+ |
| [StatusBarView.axaml](src/UI.Desktop/Views/StatusBarView.axaml) | `11` | 2 (while also using `Type.Small.Size` once—inconsistent) |
| [AboutWindow.axaml](src/UI.Desktop/Views/AboutWindow.axaml) | `11`, `12`, `18` | 3 |
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | `20` | 1 |
| [ImportWindow.axaml](src/UI.Desktop/Views/ImportWindow.axaml) | `12`, `20` | 2 |
| [FrontPageWindow.axaml](src/UI.Desktop/Views/FrontPageWindow.axaml) | `26` | 1 |
| [VaultUnlockWindow.axaml](src/UI.Desktop/Views/VaultUnlockWindow.axaml) | `11`, `12`, `14`, `16`, `24` | 5+ |
| [EmptyState.axaml](src/UI.Desktop/Controls/EmptyState.axaml) | `14`, `20` | 2 |
| [ToastNotification.axaml](src/UI.Desktop/Controls/ToastNotification.axaml) | `13`, `14` | 2 |
| [SecurityDashboard.axaml](src/UI.Desktop/Controls/SecurityDashboard.axaml) | `13`, `32` | 2+ |

**Cross-reference with token values:**
- `Type.Caption.Size` exists → yet `FontSize="11"` is hardcoded ~20 times
- `Type.Small.Size` exists → yet `FontSize="12"` is hardcoded ~30 times
- `Type.Body.Size` exists → yet `FontSize="13"` / `FontSize="14"` hardcoded ~50 times
- `Type.H3.Size` exists → yet `FontSize="16"` hardcoded ~15 times  
- `Type.H2.Size` exists → yet `FontSize="18"` / `FontSize="20"` hardcoded ~10 times
- `Type.H1.Size` exists → yet `FontSize="24"` / `FontSize="26"` hardcoded ~5 times

### 3.2 Hardcoded Widths and Heights

| File | Line(s) | Value | Context |
|------|---------|-------|---------|
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | - | `Width="600" Height="200"` | Window size |
| [SettingsWindow.axaml](src/UI.Desktop/Views/SettingsWindow.axaml) | Multiple | `Width="140"`, `Width="200"`, `Width="220"`, `Width="240"` | Button sizes in same dialog |
| [SettingsWindow.axaml](src/UI.Desktop/Views/SettingsWindow.axaml) | Multiple | `Height="36"` | Repeated on multiple buttons |
| [PasswordGeneratorWindow.axaml](src/UI.Desktop/Views/PasswordGeneratorWindow.axaml) | Footer | `Width="150"`, `Width="110"`, `Width="100"`, `Width="90"` | 4 different widths for footer buttons |
| [FrontPageWindow.axaml](src/UI.Desktop/Views/FrontPageWindow.axaml) | - | `Width="160" Height="40"` | All 3 action buttons |
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | - | `Width="100"` | Cancel button |
| [AboutWindow.axaml](src/UI.Desktop/Views/AboutWindow.axaml) | - | `Width="80" Height="30"` | Close button |
| [SetupWizardWindow.axaml](src/UI.Desktop/Views/SetupWizardWindow.axaml) | Step indicator | `Width="32" Height="32" CornerRadius="16"` | Repeated 7 times for step circles |
| [VaultUnlockWindow.axaml](src/UI.Desktop/Views/VaultUnlockWindow.axaml) | - | `Width="70" Height="70"` | Progress circle |
| [HeaderView.axaml](src/UI.Desktop/Views/HeaderView.axaml) | Clear button | `Width="30" Height="30"` | Search clear button |
| [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) | Collapsed icons | `Width="42" Height="42"` | Repeated for all collapsed sidebar icons |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | Sidebar columns | `80` / `220` | Hardcoded in converter parameter |
| [CategoryManagerView.axaml](src/UI.Desktop/Views/CategoryManagerView.axaml) | Swatches | `Width="24" Height="24"` | Repeated 9 times |

### 3.3 Hardcoded `FontFamily`

| File | Line | Value |
|------|------|-------|
| [SetupWizardWindow.axaml](src/UI.Desktop/Views/SetupWizardWindow.axaml) | Window root | `FontFamily="Segoe UI"` |

Should use `{StaticResource MainFontFamily}` or equivalent token.

### 3.4 Hardcoded `ScrollBar` Width

| File | Context | Value |
|------|---------|-------|
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | ScrollViewer styling | ScrollBar Width `4` |

---

## 4. Inconsistent Button Styles

### 4.1 Completely Unstyled Buttons (No Classes)

| File | Button | Code |
|------|--------|------|
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | "Unlock Vault" | `<Button Content="Unlock Vault">` — no Classes, no transitions |
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | "Create New Vault" | `<Button Content="Create New Vault">` — no Classes |
| [AboutWindow.axaml](src/UI.Desktop/Views/AboutWindow.axaml) | "Close" | `<Button Click="Close_Click" Width="80" Height="30">Close</Button>` — no Classes |
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | "Cancel" | `<Button Content="Cancel" Width="100">` — no Classes (while "Start Export" has `Classes="accent"`) |

### 4.2 Mixed Button Style Approaches Within Same Dialog/View

| File | Issue |
|------|-------|
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | "Start Export" = `Classes="accent"`, "Cancel" = **plain unstyled** |
| [ImportWindow.axaml](src/UI.Desktop/Views/ImportWindow.axaml) | "Import Credentials" = inline `Background="{DynamicResource AccentBrush}"` (not liquid-glass), "Cancel" = `Classes="liquid-glass"` |
| [PasswordGeneratorWindow.axaml](src/UI.Desktop/Views/PasswordGeneratorWindow.axaml) | Footer buttons mix `Classes="accent"` with inline `Background="{DynamicResource AccentBrush}"` which **overrides** the class style |
| [CredentialDataTemplates.axaml](src/UI.Desktop/Resources/DataTemplates/CredentialDataTemplates.axaml) | Copy Username = plain inline `Background`/`Border`, Copy Password = `LiquidGlassButton`, Edit = plain inline — **3 different approaches in one row** |
| [SettingsWindow.axaml](src/UI.Desktop/Views/SettingsWindow.axaml) | Mix of widths (`140`, `200`, `220`, `240`) for buttons that should be uniform |

### 4.3 Button Class Naming Conflicts

| Class | File 1 | File 2 | Conflict |
|-------|--------|--------|----------|
| `.tile-button` | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml) | [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml) | Both define tile-button with **different hover behavior** (LiquidGlass: `#101F2D` bg, ButtonStyles: transparent bg) |
| `.tile-button-grid` | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml) | [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml) | Same class defined in two files |
| `.header-action` | [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml) | [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) inline styles | VaultWindow redefines header-action inline (~L206–232) |

### 4.4 Inline Button Styles That Should Be Extracted

| File | Lines | What |
|------|-------|------|
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml) | ~22–160 | `.dashboard-card` and `.dashboard-nav` full button styles with inline glass effects |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | ~126–175 | `.tile-button` override |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | ~206–232 | `.header-action` override |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | ~254–390 | Quick-filter toggle button styles (130+ lines of inline gradients) |
| [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) | ~12–85 | `.nav-button` style (70+ lines) |
| [SecurityDashboard.axaml](src/UI.Desktop/Controls/SecurityDashboard.axaml) | Inline | `.sec-stat-card` with full hover/shadow transitions |

---

## 5. Missing Hover/Press States

### 5.1 Buttons Without Any Hover/Press State

| File | Button | Notes |
|------|--------|-------|
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | "Unlock Vault", "Create New Vault" | No Classes, no templates — uses default Avalonia chrome |
| [AboutWindow.axaml](src/UI.Desktop/Views/AboutWindow.axaml) | "Close" | No Classes at all |
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | "Cancel" | No Classes, no hover effect |
| [ImportWindow.axaml](src/UI.Desktop/Views/ImportWindow.axaml) | "Import Credentials" | Has inline `Background` which overrides any theme hover |

### 5.2 Elements Missing Hover State Where Expected

| File | Element | Issue |
|------|---------|-------|
| [CredentialListView.axaml](src/UI.Desktop/Views/CredentialListView.axaml) | Sort ComboBox | No hover transition or visual feedback |
| [HeroHeader.axaml](src/UI.Desktop/Controls/HeroHeader.axaml) | Entire control | Uses glass-card with no entrance animation or hover response |
| [CategoryManagerView.axaml](src/UI.Desktop/Views/CategoryManagerView.axaml) | Color palette swatch buttons | Have `Classes="palette-swatch"` but no defined hover style found in GlobalStyles or Animations |

---

## 6. Missing Transitions

### 6.1 Windows Without Entrance Animation

These windows do **NOT** have `Classes="animated-window"`:

| File | Window |
|------|--------|
| [MainWindow.axaml](src/UI.Desktop/Views/MainWindow.axaml) | MainWindow (initial launcher) |
| [AboutWindow.axaml](src/UI.Desktop/Views/AboutWindow.axaml) | About dialog |
| [CategoryManagerWindow.axaml](src/UI.Desktop/Views/CategoryManagerWindow.axaml) | Category manager dialog |
| [ExportWindow.axaml](src/UI.Desktop/Views/ExportWindow.axaml) | Export dialog |
| [ImportWindow.axaml](src/UI.Desktop/Views/ImportWindow.axaml) | Import dialog |
| [VaultUnlockWindow.axaml](src/UI.Desktop/Views/VaultUnlockWindow.axaml) | Vault unlock dialog |
| [VeraCryptSetupWindow.axaml](src/UI.Desktop/Views/VeraCryptSetupWindow.axaml) | VeraCrypt setup |
| [TotpSettingsWindow.axaml](src/UI.Desktop/Views/TotpSettingsWindow.axaml) | TOTP settings |

Windows that **DO** have `Classes="animated-window"`:
VaultWindow, ThemePreviewWindow, SignInDialog, ShareWindow, SetupWizardWindow, SettingsWindow, SecuritySettingsWindow, RecoveryWindow, PasswordHealthWindow, PasswordGeneratorWindow, KeyboardShortcutsWindow, FrontPageWindow, DecoyPreviewWindow, AddEditCredentialWindow, AccessibilitySettingsWindow.

### 6.2 Missing Property Transitions

| File | Element | Missing Transition |
|------|---------|-------------------|
| [SettingsWindow.axaml](src/UI.Desktop/Views/SettingsWindow.axaml) | TabControl | No tab-switching transition (opacity, slide, or any) |
| [CredentialListView.axaml](src/UI.Desktop/Views/CredentialListView.axaml) | Sort ComboBox | No border/background transition on focus |
| [StatusBarView.axaml](src/UI.Desktop/Views/StatusBarView.axaml) | Status text / encryption indicator | No fade/opacity transition on status change |
| [HeaderView.axaml](src/UI.Desktop/Views/HeaderView.axaml) | Action menu flyout items | Flyout buttons missing entrance stagger animation |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | Detail panel swap | No cross-fade when switching between credential details |

---

## 7. Duplicate / Conflicting Styles

### 7.1 Duplicate Style Selectors Across Files

| Selector | Files | Conflict Level |
|----------|-------|----------------|
| `Border.pulse` | [Animations.axaml L242](src/UI.Desktop/Styles/Animations.axaml#L242), [GlobalStyles.axaml L741](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L741) | **HIGH** — different durations (2s vs 1s) |
| `Border.skeleton.pulse` | [Animations.axaml L464](src/UI.Desktop/Styles/Animations.axaml#L464), [GlobalStyles.axaml L704](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L704) | MEDIUM — identical but wasteful |
| `Border.skeleton-text.pulse` | [Animations.axaml L487](src/UI.Desktop/Styles/Animations.axaml#L487), [GlobalStyles.axaml L720](src/UI.Desktop/Assets/Styles/GlobalStyles.axaml#L720) | MEDIUM — identical but wasteful |
| `Button.tile-button` | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml), [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml) | **HIGH** — different hover backgrounds |
| `Button.tile-button-grid` | [LiquidGlass.axaml](src/UI.Desktop/Styles/LiquidGlass.axaml), [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml) | **HIGH** — different definitions |
| `Button.header-action` | [ButtonStyles.axaml](src/UI.Desktop/Resources/Styles/ButtonStyles.axaml), [VaultWindow.axaml inline](src/UI.Desktop/Views/VaultWindow.axaml) | MEDIUM — inline override shadows shared style |

### 7.2 View-Level Inline Styles Duplicating Shared Styles

| File | Lines | Duplicates |
|------|-------|------------|
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml) | ~126–390 | Re-defines tile-button, header-action, quick-filter, category-filter styles that exist in ButtonStyles.axaml/LiquidGlass.axaml |
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml) | ~22–160 | Defines dashboard-card and dashboard-nav glass effects instead of reusing LiquidGlass classes |
| [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) | ~12–85 | `.nav-button` is functionally very similar to VaultWindow's quick-filter but uses different class name |
| [SecurityDashboard.axaml](src/UI.Desktop/Controls/SecurityDashboard.axaml) | Inline styles | `.sec-stat-card` duplicates card hover/shadow pattern from GlobalStyles `.glass-card` |

### 7.3 Duplicated Gradient Patterns

The gradient `#40004258` → `#30004258` → `#25004258` appears in:
- [VaultWindow.axaml L249–251](src/UI.Desktop/Views/VaultWindow.axaml#L249) (quick-filter)
- [SidebarView.axaml](src/UI.Desktop/Views/SidebarView.axaml) (nav-button — same colors)

These identical gradients should be extracted to a shared brush resource.

### 7.4 Repeated Inline Shimmer/Highlight Pattern

The following pattern is copy-pasted on **every** dashboard card button (12 times in DashboardView.axaml):

```xml
<Border Grid.RowSpan="2" CornerRadius="10" Opacity="0.15" IsHitTestVisible="False">
    <Border.Background>
        <LinearGradientBrush StartPoint="0%,50%" EndPoint="100%,50%">
            <GradientStop Color="#40FFFFFF" Offset="0"/>
            <GradientStop Color="#00FFFFFF" Offset="1"/>
        </LinearGradientBrush>
    </Border.Background>
</Border>
```

Should be extracted as a shared `ContentTemplate` or `ControlTemplate` overlay.

---

## 8. Typography Inconsistencies

### 8.1 FontWeight Mismatches with Token Design

| File | Line | Issue |
|------|------|-------|
| [DashboardView.axaml](src/UI.Desktop/Views/DashboardView.axaml) | Title heading | Uses `FontWeight="Bold"` for H1-level text, but `Type.Weight.Light` is defined for H1 in the token system |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L580) | L580 | `FontSize="32" FontWeight="Bold"` — should be H1 token with Light weight |
| [VaultWindow.axaml](src/UI.Desktop/Views/VaultWindow.axaml#L712) | L712 | `FontSize="32" FontWeight="Bold"` — same issue |

### 8.2 Mixed Token Usage Within Same View

| File | Issue |
|------|-------|
| [StatusBarView.axaml](src/UI.Desktop/Views/StatusBarView.axaml) | Uses `{StaticResource Type.Small.Size}` once but `FontSize="11"` twice in the same file |
| [SetupWizardWindow.axaml](src/UI.Desktop/Views/SetupWizardWindow.axaml) | Step content uses `{StaticResource Type.Body.Size}`, `{StaticResource Type.H3.Size}`, `{StaticResource Type.Small.Size}` correctly — but step indicator circles use hardcoded `FontSize="12"` and `FontSize="9"` |
| [HeaderView.axaml](src/UI.Desktop/Views/HeaderView.axaml) | Uses `{StaticResource Space12}` for spacing (good) but all FontSize values hardcoded |
| [AddEditCredentialWindow.axaml](src/UI.Desktop/Views/AddEditCredentialWindow.axaml) | Uses `{StaticResource Type.Label.Size}` and `{StaticResource Type.Body.Size}` well but color picker buttons use hardcoded `Width="42" Height="42"` |

### 8.3 Unmapped FontSize Values

Some hardcoded sizes don't correspond to any existing token:

| Value | Used In | Closest Token |
|-------|---------|---------------|
| `FontSize="9"` | SetupWizardWindow step numbers | None (below Type.Tiny.Size) |
| `FontSize="10"` | VaultWindow authenticator labels, SetupWizardWindow badges | None / Type.Tiny.Size? |
| `FontSize="15"` | VeraCryptSetupWindow (11 instances) | Between Type.Body and Type.H3 |
| `FontSize="22"` | VaultWindow detail title | Between Type.H2 and Type.H1? |
| `FontSize="26"` | FrontPageWindow welcome | Between Type.H1 and Type.H2? |
| `FontSize="32"` | VaultWindow credential name, SecurityDashboard stat value | No token (extra-large) |
| `FontSize="34"` | VaultWindow detail icon | No token |
| `FontSize="48"` | VaultWindow icon text | No token (display) |
| `FontSize="80"` | VaultWindow empty state icon | No token (display) |

**Recommendation:** Add display-level tokens: `Type.Display.Size`, `Type.DisplayLg.Size`, `Type.DisplayXl.Size` to cover 32, 48, 80 values.

### 8.4 `FontWeight` Inconsistencies

Various `FontWeight="SemiBold"` scattered throughout without a weight token:
- VaultWindow: `FontWeight="SemiBold"` on field labels (L1053 etc.), button text
- SidebarView: `FontWeight="Bold"` on section headers
- DashboardView: `FontWeight="SemiBold"` on card labels
- HeaderView: No consistent FontWeight on action items

The token system defines `Type.Weight.Light` and `Type.Weight.Medium` but **not** `Type.Weight.SemiBold` or `Type.Weight.Bold`, yet these are used extensively.

---

## Summary Statistics

| Category | Finding Count |
|----------|--------------|
| Animation inconsistencies | 5 major issues + 12+ duration variants |
| Hardcoded colors | 100+ instances across 15+ files |
| Hardcoded sizes/spacing | 150+ FontSize instances, 30+ Width/Height instances |
| Inconsistent button styles | 4 unstyled buttons, 5 mixed-approach dialogs, 3 class conflicts |
| Missing hover/press states | 7 buttons/elements without hover feedback |
| Missing transitions | 8 windows without entrance animation, 5 missing property transitions |
| Duplicate styles | 6 duplicate selectors, 4 views with redundant inline styles, 2 repeated gradient patterns |
| Typography inconsistencies | 9 unmapped font sizes, 3 FontWeight mismatches, 4 mixed-usage files |

---

## Priority Recommendations

1. **CRITICAL:** Resolve `Border.pulse` duplicate (Animations.axaml vs GlobalStyles.axaml) — pick one duration, remove the other
2. **CRITICAL:** Resolve `tile-button` / `tile-button-grid` conflicts between LiquidGlass.axaml and ButtonStyles.axaml  
3. **HIGH:** Create animation duration tokens and replace all 12+ ad-hoc duration values
4. **HIGH:** Replace all hardcoded `FontSize` with typography token references (150+ replacements)
5. **HIGH:** Add `animated-window` class to the 8 windows currently missing it
6. **HIGH:** Style the 4 completely unstyled buttons with liquid-glass or secondary classes
7. **MEDIUM:** Extract repeated shimmer overlay into a shared template/resource
8. **MEDIUM:** Extract VaultWindow inline styles (~260 lines) into shared style files
9. **MEDIUM:** Replace `Foreground="White"` with `{DynamicResource Brush.TextOnDark}` throughout (30+ instances)
10. **LOW:** Add missing typography tokens for display sizes (32, 48, 80) and `Type.Weight.SemiBold`
