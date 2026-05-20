<#
.SYNOPSIS
    Registers PhantomVault as a native messaging host for Firefox, Chrome, Edge, and Opera.

.DESCRIPTION
    Writes the native host manifest JSON files and the corresponding Windows registry keys
    for all four supported browsers. Also writes the allowed-origins list that
    PhantomVault.UI.exe --native-messaging reads to validate incoming extension connections.

.PARAMETER ExePath
    Full path to PhantomVault.UI.exe (e.g. "C:\Program Files\PhantomVault\PhantomVault.UI.exe").
    Defaults to the executable in the same directory as this script.

.PARAMETER ChromeExtensionId
    The unpacked / published Chrome extension ID (e.g. "abcdefghijklmnopqrstuvwxyz123456").
    Required for Chrome, Edge, and Opera registration. If omitted those browsers are skipped.

.EXAMPLE
    .\register-native-host.ps1 -ExePath "C:\Program Files\PhantomVault\PhantomVault.UI.exe" `
        -ChromeExtensionId "abcdefghijklmnopqrstuvwxyz123456"
#>

[CmdletBinding()]
param(
    [string] $ExePath = (Join-Path $PSScriptRoot '..\src\UI.Desktop\bin\Release\net9.0-windows10.0.19041.0\PhantomVault.UI.exe'),
    [string] $ChromeExtensionId = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve and validate exe path ────────────────────────────────────────────

$ExePath = (Resolve-Path $ExePath -ErrorAction SilentlyContinue)?.Path
if (-not $ExePath -or -not (Test-Path $ExePath)) {
    Write-Error "PhantomVault.UI.exe not found at: $ExePath`nPass -ExePath with the correct path."
    exit 1
}

$ExePathEscaped = $ExePath -replace '\\', '\\\\'

Write-Host "`n=== PhantomVault Native Host Registration ===" -ForegroundColor Cyan
Write-Host "Exe : $ExePath"

# ── Output directories ────────────────────────────────────────────────────────

$AppData          = $env:APPDATA
$MozHostDir       = Join-Path $AppData 'Mozilla\NativeMessagingHosts'
$ChromeHostDir    = Join-Path $AppData 'PhantomVault\NativeMessagingHosts\Chrome'
$EdgeHostDir      = Join-Path $AppData 'PhantomVault\NativeMessagingHosts\Edge'
$OperaHostDir     = Join-Path $AppData 'PhantomVault\NativeMessagingHosts\Opera'
$OriginsFile      = Join-Path $AppData 'PhantomVault\autofill-origins.json'

foreach ($dir in @($MozHostDir, $ChromeHostDir, $EdgeHostDir, $OperaHostDir,
                   (Split-Path $OriginsFile))) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

# ── Firefox ──────────────────────────────────────────────────────────────────

$firefoxManifest = @{
    name                = 'com.phantomvault.autofill'
    description         = 'PhantomVault Autofill Native Messaging Host'
    path                = $ExePath
    args                = @('--native-messaging')
    type                = 'stdio'
    allowed_extensions  = @('phantomvault@giblex.com')
} | ConvertTo-Json -Depth 5

$firefoxManifestPath = Join-Path $MozHostDir 'com.phantomvault.autofill.json'
Set-Content -Path $firefoxManifestPath -Value $firefoxManifest -Encoding UTF8

$firefoxRegKey = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.phantomvault.autofill'
New-Item -Path $firefoxRegKey -Force | Out-Null
Set-ItemProperty -Path $firefoxRegKey -Name '(Default)' -Value $firefoxManifestPath

Write-Host "`n[Firefox]  OK — $firefoxManifestPath" -ForegroundColor Green
Write-Host "           Registry: $firefoxRegKey"

# ── Chromium-based (Chrome / Edge / Opera) ───────────────────────────────────

if ([string]::IsNullOrWhiteSpace($ChromeExtensionId)) {
    Write-Host "`n[Chrome / Edge / Opera]  Skipped — provide -ChromeExtensionId to register." -ForegroundColor Yellow
} else {
    $chromiumOrigin = "chrome-extension://$ChromeExtensionId/"

    $chromiumManifest = @{
        name             = 'com.phantomvault.autofill'
        description      = 'PhantomVault Autofill Native Messaging Host'
        path             = $ExePath
        args             = @('--native-messaging')
        type             = 'stdio'
        allowed_origins  = @($chromiumOrigin)
    } | ConvertTo-Json -Depth 5

    # Chrome
    $chromeManifestPath = Join-Path $ChromeHostDir 'com.phantomvault.autofill.json'
    Set-Content -Path $chromeManifestPath -Value $chromiumManifest -Encoding UTF8
    $chromeRegKey = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.phantomvault.autofill'
    New-Item -Path $chromeRegKey -Force | Out-Null
    Set-ItemProperty -Path $chromeRegKey -Name '(Default)' -Value $chromeManifestPath
    Write-Host "`n[Chrome]   OK — $chromeManifestPath" -ForegroundColor Green

    # Edge
    $edgeManifestPath = Join-Path $EdgeHostDir 'com.phantomvault.autofill.json'
    Set-Content -Path $edgeManifestPath -Value $chromiumManifest -Encoding UTF8
    $edgeRegKey = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.phantomvault.autofill'
    New-Item -Path $edgeRegKey -Force | Out-Null
    Set-ItemProperty -Path $edgeRegKey -Name '(Default)' -Value $edgeManifestPath
    Write-Host "[Edge]     OK — $edgeManifestPath" -ForegroundColor Green

    # Opera (stable and beta registry paths)
    $operaManifestPath = Join-Path $OperaHostDir 'com.phantomvault.autofill.json'
    Set-Content -Path $operaManifestPath -Value $chromiumManifest -Encoding UTF8
    foreach ($operaKey in @(
        'HKCU:\Software\Opera Software\Opera Stable\NativeMessagingHosts\com.phantomvault.autofill',
        'HKCU:\Software\Opera Software\Opera\NativeMessagingHosts\com.phantomvault.autofill'
    )) {
        New-Item -Path $operaKey -Force | Out-Null
        Set-ItemProperty -Path $operaKey -Name '(Default)' -Value $operaManifestPath
    }
    Write-Host "[Opera]    OK — $operaManifestPath" -ForegroundColor Green

    # ── Allowed origins file (read by NativeMessagingMode.cs) ────────────────
    $origins = @{
        origins = @(
            "moz-extension://phantomvault@giblex.com/",
            $chromiumOrigin
        )
    } | ConvertTo-Json -Depth 3
    Set-Content -Path $OriginsFile -Value $origins -Encoding UTF8
    Write-Host "`n[Origins]  Written to $OriginsFile" -ForegroundColor Green
}

# Firefox-only origins (if Chrome was skipped)
if ([string]::IsNullOrWhiteSpace($ChromeExtensionId)) {
    $origins = @{ origins = @("moz-extension://phantomvault@giblex.com/") } | ConvertTo-Json -Depth 3
    Set-Content -Path $OriginsFile -Value $origins -Encoding UTF8
    Write-Host "`n[Origins]  Written to $OriginsFile (Firefox only)" -ForegroundColor Green
}

Write-Host "`n=== Registration complete ===" -ForegroundColor Cyan
Write-Host "Restart your browser(s) for the changes to take effect.`n"
