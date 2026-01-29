# Archive Unused Files and Folders
# Moves all unnecessary, old, duplicate, or unreferenced files to the archive folder

$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$archivePath = "g:\Users\Giblex\Builds\PhantomObscuraV5\archive_$timestamp"

Write-Host "=== PhantomVault Cleanup - Archive Unused Files ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Archive destination: $archivePath" -ForegroundColor Yellow
Write-Host ""

# Create archive directory
New-Item -ItemType Directory -Path $archivePath -Force | Out-Null

# Files and folders to archive (with reasons)
$itemsToArchive = @(
    # OLD/PREVIOUS VERSIONS
    @{
        Path = "archive_20251017_221245"
        Reason = "Previous archive - old patched WPF version (not compatible with current Avalonia app)"
    },
    
    # DUPLICATE SOLUTION FILES
    @{
        Path = "GiblexVault-Improved.sln"
        Reason = "Duplicate solution file - includes MAUI project that's not used in main app. PhantomVault.sln is the active solution."
    },
    
    # UNUSED MAUI PROJECT
    @{
        Path = "src\GiblexVault.Maui"
        Reason = "MAUI mobile project - not referenced in active PhantomVault.sln, cross-platform prototype only"
    },
    
    # TEMPORARY/DEBUG FOLDERS
    @{
        Path = "temp-aes-check"
        Reason = "Temporary AES encryption test project - debugging tool, not part of main app"
    },
    @{
        Path = "tools"
        Reason = "Diagnostic tools (diag-derivekey, EncryptionDemo) - development utilities not needed in production"
    },
    @{
        Path = "tests\tests"
        Reason = "Nested empty tests folder - only contains diagnostics subfolder, not referenced in solution"
    },
    
    # DUPLICATE TEST PROJECT
    @{
        Path = "src\PhantomVault.Core.Tests"
        Reason = "Duplicate test project - actual tests are in tests\PhantomVault.Core.Tests with more comprehensive tests"
    },
    
    # ONE-TIME UTILITY SCRIPTS
    @{
        Path = "add-missing-logos.ps1"
        Reason = "One-time logo download utility script - task already completed"
    },
    @{
        Path = "consolidate-logos.ps1"
        Reason = "One-time logo consolidation script - task already completed"
    },
    @{
        Path = "organize-assets.ps1"
        Reason = "One-time asset organization script - task already completed"
    },
    @{
        Path = "standardize-buttons.ps1"
        Reason = "One-time button standardization script - task already completed"
    },
    @{
        Path = "fix-scrolling-and-buttons.ps1"
        Reason = "One-time UI fix script - changes already applied"
    },
    @{
        Path = "update-theme.ps1"
        Reason = "One-time theme update script - task already completed"
    },
    
    # DOCUMENTATION FILES (keeping relevant ones)
    @{
        Path = "PATCHED_VERSION_ANALYSIS.md"
        Reason = "Analysis of old WPF patched version - historical document, patched version already archived"
    },
    @{
        Path = "PR_SUMMARY.md"
        Reason = "Pull request summary - historical document for AesGcm migration completed"
    },
    @{
        Path = "THEME_LOCK.md"
        Reason = "Theme lock policy for old WPF version - not applicable to current Avalonia app"
    },
    
    # TEMPORARY FILES
    @{
        Path = "tmp_before_patch.txt"
        Reason = "Temporary diagnostic file - no longer needed"
    },
    @{
        Path = "tmp_after_patch.txt"
        Reason = "Temporary diagnostic file - no longer needed"
    },
    
    # NPM/NODE FILES (Not used in .NET app)
    @{
        Path = "node_modules"
        Reason = "Node.js dependencies - not used in .NET/Avalonia application"
    },
    @{
        Path = "package.json"
        Reason = "NPM package file with React drag-drop libraries - not used in .NET/Avalonia app"
    },
    @{
        Path = "package-lock.json"
        Reason = "NPM lock file - not used in .NET/Avalonia application"
    }
)

Write-Host "Items to archive:" -ForegroundColor Green
Write-Host ""

foreach ($item in $itemsToArchive) {
    $fullPath = Join-Path "g:\Users\Giblex\Builds\PhantomObscuraV5" $item.Path
    
    if (Test-Path $fullPath) {
        Write-Host "  ✓ $($item.Path)" -ForegroundColor White
        Write-Host "    Reason: $($item.Reason)" -ForegroundColor DarkGray
        Write-Host ""
    } else {
        Write-Host "  ⊗ $($item.Path) (not found, skipping)" -ForegroundColor DarkYellow
        Write-Host ""
    }
}

Write-Host ""
Write-Host "Press any key to continue with archiving, or Ctrl+C to cancel..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
Write-Host ""

# Archive each item
$movedCount = 0
$skippedCount = 0

foreach ($item in $itemsToArchive) {
    $fullPath = Join-Path "g:\Users\Giblex\Builds\PhantomObscuraV5" $item.Path
    
    if (Test-Path $fullPath) {
        try {
            $destPath = Join-Path $archivePath $item.Path
            $destDir = Split-Path $destPath -Parent
            
            # Create destination directory if needed
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            
            # Move item to archive
            Move-Item -Path $fullPath -Destination $destPath -Force
            Write-Host "  ✓ Archived: $($item.Path)" -ForegroundColor Green
            $movedCount++
        }
        catch {
            Write-Host "  ✗ Failed to archive $($item.Path): $($_.Exception.Message)" -ForegroundColor Red
            $skippedCount++
        }
    } else {
        $skippedCount++
    }
}

Write-Host ""
Write-Host "=== Archive Complete ===" -ForegroundColor Cyan
Write-Host "  Archived: $movedCount items" -ForegroundColor Green
Write-Host "  Skipped: $skippedCount items" -ForegroundColor Yellow
Write-Host "  Location: $archivePath" -ForegroundColor White
Write-Host ""

# Create archive manifest
$manifestPath = Join-Path $archivePath "ARCHIVE_MANIFEST.md"
$manifestContent = @"
# Archive Manifest
**Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Reason:** Cleanup of unused, duplicate, and temporary files from PhantomVault project

## Archived Items

"@

foreach ($item in $itemsToArchive) {
    $manifestContent += @"

### $($item.Path)
**Reason:** $($item.Reason)

"@
}

$manifestContent += @"

## Active Project Structure (After Cleanup)

```
PhantomObscuraV5/
├── PhantomVault.sln          ← Active solution file
├── README.md                  ← Project documentation
├── .github/                   ← GitHub workflows and instructions
├── .githooks/                 ← Git hooks
├── .vscode/                   ← VS Code settings
├── BrowserExtension/          ← Browser extension for auto-fill (Chrome/Firefox)
├── Docs/                      ← Implementation guides and documentation
├── scripts/                   ← Active automation scripts
├── src/
│   ├── PhantomVault.Core/    ← Core library (encryption, models, services)
│   ├── PhantomVault.UI/      ← Avalonia desktop UI (active app)
│   └── GVZK_tmp/             ← Zero-knowledge security module
└── tests/
    └── PhantomVault.Core.Tests/  ← Unit tests (xUnit)
```

## Notes

- **Main application:** PhantomVault.UI (Avalonia-based, cross-platform)
- **Browser integration:** BrowserExtension folder contains Chrome/Firefox extensions and native host
- **Zero-knowledge encryption:** GVZK_tmp module provides advanced cryptographic features
- **Testing:** tests/PhantomVault.Core.Tests contains comprehensive unit tests

All archived items were not referenced in the active solution or were temporary/completed utilities.
"@

Set-Content -Path $manifestPath -Value $manifestContent -Encoding UTF8

Write-Host "Archive manifest created: ARCHIVE_MANIFEST.md" -ForegroundColor Cyan
Write-Host ""
Write-Host "Project cleanup complete! ✓" -ForegroundColor Green
