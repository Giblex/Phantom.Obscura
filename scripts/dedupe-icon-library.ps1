<#
.SYNOPSIS
    Finds byte-identical duplicate files under the shipped icon asset trees
    (Assets/Visuals/Cat Icons and Assets/Visuals/Entry Logos) and, optionally,
    removes the redundant copies via `git rm`.

.DESCRIPTION
    IconLibraryIndex (src/Core/Services/Icons/IconLibraryIndex.cs) already de-duplicates
    these trees at runtime: one file per Cat Icons colour-variant folder, one file per
    logo name, and any remaining byte-identical files collapsed to a single copy. That
    logic only decides what the *picker* offers — it never removes anything from disk,
    so the repo still ships every colour variant and every duplicate logo file.

    This script is deliberately narrower and safer than reimplementing that whole
    algorithm: it only removes a file when
      1. its content (SHA-256) is byte-identical to another file that is kept, AND
      2. its exact file name is never referenced as a string literal anywhere in
         src/**/*.cs, src/**/*.axaml or src/**/*.csproj (hard-coded default icons like
         IconPathMigrator.cs and the Import-logos lookup in ImportViewModel.cs rely on
         exact file names — those must never be deleted even if a byte-identical twin
         exists elsewhere).

    "Entry Logos/Entry Logos/Import logos" is a small, fixed set of per-format icons
    looked up by exact file name (ImportViewModel) and is always skipped entirely.

    Run with no switches for a dry-run report. Pass -Apply to actually remove files
    (via `git rm`, so the change is a normal, revertable commit).

.PARAMETER Apply
    Actually delete the redundant files (via git rm). Without this switch the script
    only reports what it would do.

.PARAMETER RepoRoot
    Path to the repository root. Defaults to the parent of the scripts/ folder.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$visualsRoot = Join-Path $RepoRoot 'src\UI.Desktop\Assets\Visuals'
$targets = @(
    Join-Path $visualsRoot 'Cat Icons'
    Join-Path $visualsRoot 'Entry Logos'
)
$skipFolderMarker = [IO.Path]::DirectorySeparatorChar + 'Import logos' + [IO.Path]::DirectorySeparatorChar

Write-Host "Scanning:" -ForegroundColor Cyan
$targets | ForEach-Object { Write-Host "  $_" }

# --- Build the set of file names referenced literally anywhere in source. ---------------
Write-Host "`nIndexing source references (src/**/*.cs, *.axaml, *.csproj)..." -ForegroundColor Cyan
$sourceRoot = Join-Path $RepoRoot 'src'
$sourceFiles = Get-ChildItem -Path $sourceRoot -Recurse -File -Include *.cs, *.axaml, *.csproj -ErrorAction SilentlyContinue
$sourceText = ($sourceFiles | Get-Content -Raw -ErrorAction SilentlyContinue) -join "`n"

function Test-Referenced([string]$fileName) {
    # Literal match on the bare file name (e.g. "Chrome.png"), which is how every
    # hard-coded lookup in this codebase names an icon (avares URIs, IconPathMigrator
    # constants, etc). Cheap substring check; false positives only make us keep an
    # extra file, never delete a referenced one.
    return $sourceText.Contains($fileName)
}

# --- Enumerate candidate files, skipping the Import logos folder. -----------------------
$allFiles = foreach ($target in $targets) {
    if (-not (Test-Path $target)) { continue }
    Get-ChildItem -Path $target -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch [regex]::Escape($skipFolderMarker) }
}
$allFiles = @($allFiles)
Write-Host "Found $($allFiles.Count) candidate files.`n" -ForegroundColor Cyan

# --- Group by size first (cheap), then hash only real collisions. -----------------------
$bySize = $allFiles | Group-Object Length | Where-Object { $_.Count -gt 1 }

$toRemove = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$toKeep = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$reclaimedBytes = 0L
$referencedSaves = 0

foreach ($sizeGroup in $bySize) {
    $byHash = $sizeGroup.Group | Group-Object { (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash }
    foreach ($hashGroup in $byHash) {
        if ($hashGroup.Count -le 1) { continue }

        # Prefer to keep the shallowest / shortest path as the canonical survivor.
        $ordered = $hashGroup.Group | Sort-Object { $_.FullName.Length }
        $keeper = $ordered[0]
        $toKeep.Add($keeper)

        foreach ($dup in $ordered[1..($ordered.Count - 1)]) {
            if (Test-Referenced $dup.Name) {
                $toKeep.Add($dup)
                $referencedSaves++
            } else {
                $toRemove.Add($dup)
                $reclaimedBytes += $dup.Length
            }
        }
    }
}

Write-Host "Duplicate content groups found: $($bySize.Count)" -ForegroundColor Yellow
Write-Host "Files that would be removed:    $($toRemove.Count)" -ForegroundColor Yellow
Write-Host "Kept anyway (name referenced):  $referencedSaves" -ForegroundColor Yellow
Write-Host ("Disk space reclaimed:          {0:N1} MB" -f ($reclaimedBytes / 1MB)) -ForegroundColor Yellow

if (-not $Apply) {
    Write-Host "`nDry run only. Re-run with -Apply to remove these files via 'git rm'." -ForegroundColor Cyan
    $reportPath = Join-Path $RepoRoot 'scripts\dedupe-icon-library.report.txt'
    $toRemove | ForEach-Object { $_.FullName.Substring($RepoRoot.Length + 1) } | Set-Content -Path $reportPath
    Write-Host "Full removal list written to $reportPath"
    exit 0
}

if ($toRemove.Count -eq 0) {
    Write-Host "Nothing to remove." -ForegroundColor Green
    exit 0
}

Write-Host "`nRemoving $($toRemove.Count) files via git rm..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    $batchSize = 200
    for ($i = 0; $i -lt $toRemove.Count; $i += $batchSize) {
        $batch = $toRemove[$i..([Math]::Min($i + $batchSize, $toRemove.Count) - 1)] | ForEach-Object { $_.FullName }
        git rm -q --cached -- $batch 2>$null
        $batch | ForEach-Object { Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue }
    }
} finally {
    Pop-Location
}

Write-Host "Done. Review 'git status' before committing." -ForegroundColor Green
