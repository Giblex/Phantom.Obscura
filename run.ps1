# ===================================================================
# Phantom Obscura - Standard Launcher (PowerShell)
# ===================================================================
# Launches the desktop app with full policy enforcement (no bypass).

Write-Host "Starting Phantom Obscura..." -ForegroundColor Cyan
Write-Host ""

$env:MSBUILDDISABLENODEREUSE = "1"
$env:UseSharedCompilation = "false"
Remove-Item Env:\PHANTOM_DEV_BYPASS_POLICY -ErrorAction SilentlyContinue

dotnet run --project "src\UI.Desktop\PhantomVault.UI.csproj" -c Release /p:UseSharedCompilation=false
