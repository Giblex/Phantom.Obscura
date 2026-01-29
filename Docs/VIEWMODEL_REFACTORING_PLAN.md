# VaultViewModel Refactoring Plan

**Current Status**: VaultViewModel is 4,492 lines with 115+ private fields, 72+ public properties, and 60+ commands

**Goal**: Break down into focused, maintainable child ViewModels following Single Responsibility Principle

---

## Analysis: Identified Functional Areas

### 1. **Credential Management** (~35% of code)
**Responsibilities**:
- CRUD operations for credentials
- Credential collection management
- Add/Edit/Delete commands
- Credential type-specific commands (Login, Payment Card, Identity, etc.)
- Copy operations (password, username)
- Favorite toggling

**Fields/Properties**:
- `_credentials`, `_filteredCredentials`, `_selectedCredential`
- `_flaggedCredentials`, `_passkeys`
- `AddCredentialCommand`, `EditCredentialCommand`, `DeleteCredentialCommand`
- `AddLoginCredentialCommand`, `AddPaymentCardCommand`, etc.
- `CopyPasswordCommand`, `CopyUsernameCommand`
- `ToggleFavoriteCommand`

---

### 2. **Search & Filtering** (~15% of code)
**Responsibilities**:
- Search text handling
- Filter application (all, favorites, passkeys, recent, expiring soon)
- Sorting options
- Grouped list generation
- Entry type filtering

**Fields/Properties**:
- `_searchText`, `_sortOption`
- `_isShowingAll`, `_isShowingFavorites`, `_isShowingPasskeys`
- `_isShowingRecent`, `_isShowingExpiringSoon`
- `_groupedListItems`
- `_currentEntryType`
- `ShowAllCommand`, `ShowFavoritesCommand`, `ShowPasskeysCommand`
- `ShowRecentCommand`, `ShowExpiringSoonCommand`
- `FilterByEntryTypeCommand`, `ClearSearchCommand`

---

### 3. **Security & Lockscreen** (~20% of code)
**Responsibilities**:
- In-app lockscreen management
- Soft/hard lock states
- PIN and passphrase authentication
- Security threat monitoring
- USB device removal handling
- Idle lock timer integration

**Fields/Properties**:
- `_isLockscreenVisible`, `_isSoftLocked`
- `_lockscreenTitle`, `_lockscreenMessage`, `_lockscreenPassword`, `_lockscreenPin`
- `_lockscreenError`, `_showPassphraseFallback`
- `_currentThreatLevel`, `_securityStatus`, `_showSecurityAlert`
- `_isDeveloperBypassMode`
- `LockCommand`, `UnlockWithPasswordCommand`, `UnlockWithPinCommand`
- `ShowPassphraseFallbackCommand`, `DismissLockscreenCommand`, `SetupPinLockCommand`

---

### 4. **Category Management** (~10% of code)
**Responsibilities**:
- Category operations
- Category selection/filtering
- Category highlight animations
- Category manager panel

**Fields/Properties**:
- `_categories`, `_activeCategory`, `_activeCategoryDisplayName`
- `_isCategoryManagerPanelVisible`, `_categoryManagerViewModel`
- Category highlight properties (margin, opacity, offset, light shift)
- `SelectCategoryCommand`, `ManageCategoriesCommand`

---

### 5. **Trash/Recovery Management** (~10% of code)
**Responsibilities**:
- Secure trash operations
- Recovery panel management
- Trash item selection
- Restore/purge operations

**Fields/Properties**:
- `_filteredTrashItems`, `_selectedTrashItem`
- `_isShowingSecureTrash`, `_isRecoveryPanelVisible`
- `OpenTrashManagerCommand`, `ToggleRecoveryPanelCommand`
- `RecoverTrashItemCommand`, `PurgeTrashItemCommand`
- `RestoreSelectedTrashCommand`, `PurgeSelectedTrashCommand`
- `ToggleTrashSelectAllCommand`

---

### 6. **Undo/Redo System** (~5% of code)
**Responsibilities**:
- Undo stack management
- Redo stack management
- Action tracking

**Fields/Properties**:
- `_undoStack`, `_redoStack`
- `UndoCommand`, `RedoCommand`

---

### 7. **UI State & Settings** (~5% of code)
**Responsibilities**:
- View mode (list/grid)
- Theme management
- Privacy mode
- Settings panel visibility
- Edit panel visibility

**Fields/Properties**:
- `_isGridView`, `_isDarkTheme`, `_viewModeIcon`
- `_privacyModeEnabled`
- `_isEditPanelVisible`
- `ToggleViewModeCommand`, `ToggleThemeCommand`
- Settings-related commands

---

## Refactoring Strategy

### Phase 1: Create Child ViewModels

1. **CredentialManagementViewModel**
   - Move all credential CRUD logic
   - Move credential commands
   - Expose `IObservable<CredentialViewModel>` for selected credential
   - Expose `ObservableCollection<CredentialViewModel>` for filtered credentials

2. **SearchAndFilterViewModel**
   - Move search/filter logic
   - Move sorting logic
   - Expose filter state properties
   - Subscribe to credential collection changes

3. **SecurityViewModel**
   - Move lockscreen logic
   - Move authentication logic
   - Move security monitoring
   - Handle USB removal events

4. **CategoryManagementViewModel**
   - Move category operations
   - Move category selection logic
   - Move category highlight animations

5. **TrashManagementViewModel**
   - Move trash operations
   - Move recovery logic
   - Expose trash collection

6. **UndoRedoViewModel**
   - Move undo/redo stacks
   - Expose undo/redo commands
   - Generic action tracking

### Phase 2: Update VaultViewModel

- Keep only coordination logic
- Keep vault initialization
- Keep service references
- Delegate to child ViewModels
- Maintain communication between child ViewModels via events/observables

### Phase 3: Update UI Bindings

- Update AXAML bindings to reference child ViewModels
- Example: `{Binding CredentialManagement.SelectedCredential}`
- Example: `{Binding Search.SearchText}`

---

## Benefits

1. **Maintainability**: Each ViewModel has single responsibility
2. **Testability**: Easier to unit test individual concerns
3. **Reusability**: Child ViewModels can be reused in other contexts
4. **Performance**: Smaller ViewModels with focused change notifications
5. **Team Collaboration**: Multiple developers can work on different ViewModels simultaneously

---

## Implementation Timeline

- **Phase 1**: 8-12 hours (create 6 child ViewModels)
- **Phase 2**: 2-3 hours (refactor VaultViewModel)
- **Phase 3**: 2-3 hours (update UI bindings)
- **Testing**: 2-3 hours (verify all functionality works)

**Total**: 14-21 hours

---

## File Structure

```
ViewModels/
├── VaultViewModel.cs (coordinator, 500-800 lines)
├── Vault/
│   ├── CredentialManagementViewModel.cs
│   ├── SearchAndFilterViewModel.cs
│   ├── SecurityViewModel.cs
│   ├── CategoryManagementViewModel.cs
│   ├── TrashManagementViewModel.cs
│   └── UndoRedoViewModel.cs
```

---

**Status**: Ready to implement
**Next Step**: Create `CredentialManagementViewModel` first (largest component)
