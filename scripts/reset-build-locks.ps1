[CmdletBinding()]
param(
    [switch]$StopRunningApp
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$escapedRepoRoot = [Regex]::Escape($repoRoot)

$processes = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @("dotnet.exe", "testhost.exe", "testhost.net9.0.exe")
}

$stopped = [System.Collections.Generic.List[string]]::new()

foreach ($process in $processes) {
    $commandLine = $process.CommandLine ?? ""
    $shouldStop = $false

    if ($process.Name -like "testhost*") {
        $shouldStop = $true
    }
    elseif ($commandLine -match "vstest\.console\.dll") {
        $shouldStop = $true
    }
    elseif ($commandLine -match "MSBuild\.dll" -and $commandLine -match "/nodemode:1") {
        $shouldStop = $true
    }
    elseif ($StopRunningApp -and $commandLine -match $escapedRepoRoot) {
        $shouldStop = $true
    }

    if (-not $shouldStop) {
        continue
    }

    try {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
        $stopped.Add($process.Name + " (" + $process.ProcessId + ")")
    }
    catch {
        $message = "Failed to stop " + $process.Name + " (" + $process.ProcessId + "): " + $_.Exception.Message
        Write-Warning $message
    }
}

$env:MSBUILDDISABLENODEREUSE = "1"
$env:UseSharedCompilation = "false"

if ($stopped.Count -eq 0) {
    Write-Host "No stale build or test host processes were found." -ForegroundColor Green
}
else {
    Write-Host ("Stopped stale processes: " + ($stopped -join ", ")) -ForegroundColor Yellow
}

Write-Host "Build-session defaults set for this shell:" -ForegroundColor Cyan
Write-Host "  MSBUILDDISABLENODEREUSE=1" -ForegroundColor Gray
Write-Host "  UseSharedCompilation=false" -ForegroundColor Gray
