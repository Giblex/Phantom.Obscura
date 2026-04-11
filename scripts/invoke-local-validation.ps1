[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipUiTests,
    [switch]$SkipCoreTests,
    [switch]$NoReset,
    [switch]$RunFullCoreTests,
    [int]$CoreTestTimeoutSeconds = 180,
    [int]$UiTestTimeoutSeconds = 180,
    [string]$ResultsDirectory,
    [string]$TestLogger
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$coreProject = Join-Path $repoRoot "src\Core\PhantomVault.Core.csproj"
$uiProject = Join-Path $repoRoot "src\UI.Desktop\PhantomVault.UI.csproj"
$coreTests = Join-Path $repoRoot "tests\PhantomVault.Core.Tests\PhantomVault.Core.Tests.csproj"
$uiTests = Join-Path $repoRoot "tests\PhantomVault.UI.Tests\PhantomVault.UI.Tests.csproj"
$resetScript = Join-Path $PSScriptRoot "reset-build-locks.ps1"
$defaultCoreFilter = @(
    "FullyQualifiedName~ManifestServiceTests",
    "FullyQualifiedName~IntrusionServiceIntegrationTests",
    "FullyQualifiedName~VaultLifecycleIntegrationTests",
    "FullyQualifiedName~PhantomContainerServiceTests",
    "FullyQualifiedName~FeatureAvailabilityServiceTests",
    "FullyQualifiedName~FeatureAvailabilityServiceWindowsHelloTests",
    "FullyQualifiedName~PasskeyServiceTests",
    "FullyQualifiedName~RecoveryCodeServiceTests",
    "FullyQualifiedName~PolicyEngineTests"
) -join "|"

if (-not $NoReset) {
    & $resetScript
}

$env:MSBUILDDISABLENODEREUSE = "1"
$env:UseSharedCompilation = "false"

function Format-Argument {
    param([string]$Value)

    if ($Value -match '\s') {
        return '"' + $Value.Replace('"', '\"') + '"'
    }

    return $Value
}

function Invoke-DotnetStep {
    param(
        [string]$Label,
        [string[]]$Arguments,
        [int]$TimeoutSeconds = 0
    )

    $stdout = [System.IO.Path]::GetTempFileName()
    $stderr = [System.IO.Path]::GetTempFileName()
    $argumentString = ($Arguments | ForEach-Object { Format-Argument $_ }) -join " "

    try {
        Write-Host $Label -ForegroundColor Cyan

        $process = Start-Process dotnet `
            -ArgumentList $argumentString `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr `
            -PassThru

        if ($TimeoutSeconds -gt 0) {
            $completed = $process.WaitForExit($TimeoutSeconds * 1000)
            if (-not $completed) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                if (Test-Path $stdout) { Get-Content $stdout }
                if ((Test-Path $stderr) -and (Get-Item $stderr).Length -gt 0) { Get-Content $stderr }
                throw ($Label + " timed out after " + $TimeoutSeconds + " seconds.")
            }
        }
        else {
            $process.WaitForExit()
        }

        if (Test-Path $stdout) { Get-Content $stdout }
        if ((Test-Path $stderr) -and (Get-Item $stderr).Length -gt 0) { Get-Content $stderr }

        if ($process.ExitCode -ne 0) {
            throw ($Label + " failed with exit code " + $process.ExitCode + ".")
        }
    }
    finally {
        Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}

function Add-TestOutputArguments {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [string]$ProjectName
    )

    if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $projectResultsDirectory = Join-Path $ResultsDirectory $ProjectName
        New-Item -ItemType Directory -Path $projectResultsDirectory -Force | Out-Null
        $Arguments.Add("--results-directory")
        $Arguments.Add($projectResultsDirectory)
    }

    if (-not [string]::IsNullOrWhiteSpace($TestLogger)) {
        $Arguments.Add("--logger")
        $Arguments.Add($TestLogger)
    }
}

Push-Location $repoRoot
try {
    $commonArgs = @("-c", $Configuration, "--no-restore", "/p:UseSharedCompilation=false")

    Invoke-DotnetStep "Building core..." (@("build", $coreProject) + $commonArgs)
    Invoke-DotnetStep "Building desktop UI..." (@("build", $uiProject) + $commonArgs)
    Invoke-DotnetStep "Building core tests..." (@("build", $coreTests) + $commonArgs)

    if (-not $SkipCoreTests) {
        $coreTestArgs = [System.Collections.Generic.List[string]]::new()
        $coreTestArgs.AddRange([string[]]@("test", $coreTests, "-c", $Configuration, "--no-build", "/p:UseSharedCompilation=false"))
        if (-not $RunFullCoreTests) {
            $coreTestArgs.Add("--filter")
            $coreTestArgs.Add($defaultCoreFilter)
        }

        Add-TestOutputArguments $coreTestArgs "PhantomVault.Core.Tests"
        Invoke-DotnetStep "Running core tests..." $coreTestArgs $CoreTestTimeoutSeconds
    }

    Invoke-DotnetStep "Building UI tests..." (@("build", $uiTests) + $commonArgs)

    if (-not $SkipUiTests) {
        $uiTestArgs = [System.Collections.Generic.List[string]]::new()
        $uiTestArgs.AddRange([string[]]@("test", $uiTests, "-c", $Configuration, "--no-build", "/p:UseSharedCompilation=false"))
        Add-TestOutputArguments $uiTestArgs "PhantomVault.UI.Tests"
        Invoke-DotnetStep "Running UI tests..." $uiTestArgs $UiTestTimeoutSeconds
    }

    Write-Host "Local validation completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
