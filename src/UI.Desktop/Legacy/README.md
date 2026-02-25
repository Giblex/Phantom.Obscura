# Legacy Folder

This folder contains files that are no longer in active use but have been retained for reference or potential future restoration.

**Date Archived:** 2026-02-03

## Contents

### Examples/

| File | Reason |
|------|--------|
| `BehaviorAnimationExample.axaml` | Documentation example only, never imported or used in production views |
| `BehaviorAnimationExample.axaml.cs` | Code-behind for above |

### Services/

| File | Reason | Replacement |
|------|--------|-------------|
| `IntegratedRecoveryService.cs` | PhantomRecovery integration disabled | `IntegratedRecoveryServiceStub.cs` |
| `RecoveryDeveloperMode.cs` | PhantomRecovery integration disabled | `RecoveryDeveloperModeStub.cs` |

### Views/

| File | Reason | Replacement |
|------|--------|-------------|
| `RecoveryPanel.axaml` | PhantomRecovery integration disabled | `RecoveryPanelStub.cs` |
| `RecoveryPanel.axaml.cs` | PhantomRecovery integration disabled | `RecoveryPanelStub.cs` |
| `VaultWindow.axaml.backup` | Backup file from previous refactoring | N/A |

## Exclusion

These files are excluded from build via the following in `PhantomVault.UI.csproj`:

```xml
<Compile Remove="Legacy\**\*" />
<AvaloniaXaml Remove="Legacy\**\*.axaml" />
<AvaloniaResource Remove="Legacy\**\*" />
<None Remove="Legacy\**\*" />
```

## Re-enabling PhantomRecovery Integration

To re-enable PhantomRecovery integration:

1. Move files from `Legacy/Services/` and `Legacy/Views/` back to their original locations
2. Remove or rename the stub files (`*Stub.cs`)
3. Uncomment the PhantomRecovery project references in `PhantomVault.UI.csproj`:

   ```xml
   <ProjectReference Include="..\..\..\PhantomRecovery\PhantomRecovery.App\PhantomRecovery.App.csproj" />
   <ProjectReference Include="..\..\..\PhantomRecovery\PhantomRecovery.Core\PhantomRecovery.Core.csproj" />
   ```

## Safe to Delete

These files can be permanently deleted after confirming they are no longer needed:

- All files in this folder
- The entire `Legacy/` folder

---

*Note: VeraCrypt-related files were retained in the active codebase as they are still referenced by multiple ViewModels and the Installer functionality. A separate cleanup effort may be needed if VeraCrypt support is fully deprecated in favor of the custom container implementation.*
