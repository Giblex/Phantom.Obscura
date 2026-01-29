`param(
    [string]$Message = 'Authorize theme change'
)

$allowFile = '.allow-theme-changes'
if (Test-Path $allowFile) {
    Write-Host "$allowFile already exists." -ForegroundColor Yellow
    exit 0
}

New-Item -Path $allowFile -ItemType File -Force | Out-Null
git add $allowFile
git commit -m "$Message"
Write-Host "Created and committed $allowFile. You can now modify the theme file." -ForegroundColor Green
