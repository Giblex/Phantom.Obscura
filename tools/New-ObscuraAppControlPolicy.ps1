[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishRoot,
    [Parameter(Mandatory)] [string] $OutputPolicy,
    [switch] $Enforce
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = (Resolve-Path -LiteralPath $PublishRoot).Path
if (-not (Get-Command New-CIPolicy -ErrorAction SilentlyContinue)) {
    throw 'Windows App Control policy cmdlets are not installed on this machine.'
}

$policy = [System.IO.Path]::GetFullPath($OutputPolicy)
$policyDirectory = [System.IO.Path]::GetDirectoryName($policy)
[System.IO.Directory]::CreateDirectory($policyDirectory) | Out-Null

# Publisher rules survive routine signed updates; hash fallback covers files whose
# publisher metadata cannot safely express the exact trust decision.
New-CIPolicy -ScanPath $resolvedRoot -Level Publisher -Fallback Hash -UserPEs -FilePath $policy
Set-CIPolicyIdInfo -FilePath $policy -PolicyName 'Phantom Obscura Signed Application Control' -ResetPolicyID

if ($Enforce) {
    Set-RuleOption -FilePath $policy -Option 3 -Delete # remove Audit Mode
} else {
    Set-RuleOption -FilePath $policy -Option 3        # audit before enforcement
}

Write-Host "Created App Control policy: $policy"
Write-Host $(if ($Enforce) { 'Mode: enforce' } else { 'Mode: audit (recommended for validation)' })
