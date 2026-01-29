# PowerShell script to remove emoji prefixes from status messages
# Run from project root

$projectRoot = "G:\Users\Giblex\Build Projects\PhantomObscuraV6\src\UI.Desktop"

Write-Host "Cleaning status message emojis..." -ForegroundColor Cyan

# Emoji patterns to remove from status messages
$emojiPatterns = @(
    '✓ ',
    '✔️ ',
    '⚠️ ',
    '⚠ ',
    '❌ ',
    'ℹ️ ',
    'ℹ '
)

# Files to process
$viewModelFiles = Get-ChildItem -Path "$projectRoot\ViewModels" -Filter "*.cs" -Recurse

$totalChanges = 0

foreach ($file in $viewModelFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    $fileChanges = 0

    foreach ($emoji in $emojiPatterns) {
        $escapedEmoji = [regex]::Escape($emoji)

        # Pattern: StatusMessage = "emoji text"
        $pattern1 = "StatusMessage = ""$escapedEmoji"
        $replacement1 = 'StatusMessage = "'

        # Count replacements
        $matches = [regex]::Matches($content, $pattern1)
        if ($matches.Count -gt 0) {
            $content = $content -replace $pattern1, $replacement1
            $fileChanges += $matches.Count
        }

        # Pattern: StatusMessage += "emoji text"
        $pattern2 = "StatusMessage \+= ""$escapedEmoji"
        $replacement2 = 'StatusMessage += "'

        $matches = [regex]::Matches($content, $pattern2)
        if ($matches.Count -gt 0) {
            $content = $content -replace $pattern2, $replacement2
            $fileChanges += $matches.Count
        }

        # Pattern: StatusMessage = $"emoji {interpolation}"
        $pattern3 = "StatusMessage = \`$""$escapedEmoji"
        $replacement3 = 'StatusMessage = $"'

        $matches = [regex]::Matches($content, $pattern3)
        if ($matches.Count -gt 0) {
            $content = $content -replace $pattern3, $replacement3
            $fileChanges += $matches.Count
        }

        # Pattern: StatusMessage += $"emoji {interpolation}"
        $pattern4 = "StatusMessage \+= \`$""$escapedEmoji"
        $replacement4 = 'StatusMessage += $"'

        $matches = [regex]::Matches($content, $pattern4)
        if ($matches.Count -gt 0) {
            $content = $content -replace $pattern4, $replacement4
            $fileChanges += $matches.Count
        }
    }

    # Only write if changed
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "  Updated: $($file.Name) ($fileChanges changes)" -ForegroundColor Green
        $totalChanges += $fileChanges
    }
}

Write-Host "`nTotal emojis removed: $totalChanges" -ForegroundColor Cyan
Write-Host "Status message cleanup complete!" -ForegroundColor Green
