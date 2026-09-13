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

REM Release builds refuse to start when debug symbols sit beside them
REM (BuildIntegrityVerifier.EnforceProductionBuild), and "dotnet run -c Release" emits .pdb
REM files by default. Build without symbols and clear any .pdb left by an earlier build.
if exist src\UI.Desktop\bin\Release del /s /q src\UI.Desktop\bin\Release\*.pdb >nul 2>&1

dotnet run --project src\UI.Desktop\PhantomVault.UI.csproj -c Release /p:UseSharedCompilation=false /p:DebugType=none /p:DebugSymbols=false
