# Stops processes that reference PhantomVault in their command line to release file locks.
# Safe: only stops processes where CommandLine contains 'PhantomVault' (case-insensitive).

$myPid = $PID
$matches = Get-CimInstance Win32_Process | Where-Object {
    ($_.CommandLine -ne $null) -and ($_.CommandLine -match 'PhantomVault') -and ($_.ProcessId -ne $myPid)
}

if (-not $matches -or $matches.Count -eq 0) {
    Write-Output "No matching processes found."
    exit 0
}

Write-Output "Found the following processes referencing PhantomVault:"
$matches | Select-Object ProcessId, Name, CommandLine | Format-Table -AutoSize

foreach ($p in $matches) {
    try {
        Write-Output "Stopping PID $($p.ProcessId) ($($p.Name))..."
        Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop
        Write-Output "Stopped PID $($p.ProcessId)."
    }
    catch {
        Write-Output "Failed to stop PID $($p.ProcessId): $($_.Exception.Message)"
    }
}

# Small pause to let OS release handles
Start-Sleep -Milliseconds 300

# Report remaining locks that might still exist (show file handles is not available here). Done.
Write-Output "Done."
