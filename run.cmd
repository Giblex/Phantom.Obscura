@echo off
REM ===================================================================
REM Phantom Obscura - Standard Launcher
REM ===================================================================
REM Launches the desktop app with full policy enforcement (no bypass).
set PHANTOM_DEV_BYPASS_POLICY=
set MSBUILDDISABLENODEREUSE=1
set UseSharedCompilation=false

echo Starting Phantom Obscura...
echo.

dotnet run --project src\UI.Desktop\PhantomVault.UI.csproj -c Release /p:UseSharedCompilation=false
