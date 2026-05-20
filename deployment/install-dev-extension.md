# Loading the PhantomVault Extension for Development

The extension lives in `src/Extension/`. No build step is required — load it directly.

## Step 1 — Build PhantomVault.UI.exe

```powershell
dotnet build src\UI.Desktop\PhantomVault.UI.csproj -c Release
```

The output exe will be at:
`src\UI.Desktop\bin\Release\net9.0-windows10.0.19041.0\PhantomVault.UI.exe`

## Step 2 — Register the native host

Run from the repo root:

```powershell
# Firefox only (no extension ID needed — uses the fixed gecko ID)
.\deployment\register-native-host.ps1 -ExePath ".\src\UI.Desktop\bin\Release\net9.0-windows10.0.19041.0\PhantomVault.UI.exe"

# Chrome / Edge / Opera — get the extension ID from Step 3 first, then re-run with it:
.\deployment\register-native-host.ps1 `
    -ExePath ".\src\UI.Desktop\bin\Release\net9.0-windows10.0.19041.0\PhantomVault.UI.exe" `
    -ChromeExtensionId "YOUR_EXTENSION_ID_HERE"
```

## Step 3 — Load the extension in your browser

### Firefox
1. Open `about:debugging`
2. Click **This Firefox** → **Load Temporary Add-on…**
3. Select `src\Extension\manifest.json`
4. The extension appears in the toolbar. The gecko ID is fixed: `phantomvault@giblex.com`.

### Chrome
1. Open `chrome://extensions`
2. Enable **Developer mode** (top-right toggle)
3. Click **Load unpacked** → select the `src\Extension\` folder
4. Note the **ID** shown under the extension card (e.g. `abcdef...`)
5. Re-run `register-native-host.ps1` with that ID (see Step 2)

### Edge
Same as Chrome but navigate to `edge://extensions`.

### Opera
Same as Chrome but navigate to `opera://extensions`.

## Step 4 — Start PhantomVault and unlock your vault

The pipe server starts automatically when PhantomVault launches. Unlock a vault so the
pipe server can serve credentials to the native messaging subprocess.

## Step 5 — Test

1. Open any login page (e.g. `https://example.com/login`)
2. A PhantomVault credential suggestion chip should appear near the password field
3. Click a credential to fill the form

### Checking the native host log

Logs are written to:
`%APPDATA%\PhantomVault\logs\nativehost-YYYYMMDD.log`

## Uninstall

```powershell
.\deployment\unregister-native-host.ps1
```

Then remove the temporary extension from each browser's extensions page.
