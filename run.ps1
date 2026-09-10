# ===================================================================
# Phantom Obscura - Standard Launcher (PowerShell)
# ===================================================================
# Launches the desktop app with full policy enforcement (no bypass).

Write-Host "Starting Phantom Obscura..." -ForegroundColor Cyan
Write-Host ""

$env:MSBUILDDISABLENODEREUSE = "1"
$env:UseSharedCompilation = "false"
Remove-Item Env:\PHANTOM_DEV_BYPASS_POLICY -ErrorAction SilentlyContinue

# Release builds refuse to start when debug symbols sit beside them
# (BuildIntegrityVerifier.EnforceProductionBuild), and "dotnet run -c Release" emits .pdb files
# by default, so this launcher used to exit immediately. Build without symbols, and clear any
# .pdb left in the Release output by an earlier symbol-producing build.
Get-ChildItem "src\UI.Desktop\bin\Release" -Recurse -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet run --project "src\UI.Desktop\PhantomVault.UI.csproj" -c Release /p:UseSharedCompilation=false /p:DebugType=none /p:DebugSymbols=false
