# Final Integration Steps for USB Auto-Inject

## Overview
The auto-inject system has been implemented with all core components. The final step is to connect VaultViewModel to the auto-inject service when the vault is opened.

## What's Already Done

1. ✅ ICredentialProvider interface created
2. ✅ VaultViewModelCredentialProvider adapter created
3. ✅ UsbAutoInjectService updated to use ICredentialProvider
4. ✅ All services registered in DI container (App.axaml.cs)
5. ✅ Platform-specific services (WindowsActiveWindowDetector, WindowsAutoTypeService) registered

## Final Integration Required

### Step 1: Inject IUsbAutoInjectService into VaultViewModel

Update `VaultViewModel` constructor to accept the auto-inject service:

```csharp
// In VaultViewModel.cs - add to constructor parameters
public VaultViewModel(
    VaultService vaultService,
    ManifestService manifestService,
    IdleLockService idleLockService,
    IZkVaultService zkVaultService,
    DialogService dialogService,
    VaultLockDurationService vaultLockDurationService,
    UsbDetector usbDetector,
    SecureTrashService secureTrashService,
    IconManager iconManager,
    IClipboardGuard? clipboardGuard = null,
    RekeyService? rekeyService = null,
    SecurityCoordinator? securityCoordinator = null,
    IUsbAutoInjectService? autoInjectService = null)  // ADD THIS LINE
{
    // ... existing code ...
    _autoInjectService = autoInjectService;

    // Initialize the credential provider if auto-inject service is available
    if (_autoInjectService != null)
    {
        InitializeAutoInject();
    }
}

// Add field
private readonly IUsbAutoInjectService? _autoInjectService;
```

### Step 2: Create InitializeAutoInject Method

Add this method to VaultViewModel:

```csharp
private void InitializeAutoInject()
{
    if (_autoInjectService == null)
        return;

    try
    {
        // Create credential provider adapter
        var credentialProvider = new VaultViewModelCredentialProvider(this);

        // Register the provider with the service
        // Note: This requires updating UsbAutoInjectService to have a SetCredentialProvider method
        // OR we inject the provider directly (see alternative below)

        // Subscribe to prompt events
        _autoInjectService.PromptRequired += OnAutoInjectPromptRequired;
        _autoInjectService.PasskeyReady += OnPasskeyReady;

        // Start the service
        _ = _autoInjectService.StartAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize auto-inject: {ex.Message}");
    }
}

private void OnAutoInjectPromptRequired(object? sender, AutoInjectPromptEventArgs e)
{
    // Show the prompt window
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
    {
        var promptWindow = new AutoInjectPromptWindow
        {
            DataContext = new AutoInjectPromptViewModel(e.Matches, e.Policy)
        };

        var result = await promptWindow.ShowDialog<bool?>(GetOwnerWindow());

        if (result == true && promptWindow.SelectedCredential != null)
        {
            await _autoInjectService.AutoFillAsync(
                promptWindow.SelectedCredential.CredentialId,
                e.Policy.AutoSubmit);
        }
    });
}

private void OnPasskeyReady(object? sender, PasskeyReadyEventArgs e)
{
    // Handle silent passkey authentication
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
    {
        // Trigger passkey authentication flow
        // This would integrate with your existing PasskeyService
        var passkeyService = TryGetServiceProvider()?.GetService<IPasskeyService>();
        if (passkeyService != null)
        {
            await passkeyService.AuthenticateAsync(e.Domain, e.CredentialId);
        }
    });
}

private Window? GetOwnerWindow()
{
    return _ownerWindow ??
           (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
```

### Step 3: Alternative - Use Factory Pattern (RECOMMENDED)

Since VaultViewModel is Transient, we need a way to provide the credential provider to the singleton UsbAutoInjectService. The best approach is to update the service to accept a provider factory:

**Update UsbAutoInjectService:**

```csharp
public class UsbAutoInjectService : IUsbAutoInjectService
{
    // ... existing fields ...
    private Func<ICredentialProvider?>? _credentialProviderFactory;

    // Add method to set the provider factory
    public void SetCredentialProviderFactory(Func<ICredentialProvider?> factory)
    {
        _credentialProviderFactory = factory;
    }

    public async Task TriggerAutoInjectAsync()
    {
        try
        {
            // Get credential provider from factory
            var credentialProvider = _credentialProviderFactory?.Invoke();
            if (credentialProvider == null)
                return;

            // Check if vault is unlocked
            if (!credentialProvider.IsVaultUnlocked())
                return;

            // ... rest of existing code, using credentialProvider instead of _credentialProvider ...
        }
        // ... rest of method ...
    }
}
```

**Update VaultViewModel initialization:**

```csharp
private void InitializeAutoInject()
{
    if (_autoInjectService == null)
        return;

    try
    {
        // Set the factory that creates credential provider on-demand
        _autoInjectService.SetCredentialProviderFactory(() =>
            new VaultViewModelCredentialProvider(this));

        // Subscribe to events
        _autoInjectService.PromptRequired += OnAutoInjectPromptRequired;
        _autoInjectService.PasskeyReady += OnPasskeyReady;

        // Start the service
        _ = _autoInjectService.StartAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to initialize auto-inject: {ex.Message}");
    }
}
```

### Step 4: Add Missing Using Statements

Add these to VaultViewModel.cs:

```csharp
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.UI.Desktop.Services;
using PhantomVault.UI.Desktop.Views;
```

### Step 5: Create AutoInjectPromptViewModel (Simple)

```csharp
// In ViewModels/AutoInjectPromptViewModel.cs
public class AutoInjectPromptViewModel
{
    public CredentialMatch[] Matches { get; }
    public AutoInjectPolicy Policy { get; }

    public AutoInjectPromptViewModel(CredentialMatch[] matches, AutoInjectPolicy policy)
    {
        Matches = matches;
        Policy = policy;
    }
}
```

### Step 6: Test the Integration

1. Build the project
2. Run the application
3. Open a vault
4. Insert a USB drive
5. Open a browser and navigate to a website (e.g., github.com)
6. The auto-inject prompt should appear if:
   - You have credentials matching "github.com" in your vault
   - The policy allows prompting

## Troubleshooting

### Service Not Starting
Check that the service is being injected into VaultViewModel. Add debug logging:

```csharp
if (_autoInjectService != null)
{
    Debug.WriteLine("[VaultViewModel] Auto-inject service available");
}
else
{
    Debug.WriteLine("[VaultViewModel] Auto-inject service NOT available");
}
```

### No Matches Found
Check that credentials have URLs set:
- Open credential in vault
- Set the "Url" field to match the domain (e.g., "https://github.com")

### Policy Issues
By default, all domains use "Prompt" behavior. Check the policy:

```csharp
var policy = _policyEngine.GetPolicyForContext(context);
Debug.WriteLine($"Policy for {context.Domain}: {policy.Behavior}");
```

### Window Detection Not Working
Verify the active window detector is working:

```csharp
var context = _windowDetector.GetCurrentContext();
Debug.WriteLine($"Window: {context.WindowTitle}");
Debug.WriteLine($"Process: {context.ProcessName}");
Debug.WriteLine($"Domain: {context.Domain}");
```

## Next Steps (Optional)

1. **Phase 2 - USB Sync**: Implement bidirectional sync between desktop and USB
2. **Passkey Integration**: Complete passkey authentication flow
3. **Policy UI**: Create settings UI to manage auto-inject policies
4. **Custom Sequences UI**: Add UI to edit auto-type sequences
5. **Advanced Features**: Tamper detection, air-gap verification, etc.

## Architecture Notes

The factory pattern is recommended because:
- VaultViewModel is Transient (new instance per vault)
- UsbAutoInjectService is Singleton (one instance for app lifetime)
- The factory allows the singleton to access the current VaultViewModel instance
- No circular dependencies or lifecycle issues
