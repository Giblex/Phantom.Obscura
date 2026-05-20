<#
.SYNOPSIS
    Removes PhantomVault native messaging host registration from all browsers.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Write-Host "`n=== PhantomVault Native Host Unregistration ===" -ForegroundColor Cyan

$regKeys = @(
    'HKCU:\Software\Mozilla\NativeMessagingHosts\com.phantomvault.autofill',
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.phantomvault.autofill',
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.phantomvault.autofill',
    'HKCU:\Software\Opera Software\Opera Stable\NativeMessagingHosts\com.phantomvault.autofill',
    'HKCU:\Software\Opera Software\Opera\NativeMessagingHosts\com.phantomvault.autofill'
)

foreach ($key in $regKeys) {
    if (Test-Path $key) {
        Remove-Item -Path $key -Force
        Write-Host "Removed: $key" -ForegroundColor Green
    }
}

$manifestFiles = @(
    Join-Path $env:APPDATA 'Mozilla\NativeMessagingHosts\com.phantomvault.autofill.json',
    Join-Path $env:APPDATA 'PhantomVault\NativeMessagingHosts\Chrome\com.phantomvault.autofill.json',
    Join-Path $env:APPDATA 'PhantomVault\NativeMessagingHosts\Edge\com.phantomvault.autofill.json',
    Join-Path $env:APPDATA 'PhantomVault\NativeMessagingHosts\Opera\com.phantomvault.autofill.json',
    Join-Path $env:APPDATA 'PhantomVault\autofill-origins.json'
)

foreach ($file in $manifestFiles) {
    if (Test-Path $file) {
        Remove-Item -Path $file -Force
        Write-Host "Removed: $file" -ForegroundColor Green
    }
}

Write-Host "`n=== Unregistration complete ===" -ForegroundColor Cyan
Write-Host "Restart your browser(s) for the changes to take effect.`n"
