using System;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services.Platform;

namespace PhantomVault.Core.Services.AutoInject
{

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

                var credentialProvider = _credentialProviderFactory?.Invoke();
                if (credentialProvider == null)
                    return;

                if (!credentialProvider.IsVaultUnlocked())
                    return;

                var context = _windowDetector.GetCurrentContext();

                var credentials = credentialProvider.GetCredentials();
                if (!credentials.Any())
                    return;

                var matches = _matchingEngine.FindMatches(context, credentials);
                if (!matches.Any())
                    return;

                var policy = _policyEngine.GetPolicyForContext(context);

                if (!_policyEngine.IsAutoInjectAllowed(context, policy))
                    return;

                var passkeyMatches = matches.Where(m => m.IsPasskey).ToArray();
                if (passkeyMatches.Any() && policy.Behavior == AutoInjectBehavior.Auto)
                {

                    PasskeyReady?.Invoke(this, new PasskeyReadyEventArgs
                    {
                        Domain = context.Domain ?? string.Empty,
                        CredentialId = passkeyMatches[0].CredentialId
                    });
                    return;
                }

                if (policy.Behavior == AutoInjectBehavior.Auto)
                {

                    await AutoFillAsync(matches[0].CredentialId, policy.AutoSubmit);
                }
                else if (policy.Behavior == AutoInjectBehavior.Prompt)
                {

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

                Console.WriteLine($"Auto-inject error: {ex.Message}");
            }
        }

        public async Task AutoFillAsync(string credentialId, bool autoSubmit)
        {
            try
            {

                var credentialProvider = _credentialProviderFactory?.Invoke();
                if (credentialProvider == null)
                    return;

                var credential = credentialProvider.GetCredentialByTitle(credentialId);
                if (credential == null)
                    return;

                if (!string.IsNullOrEmpty(credential.AutoTypeSequence))
                {

                    await _autoTypeService.TypeCustomSequenceAsync(
                        credential.AutoTypeSequence,
                        credential.Username ?? string.Empty,
                        credential.Password ?? string.Empty);
                }
                else
                {

                    await _autoTypeService.TypeCredentialsAsync(
                        credential.Username ?? string.Empty,
                        credential.Password ?? string.Empty,
                        autoSubmit);
                }

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

                await Task.Delay(500);

                await TriggerAutoInjectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"USB insert handler error: {ex.Message}");
            }
        }

        private void OnUsbRemoved(string drivePath)
        {

        }
    }
}

