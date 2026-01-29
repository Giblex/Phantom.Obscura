param()

# Path to the theme file to protect (relative to repo root)
$themePath = 'PhantomObscura_v5_Patched/GiblexTheme.xaml'

# Sentinel file that must exist to allow changes
$allowFile = '.allow-theme-changes'

Write-Verbose "Checking staged changes for protected theme file: $themePath"

# Get list of staged files
try {
    $staged = git diff --cached --name-only
} catch {
    Write-Error "Failed to run git. Ensure this hook runs in a git repository. $_"
    exit 0
}

if (-not $staged) {
    exit 0
}

$modifiedTheme = $staged -contains $themePath

if ($modifiedTheme) {
    if (Test-Path $allowFile) {
        Write-Host "Theme file changes authorized by presence of $allowFile. Proceeding." -ForegroundColor Yellow
        exit 0
    }

    Write-Host "ERROR: Changes include the locked dark theme file: $themePath" -ForegroundColor Red
    Write-Host "The dark theme is locked. To authorize a change (placement-only), create an empty file named '$allowFile' at the repository root, commit, then remove the file." -ForegroundColor Cyan
    Write-Host "Example: New-Item -Path $allowFile -ItemType File; git add $allowFile; git commit -m \"Authorize theme change\"; <make change>; git commit -am \"Theme placement change\"; Remove-Item $allowFile" -ForegroundColor Gray
    exit 1
}

exit 0
