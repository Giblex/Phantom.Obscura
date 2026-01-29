# ===================================================================
# Phantom Obscura V6 - Developer Mode Launcher (PowerShell)
# ===================================================================
# This script bypasses USB policy enforcement for development/testing

Write-Host "Starting Phantom Obscura V6 in developer mode..." -ForegroundColor Cyan
Write-Host "USB policy enforcement: BYPASSED" -ForegroundColor Yellow
Write-Host ""

# Set environment variable to enable policy bypass
$env:PHANTOM_DEV_BYPASS_POLICY = "1"

# Run the application
dotnet run --project src\UI.Desktop\PhantomVault.UI.csproj
