# PhantomVault Icons & Logos Library

**Version:** 1.0  
**Last Updated:** October 15, 2025  
**Total Assets:** ~1,400+ icons and logos

---

## 📁 Folder Structure

```
Assets/Icons/
├── Icons/              # General UI icons (~700 files)
│   ├── Actions/       # Add, Save, Delete, Export, etc.
│   ├── Categories/    # Apps, Finance, Personal, Work, etc.
│   ├── Communication/ # Mail, messaging icons
│   ├── Finance/       # Credit cards, wallet, banking
│   ├── Security/      # Lock, password visibility icons
│   ├── Shopping/      # Shopping cart, shopping bag
│   ├── Social/        # Social media related icons
│   ├── Study/         # Education icons
│   └── UI/           # Dashboard, settings, navigation
│
├── Logos/             # Brand/Company logos (~600 files)
│   └── Circle/       # Circular logo variants (~100 files)
│
└── README.md         # This file
```

---

## 🎨 Icon Categories

### **Actions/** (20+ icons)
User action icons for buttons and menus.

| Filename | Usage | Size |
|----------|-------|------|
| `Add.png` | Add new entry button | 48x48 |
| `Small_add.png` | Compact add button | 24x24 |
| `Add Folder.png` | Create folder/category | 48x48 |
| `Save.png` | Save changes | 48x48 |
| `exit.png` | Exit/Close window | 48x48 |
| `small_exit.png` | Compact close button | 24x24 |
| `Download_export.png` | Download/Export data | 48x48 |
| `export.png` | Export alternative | 48x48 |
| `upload_folder.png` | Upload/Import | 48x48 |
| `rubbish.png` | Delete/Trash | 48x48 |
| `Back.png` | Navigate back | 48x48 |

### **Security/** (5 icons)
Password and security-related icons.

| Filename | Usage | Emoji Equivalent |
|----------|-------|------------------|
| `Locked.png` | Vault locked state | 🔒 |
| `unlocked.png` | Vault unlocked state | 🔓 |
| `Hide_password.png` | Hide password field | 🙈 |
| `unhide_password.png` | Show password field | 👁️ |
| `view_password.png` | Toggle password visibility | 👁️ |

### **Categories/** (8 icons)
Credential categorization icons.

| Filename | Usage | Emoji |
|----------|-------|-------|
| `Apps.png` | Applications category | 📱 |
| `Finance.png` | Financial accounts | 💰 |
| `General.png` | General/Uncategorized | 📋 |
| `Personal.png` | Personal accounts | 👤 |
| `Social.png` | Social media | 📱 |
| `Study.png` / `Study1.png` | Education | 📚 |
| `Work.png` | Work/Professional | 💼 |

### **Finance/** (3 icons)
Financial UI elements.

| Filename | Usage |
|----------|-------|
| `Credit cards.png` | Credit card management |
| `IDCards.png` | ID card storage |
| `wallet.png` | Wallet/Payment methods |

### **UI/** (15+ icons)
General user interface elements.

| Filename | Usage |
|----------|-------|
| `Dashboard.png` | Main dashboard view |
| `home.png` / `Home2.png` | Home screen |
| `History.png` | Activity history |
| `settings.png` / `settings2.png` / `settings3.png` | Settings menu |
| `theme.png` | Theme selector |
| `filter.png` | Filter/Search |
| `more.png` | More options menu |
| `menutab.png` | Tab navigation |
| `pin.png` | Pin/Favorite |
| `attatch.png` | Attachment icon |
| `web.png` | Website/Browser |
| `Favourites checked.png` / `unchecked.png` | Favorite toggle |
| `bookmark checked.png` / `unchecked.png` | Bookmark toggle |

---

## 🏢 Brand Logos

### **Logos/** Folder
Contains **600+ brand logos** for auto-icon matching.

#### **Technology & Software**
- **Design Tools:** Adobe (Photoshop, Illustrator, After Effects, Premiere Pro, Lightroom, Acrobat), Figma, Sketch, Canva, Blender, GIMP, Inkscape, CorelDRAW
- **Operating Systems:** Microsoft, Apple, Linux (various distros)
- **Cloud Services:** Google, Amazon, Microsoft, Dropbox *(Note: More to be added)*
- **Browsers:** Chrome, Firefox, Edge, Safari, Opera, Brave, Vivaldi, Tor, DuckDuckGo
- **Communication:** WhatsApp, Telegram, Messenger, Discord, Slack, Skype, Zoom, Line
- **Development:** *GitHub, GitLab, Bitbucket to be added*

#### **Social Media**
Facebook, Instagram, Twitter/X, LinkedIn, TikTok, Snapchat, Reddit, Pinterest, YouTube, Twitch, Vimeo, Threads, Quora, Medium, Soundcloud, Spotify

#### **Financial Services**
- **International:** PayPal, Visa, Mastercard, American Express, Stripe, Skrill, Payoneer, Wise
- **Banks:** HSBC, Citi, Santander, Goldman Sachs, Morgan Stanley, J.P. Morgan
- **Indonesian Banks:** BCA, BNI, BRI, Mandiri, CIMB Niaga, Danamon, BSI, BTN, Jago, Jenius, Livin
- **Indonesian Payment:** Gopay, Ovo, Dana, LinkAja!, ShopeePay, AstraPay, Doku

#### **E-commerce & Retail**
Amazon, eBay, Etsy, Shopee, Tokopedia, Lazada, Blibli, Rakuten, Alibaba, Walmart

#### **Automotive**
BMW, Mercedes-Benz, Audi, Volkswagen, Porsche, Ferrari, Toyota, Honda, Nissan, Ford, Tesla, KIA, Hyundai, Range Rover

#### **Fashion & Luxury**
Gucci, Prada, Louis Vuitton, Chanel, Dior, Versace, Hermes, Burberry, Nike, Adidas, Puma, ZARA, H&M, Uniqlo

#### **Food & Beverage**
McDonald's, KFC, Starbucks, Coca-Cola, Pepsi, Nestle, Nescafe

#### **Job & Dating Platforms**
LinkedIn, Indeed, Glassdoor, Glints, Fiverr, Upwork, Tinder, Bumble, Hinge, Match

---

## 🔍 Auto-Icon Matching System

### **How It Works**

The `IconManager` service automatically matches icons to credentials:

1. **By filename:** Searches for `{website-domain}.png` or `{title-first-word}.png`
2. **By URL domain:** Extracts domain from credential URL (e.g., `netflix.com` → `netflix.png`)
3. **Fallback to emoji:** Returns predefined emoji if no file found

### **Naming Convention**

For auto-matching to work, logo files should be named:
```
{brand-name-lowercase}.png
```

**Examples:**
- `netflix.png` → Matches "Netflix", "netflix.com", "www.netflix.com"
- `google.png` → Matches "Google Account", "google.com", "gmail.com"
- `facebook.png` → Matches "Facebook", "facebook.com", "fb.com"
- `github.png` → Matches "GitHub", "github.com"

### **Supported Formats**
- `.png` (preferred for UI icons)
- `.svg` (preferred for scalable logos)
- `.jpg` / `.jpeg` (supported)
- `.ico` (application icons only)

### **File Size Recommendations**
- **UI Icons:** 48x48px to 128x128px PNG
- **Brand Logos:** 256x256px to 512x512px PNG or SVG
- **Max file size:** 1MB (enforced by SecureIconDownloaderService)

---

## 📥 Adding New Icons

### **Method 1: Manual Addition**
1. Download icon/logo (PNG or SVG)
2. Rename to lowercase brand/service name
3. Place in appropriate folder:
   - UI icons → `Icons/{Category}/`
   - Brand logos → `Logos/`
4. Optional: Add SVG version with same name

### **Method 2: Flaticon Integration**
1. Open credential → Click "🎨 Browse All Icons"
2. Search for icon/logo in Icon Downloader
3. Select icon and download
4. Service saves to `Assets/Icons/` with auto-generated name
5. **Recommended:** Rename numbered file to brand name for auto-matching

### **Method 3: Bulk Import**
Run `organize-assets.ps1` to batch organize icons.

---

## 🔧 Developer Guide

### **Accessing Icons in Code**

**C# (ViewModel):**
```csharp
// Auto-detect icon
var iconManager = new IconManager(iconsDirectory);
var icon = iconManager.FindIconForCredential(credential);

// Generate search query for Flaticon
var query = iconManager.GenerateSearchQuery(credential);

// Get suggested filename for download
var filename = iconManager.GetSuggestedIconFilename(credential);
```

**XAML:**
```xml
<!-- Icon from Assets -->
<Image Source="/Assets/Icons/Logos/netflix.png" Width="32" Height="32"/>

<!-- Icon from ViewModel -->
<Image Source="{Binding IconPath}" Width="32" Height="32"/>

<!-- Emoji fallback -->
<TextBlock Text="{Binding DisplayIcon}" FontSize="24"/>
```

### **IconManager Methods**

| Method | Purpose | Returns |
|--------|---------|---------|
| `FindIconForCredential(credential)` | Auto-detect icon from filename | String (path or emoji) |
| `GenerateSearchQuery(credential)` | Create Flaticon search | String query |
| `GetSuggestedIconFilename(credential)` | Suggest download name | String filename |
| `GetIconEmoji(serviceName)` | Get emoji fallback | String emoji |

---

## 📊 Asset Statistics

| Category | Count | Formats | Status |
|----------|-------|---------|--------|
| Application Icons | 4 | .ico | ✅ Complete |
| UI Icons (Named) | ~50 | .png | ✅ Organized |
| UI Icons (Numbered) | ~650 | .png, .svg | ⚠️ Needs renaming |
| Brand Logos | ~600 | .png, .svg, .jpg | ✅ Comprehensive |
| Circular Logos | ~100 | .png | ✅ Available |
| **Total** | **~1,400+** | Mixed | 🔄 In progress |

---

## ✅ Completed Improvements

- ✅ Organized folder structure with categories
- ✅ Named all UI icons for easy reference
- ✅ Comprehensive brand logo library (600+ companies)
- ✅ Auto-icon matching system implemented
- ✅ Flaticon integration for downloading new icons
- ✅ Both PNG and SVG format support
- ✅ Indonesian market coverage (banks, e-wallets)

---

## 🔄 Planned Improvements

- [ ] Rename numbered Flaticon files to brand names
- [ ] Add missing tech services (GitHub, GitLab, AWS, Azure, DigitalOcean, Heroku)
- [ ] Add cloud storage logos (Dropbox, OneDrive, iCloud, Google Drive)
- [ ] Consolidate duplicate logos between root and Circle folder
- [ ] Create icon preview gallery tool
- [ ] Add dark mode variants for logos
- [ ] Generate thumbnails for faster loading

---

## 🆘 Troubleshooting

### **Icon Not Found**
**Problem:** Credential shows emoji instead of logo  
**Solution:** 
1. Check if logo exists in `Assets/Icons/Logos/`
2. Verify filename matches domain (e.g., `netflix.png` for `netflix.com`)
3. Use Icon Downloader to search and download from Flaticon

### **Icon Too Large/Small**
**Problem:** Icon appears pixelated or oversized  
**Solution:**
1. Preferred size: 256x256px for logos, 48x48px for UI icons
2. Use SVG format for scalable logos
3. Use image editing tool to resize PNG files

### **Download Failed**
**Problem:** Icon Downloader shows error  
**Solution:**
1. Check Flaticon API key in settings
2. Verify internet connection (HTTPS only)
3. Ensure filename doesn't contain special characters
4. Check file size < 1MB limit

---

## 📝 Naming Best Practices

### **DO:**
- ✅ Use lowercase: `netflix.png`
- ✅ Use hyphens for spaces: `visual-studio.png`
- ✅ Match primary domain: `instagram.png` not `ig.png`
- ✅ Keep names short: `github.png` not `github-repository-service.png`

### **DON'T:**
- ❌ Use spaces: `My Bank.png`
- ❌ Use special chars: `Bank@123.png`
- ❌ Use version numbers: `netflix-v2.png`
- ❌ Include file extension in name: `google.png.png`

---

## 🔗 Resources

- **Flaticon:** https://www.flaticon.com/ (Icon source)
- **Icon Format Guide:** PNG for raster, SVG for vector
- **Image Optimization:** Use tools like TinyPNG or ImageOptim
- **Icon Design:** Maintain consistent style and size

---

## 📄 License

Icons and logos are property of their respective trademark holders. Use in PhantomVault password manager is for identification purposes only under fair use. Do not redistribute icon library separately.

---

**For questions or suggestions, refer to project documentation.**
