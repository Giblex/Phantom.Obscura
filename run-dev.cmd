@echo off
REM ===================================================================
REM Phantom Obscura V6 - Developer Mode Launcher
REM ===================================================================
REM This script bypasses USB policy enforcement for development/testing
REM Set environment variable to enable policy bypass
set PHANTOM_DEV_BYPASS_POLICY=1
set MSBUILDDISABLENODEREUSE=1
set UseSharedCompilation=false

echo Starting Phantom Obscura V6 in developer mode...
echo USB policy enforcement: BYPASSED
echo.

dotnet run --project src\UI.Desktop\PhantomVault.UI.csproj /p:UseSharedCompilation=false
