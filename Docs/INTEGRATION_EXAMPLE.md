# USB Auto-Inject Integration Guide

## Complete Integration Example

This guide shows how to wire up the USB auto-inject system in your PhantomVault application.

## Step 1: Register Services in DI Container

```csharp
// In your App.xaml.cs or Program.cs startup code:

using PhantomVault.Core.Extensions;
using PhantomVault.Platform.Services.Windows;
using Microsoft.Extensions.DependencyInjection;

public void ConfigureServices(IServiceCollection services)
{
    // Get application data directory
    var dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhantomVault");

    // Register core auto-inject services
    services.AddUsbAutoInject(dataDir);

    // Register platform-specific services (Windows)
    if (OperatingSystem.IsWindows())
    {
        services.AddSingleton<IActiveWindowDetector, WindowsActiveWindowDetector>();
        services.AddSingleton<IAutoTypeService, WindowsAutoTypeService>();
    }

    // Register existing vault service
    services.AddSingleton<VaultService>();

    // ... other services
}
```

## Step 2: Initialize in Main Window

```csharp
// In your MainWindow.axaml.cs or App.axaml.cs:

using PhantomVault.Core.Services.AutoInject;
using PhantomVault.UI.Views;

public partial class MainWindow : Window
{
    private readonly IUsbAutoInjectService _autoInjectService;
    private readonly VaultService _vaultService;

    public MainWindow(
        IUsbAutoInjectService autoInjectService,
        VaultService vaultService)
    {
        InitializeComponent();

        _autoInjectService = autoInjectService;
        _vaultService = vaultService;

        // Wire up event handlers
        SetupAutoInject();
    }

    private void SetupAutoInject()
    {
        // Handle prompt required event
        _autoInjectService.PromptRequired += OnAutoInjectPromptRequired;

        // Handle passkey ready event
        _autoInjectService.PasskeyReady += OnPasskeyReady;

        // Start monitoring (when vault is unlocked)
        _vaultService.VaultUnlocked += async (s, e) =>
        {
            await _autoInjectService.StartAsync();
        };

        // Stop monitoring (when vault is locked)
        _vaultService.VaultLocked += async (s, e) =>
        {
            await _autoInjectService.StopAsync();
        };
    }

    private async void OnAutoInjectPromptRequired(
        object? sender,
        AutoInjectPromptEventArgs e)
    {
        // Create and show the prompt window
        var promptWindow = new AutoInjectPromptWindow();
        promptWindow.SetCredentials(e.Matches, e.Context);

        var result = await promptWindow.ShowDialog<AutoInjectPromptResult>(this);

        if (result == AutoInjectPromptResult.Yes &&
            promptWindow.SelectedCredential != null)
        {
            // User clicked Yes - auto-fill the credential
            await _autoInjectService.AutoFillAsync(
                promptWindow.SelectedCredential.CredentialId,
                e.Policy.AutoSubmit);
        }
        else if (result == AutoInjectPromptResult.MoreOptions)
        {
            // Show more options panel
            ShowMoreOptionsPanel(e.Matches, e.Context);
        }
        // If No, just close (do nothing)
    }

    private void OnPasskeyReady(object? sender, PasskeyReadyEventArgs e)
    {
        // Handle silent passkey authentication
        ShowNotification($"✓ Authenticated to {e.Domain}");
    }

    private void ShowMoreOptionsPanel(CredentialMatch[] matches, AutoInjectContext context)
    {
        // TODO: Implement more options panel
        // Could show:
        // - Copy to clipboard option
        // - Edit before filling option
        // - Custom field mapping
        // - Policy management
        // - Open main app
    }

    private void ShowNotification(string message)
    {
        // Show toast notification or status bar message
        Console.WriteLine(message);
    }
}
```

## Step 3: Manual Trigger (Optional)

You can also trigger auto-inject manually with a keyboard shortcut:

```csharp
// In your MainWindow:

private void OnGlobalHotkey(object? sender, KeyEventArgs e)
{
    // Ctrl+Shift+A to manually trigger auto-inject
    if (e.KeyModifiers == KeyModifiers.Control | KeyModifiers.Shift &&
        e.Key == Key.A)
    {
        _ = _autoInjectService.TriggerAutoInjectAsync();
        e.Handled = true;
    }
}
```

## Step 4: Create Default Policies

Set up some sensible default policies on first run:

```csharp
private void CreateDefaultPolicies()
{
    var policyEngine = serviceProvider.GetService<IAutoInjectPolicyEngine>();

    // Banking sites - always prompt, never auto
    policyEngine.SavePolicy(new AutoInjectPolicy
    {
        DomainPattern = "*.bank.com",
        Behavior = AutoInjectBehavior.Prompt,
        RequireAdditionalAuth = true,
        AutoSubmit = false
    });

    // Work domains - auto-fill during business hours
    policyEngine.SavePolicy(new AutoInjectPolicy
    {
        DomainPattern = "*.company.com",
        Behavior = AutoInjectBehavior.Auto,
        AutoSubmit = true,
        TimeRestriction = new TimeRestriction
        {
            StartHour = 9,
            EndHour = 17,
            AllowedDays = new List<int> { 1, 2, 3, 4, 5 } // Mon-Fri
        }
    });

    // GitHub - prompt but allow auto-submit
    policyEngine.SavePolicy(new AutoInjectPolicy
    {
        DomainPattern = "github.com",
        Behavior = AutoInjectBehavior.Prompt,
        AutoSubmit = true
    });
}
```

## Complete Flow Example

### Scenario: User Plugs in USB at GitHub Login Page

```
1. User navigates to github.com/login
2. User plugs in USB drive
3. UsbDetector fires RemovableDriveInserted event
4. UsbAutoInjectService.OnUsbInserted() is called
5. Service waits 500ms for USB to mount
6. Service calls TriggerAutoInjectAsync()
7. WindowsActiveWindowDetector captures:
   - Window title: "Sign in to GitHub - Google Chrome"
   - Process: "chrome.exe"
   - URL: "https://github.com/login"
   - Domain: "github.com"
8. CredentialMatchingEngine searches vault:
   - Finds: "GitHub (john@email.com)" - Score: 85
   - Finds: "GitHub Work (john@company.com)" - Score: 80
9. AutoInjectPolicyEngine checks policy for "github.com"
   - Behavior: Prompt
   - AutoSubmit: true
10. Service fires PromptRequired event
11. MainWindow shows AutoInjectPromptWindow
12. Prompt displays:
    ┌─────────────────────────────────────┐
    │  🔐 PhantomVault Detected           │
    │                                     │
    │  Found credentials for github.com:  │
    │                                     │
    │  • GitHub (john@email.com)          │
    │    github.com                       │
    │                                     │
    │  • GitHub Work (john@company.com)   │
    │    github.com                       │
    │                                     │
    │  [More Options] [No] [Yes]          │
    └─────────────────────────────────────┘
13. User clicks [Yes]
14. MainWindow calls AutoFillAsync(credentialId, autoSubmit: true)
15. WindowsAutoTypeService types:
    - "john@email.com"
    - [Tab]
    - "••••••••••" (password)
    - [Delay 200ms]
    - [Enter]
16. GitHub form submits
17. User is logged in!
```

## Advanced: Custom Auto-Type Sequences

For sites with unusual login flows:

```csharp
// In credential editor, set custom sequence:
credential.AutoTypeSequence = "{username}{delay:1000}{tab}{password}{tab}{tab}{enter}";

// For sites with separate username/password pages:
credential.AutoTypeSequence = "{username}{enter}{delay:2000}{password}{enter}";

// For sites with 2FA:
credential.AutoTypeSequence = "{username}{tab}{password}{tab}";
// (User manually enters 2FA code)
```

## Testing

### Manual Test
1. Create a test credential for a website
2. Navigate to that website's login page
3. Plug in USB drive
4. Verify prompt appears
5. Click Yes
6. Verify credentials are filled

### Automated Test
```csharp
[Fact]
public async Task AutoInject_WithMatchingCredential_ShowsPrompt()
{
    // Arrange
    var service = CreateAutoInjectService();
    var eventFired = false;

    service.PromptRequired += (s, e) =>
    {
        eventFired = true;
        Assert.NotEmpty(e.Matches);
    };

    // Act
    await service.TriggerAutoInjectAsync();

    // Assert
    Assert.True(eventFired);
}
```

## Troubleshooting

### Prompt doesn't appear
- Check vault is unlocked
- Verify service is started (`StartAsync()` was called)
- Check active window is detected (not null)
- Ensure credentials exist for that domain

### Wrong credentials matched
- Adjust credential URL to be more specific
- Add tags to credential
- Update matching algorithm confidence scores

### Auto-type types in wrong fields
- Use custom auto-type sequence
- Add delays: `{username}{delay:500}{tab}{password}`
- Check tab order on website

## Performance Tips

1. **Lazy initialization**: Only start service when vault is unlocked
2. **Debouncing**: Add slight delay before showing prompt
3. **Caching**: Cache active window context to avoid repeated Win32 calls
4. **Background matching**: Run credential matching on background thread

## Security Considerations

1. **Never store policies in plaintext** - Encrypt policy file
2. **Validate machine fingerprint** - Prevent unauthorized machine access
3. **Audit logging** - Log all auto-fill actions
4. **Timeout prompts** - Auto-dismiss prompt after 30 seconds
5. **Clear clipboard** - If using clipboard, clear after use

## Next Steps

1. Implement desktop ↔ USB sync
2. Add manifest versioning
3. Create "More Options" panel
4. Implement passkey FIDO2 support
5. Add breach monitoring integration

## Resources

- [Auto-Inject Implementation Doc](AUTO_INJECT_IMPLEMENTATION.md)
- [API Documentation](#)
- [Contributing Guide](#)
