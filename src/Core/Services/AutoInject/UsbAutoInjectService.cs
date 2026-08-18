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

                System.Diagnostics.Debug.WriteLine($"Auto-inject error: {ex.Message}");
            }
        }

        public async Task AutoFillAsync(string credentialId, bool autoSubmit, AutoFillField field = AutoFillField.Both)
        {
            try
            {
                var credential = ResolveCredential(credentialId, out var credentialProvider);
                if (credential == null || credentialProvider == null)
                    return;

                switch (field)
                {
                    case AutoFillField.UsernameOnly:
                        await _autoTypeService.TypeTextAsync(credential.Username ?? string.Empty);
                        break;

                    case AutoFillField.PasswordOnly:
                        await _autoTypeService.TypeTextAsync(credential.Password ?? string.Empty);
                        break;

                    case AutoFillField.TotpCode:
                        var totp = ReadTotp(credential);
                        if (totp == null) return;
                        await _autoTypeService.TypeTextAsync(totp.Code);
                        break;

                    default:
                        // A custom sequence describes a whole login, so it only applies
                        // when filling everything.
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
                        break;
                }

                credentialProvider.UpdateLastUsed(credentialId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-fill error: {ex.Message}");
            }
        }

        public async Task<bool> CopyFieldAsync(string credentialId, AutoFillField field, int clearAfterSeconds = 30)
        {
            try
            {
                var credential = ResolveCredential(credentialId, out _);
                if (credential == null) return false;

                string? value = field switch
                {
                    AutoFillField.UsernameOnly => credential.Username,
                    AutoFillField.PasswordOnly => credential.Password,
                    AutoFillField.TotpCode => ReadTotp(credential)?.Code,
                    _ => null
                };

                if (string.IsNullOrEmpty(value)) return false;

                // Keeps the value out of Windows clipboard history and wipes it on a
                // timer, so a copied password does not linger indefinitely.
                return await Security.ClipboardHistoryExclusion.CopyWithExclusionAndAutoClearAsync(
                    value, TimeSpan.FromSeconds(Math.Max(1, clearAfterSeconds)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy field error: {ex.Message}");
                return false;
            }
        }

        public Task<TotpSnapshot?> GetTotpAsync(string credentialId)
        {
            try
            {
                var credential = ResolveCredential(credentialId, out _);
                return Task.FromResult(credential == null ? null : ReadTotp(credential));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TOTP read error: {ex.Message}");
                return Task.FromResult<TotpSnapshot?>(null);
            }
        }

        private Models.Credential? ResolveCredential(string credentialId, out ICredentialProvider? provider)
        {
            provider = _credentialProviderFactory?.Invoke();
            return provider?.GetCredentialByTitle(credentialId);
        }

        /// <summary>
        /// Generates the current code and works out how long it survives, from the
        /// credential's own digits/step/algorithm rather than assuming 6/30/SHA1.
        /// </summary>
        private static TotpSnapshot? ReadTotp(Models.Credential credential)
        {
            // The seed may live on the entry or in a TOTP section, so resolve rather than
            // reading Credential.TotpSecret directly.
            var totp = CredentialTotpResolver.Resolve(credential);
            if (totp == null) return null;

            int step = totp.Period;

            var now = DateTimeOffset.UtcNow;
            string code = new TotpService().GenerateCode(
                totp.Secret, totp.ParsedAlgorithm, now, totp.Digits, step);

            int remaining = step - (int)(now.ToUnixTimeSeconds() % step);

            return new TotpSnapshot
            {
                Code = code,
                SecondsRemaining = remaining,
                StepSeconds = step
            };
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
                System.Diagnostics.Debug.WriteLine($"USB insert handler error: {ex.Message}");
            }
        }

        private void OnUsbRemoved(string drivePath)
        {

        }
    }
}

