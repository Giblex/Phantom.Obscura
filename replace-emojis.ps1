# PowerShell script to replace emojis with SVG icon references
# Run from project root

$projectRoot = "G:\Users\Giblex\Build Projects\PhantomObscuraV6\src\UI.Desktop"

# Emoji to SVG mapping for ViewModels (status messages)
$statusEmojiMap = @{
    '✓ ' = ''  # Remove checkmark, rely on StatusType
    '✔️ ' = ''
    '⚠️ ' = ''  # Remove warning, rely on StatusType
    '⚠ ' = ''
    '❌ ' = ''  # Remove X, rely on StatusType
    'ℹ️ ' = ''  # Remove info, rely on StatusType
    'ℹ ' = ''
}

# Files to process
$viewModelFiles = Get-ChildItem -Path "$projectRoot\ViewModels" -Filter "*.cs" -Recurse
$dialogServiceFile = "$projectRoot\Services\DialogService.cs"

Write-Host "Found $($viewModelFiles.Count) ViewModel files to process" -ForegroundColor Cyan

# Process each ViewModel file
foreach ($file in $viewModelFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content

    # Replace status message emojis
    foreach ($emoji in $statusEmojiMap.Keys) {
        $replacement = $statusEmojiMap[$emoji]
        $content = $content -replace [regex]::Escape($emoji), $replacement
    }

    # Only write if changed
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Updated: $($file.Name)" -ForegroundColor Green
    }
}

# Process DialogService.cs
if (Test-Path $dialogServiceFile) {
    $content = Get-Content $dialogServiceFile -Raw
    $originalContent = $content

    # Remove emoji prefixes from dialog titles
    $content = $content -replace 'Text = "ℹ️ " \+ title', 'Text = title'
    $content = $content -replace 'Text = "⚠️ " \+ title', 'Text = title'
    $content = $content -replace 'Text = "❌ " \+ \(title \?\? "Error"\)', 'Text = title ?? "Error"'

    if ($content -ne $originalContent) {
        Set-Content -Path $dialogServiceFile -Value $content -NoNewline
        Write-Host "Updated: DialogService.cs" -ForegroundColor Green
    }
}

Write-Host "`nEmoji replacement complete!" -ForegroundColor Cyan
Write-Host "Note: AXAML files require manual review for icon replacements" -ForegroundColor Yellow
