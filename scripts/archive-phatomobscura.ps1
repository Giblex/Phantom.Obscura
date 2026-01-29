param(
    [string]$FolderToArchive = 'PhantomObscura_v5_Patched'
)

# Safety checks
if (-not (Test-Path -LiteralPath $FolderToArchive)) {
    Write-Error "Folder '$FolderToArchive' does not exist. Aborting."
    exit 1
}

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$archiveName = "archive_$ts"

Write-Output "Creating archive folder: $archiveName"
New-Item -ItemType Directory -Path $archiveName -Force | Out-Null

# Authorize theme change (creates and commits .allow-theme-changes)
$authScript = Join-Path -Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) -ChildPath 'authorize-theme-change.ps1'
if (-not (Test-Path $authScript)) {
    Write-Error "Authorize script not found at $authScript. Aborting."
    exit 1
}

Write-Output "Running authorize script to allow moving theme-containing folder..."
pwsh -NoProfile -File $authScript -Message "Temporary authorize theme move for archival $ts"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Authorize script failed. Aborting."
    exit 1
}

# Perform git mv to preserve history
$dest = Join-Path -Path $archiveName -ChildPath $FolderToArchive
Write-Output "Running git mv '$FolderToArchive' -> '$dest'"
git mv -- "$FolderToArchive" "$dest"
if ($LASTEXITCODE -ne 0) {
    Write-Error "git mv failed. Aborting."
    # Attempt to revoke sentinel before exiting
    pwsh -NoProfile -File (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) 'revoke-theme-change.ps1') -Message "Revoke after failed archive attempt"
    exit 1
}

# Commit the move
git commit -m "Archive: move $FolderToArchive to $archiveName"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Commit failed. You may need to commit manually."
    exit 1
}

# Revoke sentinel
$revokeScript = Join-Path -Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) -ChildPath 'revoke-theme-change.ps1'
if (Test-Path $revokeScript) {
    Write-Output "Revoking theme change authorization..."
    pwsh -NoProfile -File $revokeScript -Message "Revoke authorization after archival $ts"
}

Write-Output "Archive completed: $dest"
exit 0
