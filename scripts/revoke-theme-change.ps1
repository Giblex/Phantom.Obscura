param(
    [string]$Message = 'Revoke theme change authorization'
)

$allowFile = '.allow-theme-changes'
if (-not (Test-Path $allowFile)) {
    Write-Host "$allowFile does not exist." -ForegroundColor Yellow
    exit 0
}

git rm --cached $allowFile 2>$null
Remove-Item $allowFile -Force
git commit -m "$Message" 2>$null
Write-Host "Removed $allowFile and committed. Theme is locked again." -ForegroundColor Green
