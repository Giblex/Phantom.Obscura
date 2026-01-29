using System;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services.Platform;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Main orchestrator implementing USB auto-inject workflow
    /// </summary>
    public class UsbAutoInjectService : IUsbAutoInjectService
    {
        private readonly IUsbDetector _usbDetector;
        private readonly IActiveWindowDetector _windowDetector;
        private readonly ICredentialMatchingEngine _matchingEngine;
        private readonly IAutoInjectPolicyEngine _policyEngine;
        private readonly IAutoTypeService _autoTypeService;
        private Func<ICredentialProvider?>? _credentialProviderFactory;
        private bool _isRunning;

        public event EventHandler<AutoInjectPromptEventArgs>? PromptRequired;
        public event EventHandler<PasskeyReadyEventArgs>? PasskeyReady;

        public UsbAutoInjectService(
            IUsbDetector usbDetector,
            IActiveWindowDetector windowDetector,
            ICredentialMatchingEngine matchingEngine,
            IAutoInjectPolicyEngine policyEngine,
            IAutoTypeService autoTypeService)
        {
            _usbDetector = usbDetector;
            _windowDetector = windowDetector;
            _matchingEngine = matchingEngine;
            _policyEngine = policyEngine;
            _autoTypeService = autoTypeService;
        }

        /// <summary>
        /// Sets the factory that creates ICredentialProvider on-demand.
        /// This allows integration with Transient VaultViewModel instances.
        /// </summary>
        public void SetCredentialProviderFactory(Func<ICredentialProvider?> factory)
        {
            _credentialProviderFactory = factory;
        }

        public Task StartAsync()
        {
            if (_isRunning)
                return Task.CompletedTask;

            _usbDetector.RemovableDriveInserted += OnUsbInserted;
            _usbDetector.RemovableDriveRemoved += OnUsbRemoved;
            _isRunning = true;

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (!_isRunning)
                return Task.CompletedTask;

            _usbDetector.RemovableDriveInserted -= OnUsbInserted;
            _usbDetector.RemovableDriveRemoved -= OnUsbRemoved;
            _isRunning = false;

            return Task.CompletedTask;
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

                // Get current window context
                var context = _windowDetector.GetCurrentContext();

                // Get credentials from provider
                var credentials = credentialProvider.GetCredentials();
                if (!credentials.Any())
                    return;

                // Find matches
                var matches = _matchingEngine.FindMatches(context, credentials);
                if (!matches.Any())
                    return;

                // Get policy
                var policy = _policyEngine.GetPolicyForContext(context);

                // Check if auto-inject is allowed
                if (!_policyEngine.IsAutoInjectAllowed(context, policy))
                    return;

                // Handle passkeys silently
                var passkeyMatches = matches.Where(m => m.IsPasskey).ToArray();
                if (passkeyMatches.Any() && policy.Behavior == AutoInjectBehavior.Auto)
                {
                    // Silent passkey auth
                    PasskeyReady?.Invoke(this, new PasskeyReadyEventArgs
                    {
                        Domain = context.Domain ?? string.Empty,
                        CredentialId = passkeyMatches[0].CredentialId
                    });
                    return;
                }

                // Trigger prompt for regular credentials
                if (policy.Behavior == AutoInjectBehavior.Auto)
                {
                    // Auto-fill immediately with best match
                    await AutoFillAsync(matches[0].CredentialId, policy.AutoSubmit);
                }
                else if (policy.Behavior == AutoInjectBehavior.Prompt)
                {
                    // Show prompt to user
                    PromptRequired?.Invoke(this, new AutoInjectPromptEventArgs
                    {
                        Context = context,
                        Matches = matches.ToArray(),
                        Policy = policy
                    });
                }
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Auto-inject error: {ex.Message}");
            }
        }

        public async Task AutoFillAsync(string credentialId, bool autoSubmit)
        {
            try
            {
                // Get credential provider from factory
                var credentialProvider = _credentialProviderFactory?.Invoke();
                if (credentialProvider == null)
                    return;

                // Get credential by title (used as ID)
                var credential = credentialProvider.GetCredentialByTitle(credentialId);
                if (credential == null)
                    return;

                // Check if custom auto-type sequence is defined
                if (!string.IsNullOrEmpty(credential.AutoTypeSequence))
                {
                    // Use custom sequence
                    await _autoTypeService.TypeCustomSequenceAsync(
                        credential.AutoTypeSequence,
                        credential.Username ?? string.Empty,
                        credential.Password ?? string.Empty);
                }
                else
                {
                    // Use default sequence: username → tab → password → (optional enter)
                    await _autoTypeService.TypeCredentialsAsync(
                        credential.Username ?? string.Empty,
                        credential.Password ?? string.Empty,
                        autoSubmit);
                }

                // Update last used timestamp
                credentialProvider.UpdateLastUsed(credentialId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-fill error: {ex.Message}");
            }
        }

        private async void OnUsbInserted(string drivePath)
        {
            try
            {
                // Small delay to let USB mount fully
                await Task.Delay(500);

                // Trigger auto-inject workflow
                await TriggerAutoInjectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"USB insert handler error: {ex.Message}");
            }
        }

        private void OnUsbRemoved(string drivePath)
        {
            // Handle USB removal if needed
            // Could auto-lock vault, etc.
        }
    }
}
