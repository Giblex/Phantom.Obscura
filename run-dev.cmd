@echo off
REM ===================================================================
REM Phantom Obscura V6 - Developer Mode Launcher
REM ===================================================================
REM Bypasses USB policy enforcement for development/testing.
REM The bypass is honoured in Debug builds only; Release builds refuse
REM to start when PHANTOM_DEV_BYPASS_POLICY is set. Use run.cmd for a
REM normal (fully enforced) launch.
set PHANTOM_DEV_BYPASS_POLICY=1
set MSBUILDDISABLENODEREUSE=1
set UseSharedCompilation=false

echo Starting Phantom Obscura V6 in developer mode...
echo USB policy enforcement: BYPASSED
echo.

dotnet run --project src\UI.Desktop\PhantomVault.UI.csproj /p:UseSharedCompilation=false
