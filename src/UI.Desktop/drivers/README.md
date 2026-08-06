# Bundled drivers

Drop the official **WinFsp** installer here so Phantom Obscura can install it
automatically during the Setup Wizard (Storage step → "Install driver"), without
the user needing to download anything.

## What to add

- `winfsp-<version>.msi` — download from the official source: https://winfsp.dev/rel/

Place the `.msi` directly in this folder, e.g.:

```
src/UI.Desktop/drivers/winfsp-2.0.23075.msi
```

## How it is used

- The build copies everything in this folder to the app output under `drivers\`.
- At runtime `FindBundledWinFspInstaller()` looks for `winfsp*.msi` in the app
  base directory, `drivers\`, and `Assets\`.
- When found, the Storage step runs it **elevated and passive**
  (`msiexec /i <msi> /passive /norestart` with the `runas` verb) — a single UAC
  prompt, no MSI wizard — then re-probes until WinFsp reports as installed.
- When NOT found, the app falls back to opening https://winfsp.dev/rel/ in the
  browser so the user can install it manually.

> The WinFsp MSI is third-party software under its own license and is intentionally
> not committed to this repository. Add it here as part of your packaging/release
> step.
