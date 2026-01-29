# PowerShell script to fix DefenceRule instantiations in test files
$testFile = "O:\Users\Giblex\Build Projects\PhantomObscuraV6\tests\PhantomVault.Core.Tests\Security\DefenceEngineIntegrationTests.cs"

# Read the entire file
$content = Get-Content $testFile -Raw

# Define regex patterns and replacements for each unique DefenceRule pattern

# Pattern 1: IntegrityMismatch + SwitchToDecoyVault
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.IntegrityMismatch,\s*MinLevel = ThreatLevel\.Critical,\s*Actions = new\[\] \{ DefenceActionType\.SwitchToDecoyVault \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.IntegrityMismatch, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.SwitchToDecoyVault }, cooldown: null, isEnabled: true)'

# Pattern 2: FailedLoginBurst + AddDelay (with IsEnabled = false)
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = false,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.AddDelay \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.AddDelay }, cooldown: null, isEnabled: false)'

# Pattern 3: FailedLoginBurst + AddDelay + Cooldown TimeSpan.FromSeconds(5)
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.AddDelay \},\s*Cooldown = TimeSpan\.FromSeconds\(5\)\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.AddDelay }, cooldown: TimeSpan.FromSeconds(5), isEnabled: true)'

# Pattern 4: FailedLoginBurst + AddDelay + Cooldown TimeSpan.FromMilliseconds(200)
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.AddDelay \},\s*Cooldown = TimeSpan\.FromMilliseconds\(200\)\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.AddDelay }, cooldown: TimeSpan.FromMilliseconds(200), isEnabled: true)'

# Pattern 5: FailedLoginBurst + Critical + SwitchToDecoyVault
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Critical,\s*Actions = new\[\] \{ DefenceActionType\.SwitchToDecoyVault \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.SwitchToDecoyVault }, cooldown: null, isEnabled: true)'

# Pattern 6: FailedLoginBurst + Warning + TempLockout
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.TempLockout \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.TempLockout }, cooldown: null, isEnabled: true)'

# Pattern 7: FailedLoginBurst + Info + AddDelay
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Info,\s*Actions = new\[\] \{ DefenceActionType\.AddDelay \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Info, actions: new[] { DefenceActionType.AddDelay }, cooldown: null, isEnabled: true)'

# Pattern 8: IntegrityMismatch + EnterReadOnlyMode
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.IntegrityMismatch,\s*MinLevel = ThreatLevel\.Critical,\s*Actions = new\[\] \{ DefenceActionType\.EnterReadOnlyMode \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.IntegrityMismatch, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.EnterReadOnlyMode }, cooldown: null, isEnabled: true)'

# Pattern 9: ExcessiveExports + Warning + EnterReadOnlyMode
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.ExcessiveExports,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.EnterReadOnlyMode \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.ExcessiveExports, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.EnterReadOnlyMode }, cooldown: null, isEnabled: true)'

# Pattern 10: BehaviourDeviation + Warning + ScrubShortLivedData
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.BehaviourDeviation,\s*MinLevel = ThreatLevel\.Warning,\s*Actions = new\[\] \{ DefenceActionType\.ScrubShortLivedData \}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.BehaviourDeviation, minLevel: ThreatLevel.Warning, actions: new[] { DefenceActionType.ScrubShortLivedData }, cooldown: null, isEnabled: true)'

# Pattern 11: FailedLoginBurst + Critical + Multiple Actions
$content = $content -replace `
    '(?s)new DefenceRule\s*\{\s*Id = "rule1",\s*IsEnabled = true,\s*TriggerType = ThreatType\.FailedLoginBurst,\s*MinLevel = ThreatLevel\.Critical,\s*Actions = new\[\]\s*\{\s*DefenceActionType\.AddDelay,\s*DefenceActionType\.TempLockout,\s*DefenceActionType\.RequirePhantomKey,\s*DefenceActionType\.ScrubShortLivedData\s*\}\s*\}', `
    'new DefenceRule(id: "rule1", triggerType: ThreatType.FailedLoginBurst, minLevel: ThreatLevel.Critical, actions: new[] { DefenceActionType.AddDelay, DefenceActionType.TempLockout, DefenceActionType.RequirePhantomKey, DefenceActionType.ScrubShortLivedData }, cooldown: null, isEnabled: true)'

# Write back to file
$content | Set-Content $testFile -NoNewline

Write-Host "✅ Fixed DefenceRule instantiations in DefenceEngineIntegrationTests.cs"
