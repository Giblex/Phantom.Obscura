using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using CorePlatform = PhantomVault.Core.Services.Platform;
using PhantomVault.Core.Services.Platform;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels.AutoFill;
using PhantomVault.UI.Views.AutoFill;
using PhantomVault.Core.Services.Platform.Windows;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{

    public sealed class AutoFillOrchestrator : IAutoFillOrchestrator
    {
        private enum State
        {
            GuardCheck,
            DetectPortal,
            ResolveCredential,
            FillCredential,
            WaitForTotp,
            FillTotp,
            ShowNoMatchDialog,
            Done
        }

        private readonly IActiveWindowDetector _windowDetector;
        private readonly ICredentialMatchingEngine _matchingEngine;
        private readonly IAutoInjectPolicyEngine _policyEngine;
        private readonly CorePlatform.IAutoTypeService _autoTypeService;
        private readonly ITotpFieldPoller _totpPoller;
        private readonly TotpService _totpService;
        private readonly WindowsNativeLoginDetector _nativeDetector;
        private readonly IntegratedAttestorService _integratedAttestorService;

        private ICredentialProvider? _credentialProvider;

        /// <summary>
        /// Resolves a linked TOTP section's target entry by id, so a seed held on a
        /// separate authenticator entry can still be filled.
        /// </summary>
        private Credential? LookupCredentialById(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _credentialProvider is null)
                return null;

            return _credentialProvider.GetCredentials()
                .FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        }
        private VaultManifest? _manifest;

        public AutoFillOrchestrator(
        IActiveWindowDetector windowDetector,
        ICredentialMatchingEngine matchingEngine,
        IAutoInjectPolicyEngine policyEngine,
        CorePlatform.IAutoTypeService autoTypeService,
        ITotpFieldPoller totpPoller,
        TotpService totpService,
        WindowsNativeLoginDetector nativeDetector,
        IntegratedAttestorService integratedAttestorService)
    {
        _windowDetector = windowDetector;
        _matchingEngine = matchingEngine;
        _policyEngine = policyEngine;
        _autoTypeService = autoTypeService;
        _totpPoller = totpPoller;
        _totpService = totpService;
        _nativeDetector = nativeDetector;
        _integratedAttestorService = integratedAttestorService;
    }

        public void SetVaultContext(ICredentialProvider provider, VaultManifest manifest)
        {
            _credentialProvider = provider;
            _manifest = manifest;
        }

        public async Task RunAutoFillFlowAsync(string usbDrivePath, CancellationToken ct = default)
        {
            var state = State.GuardCheck;
            AutoInjectContext? context = null;
            NativeLoginContext? nativeContext = null;
            CredentialMatch? bestMatch = null;
            Credential? credential = null;
            EffectiveTotp? effectiveTotp = null;
            bool isBrowser = false;

            Log.Information("[AutoFill] Flow started for USB path: {Path}", usbDrivePath);

            while (state != State.Done && !ct.IsCancellationRequested)
            {
                switch (state)
                {

                    case State.GuardCheck:
                    {
                        var settings = SettingsService.Load();
                        if (!settings.AutoFillModeEnabled)
                        {
                            Log.Debug("[AutoFill] Mode disabled — aborting");
                            state = State.Done;
                            break;
                        }

                        if (_credentialProvider is null || !_credentialProvider.IsVaultUnlocked())
                        {
                            Log.Information("[AutoFill] Vault locked — cannot auto-fill");
                            state = State.Done;
                            break;
                        }

                        if (_manifest?.AutoFillEnabled == false)
                        {
                            Log.Debug("[AutoFill] Vault manifest has AutoFillEnabled=false — aborting");
                            state = State.Done;
                            break;
                        }

                        state = State.DetectPortal;
                        break;
                    }

                    case State.DetectPortal:
                    {
                        context = _windowDetector.GetCurrentContext();
                        isBrowser = _windowDetector.IsActiveBrowser();

                        if (isBrowser)
                        {

                            var url = _windowDetector.TryGetBrowserUrl();
                            if (!string.IsNullOrEmpty(url))
                            {
                                context.Url = url;
                                context.Domain = ExtractDomain(url);
                            }
                            Log.Debug("[AutoFill] Browser detected — domain: {Domain}", context.Domain);
                            state = string.IsNullOrEmpty(context.Domain)
                                ? State.ShowNoMatchDialog
                                : State.ResolveCredential;
                        }
                        else
                        {

                            nativeContext = _windowDetector.DetectNativeLoginFields();
                            if (nativeContext is null)
                            {
                                Log.Debug("[AutoFill] No login fields found in native window — opening no-match dialog");
                                state = State.ShowNoMatchDialog;
                            }
                            else
                            {

                                context.WindowTitle = nativeContext.WindowTitle;
                                context.ProcessName = nativeContext.ProcessName;
                                context.Metadata["WindowHandle"] = nativeContext.WindowHandle.ToString();
                                Log.Debug("[AutoFill] Native login form detected in {Process}", context.ProcessName);
                                state = State.ResolveCredential;
                            }
                        }
                        break;
                    }

                    case State.ResolveCredential:
                    {
                        var credentials = _credentialProvider!.GetCredentials();
                        var matches = _matchingEngine.FindMatches(context!, credentials);

                        if (!matches.Any())
                        {
                            Log.Information("[AutoFill] No credential matched (domain={Domain})", context?.Domain);
                            state = State.ShowNoMatchDialog;
                            break;
                        }

                        bestMatch = matches[0];
                        credential = _credentialProvider.GetCredentialByTitle(bestMatch.CredentialId);

                        if (credential is null)
                        {
                            state = State.ShowNoMatchDialog;
                            break;
                        }

                        Log.Information("[AutoFill] Best match: {Title} (score={Score})",
                            bestMatch.CredentialId, bestMatch.ConfidenceScore);

                        var policy = _policyEngine.GetPolicyForContext(context!);
                        if (!_policyEngine.IsAutoInjectAllowed(context!, policy))
                        {
                            Log.Debug("[AutoFill] Policy blocks auto-inject — aborting");
                            state = State.Done;
                            break;
                        }

                        if (!string.IsNullOrEmpty(credential.PasskeyId))
                        {
                            Log.Information("[AutoFill] Passkey credential — skipping password fill");

                            if (!string.IsNullOrEmpty(credential.AutoTypeSequence))
                                await _autoTypeService.TypeCustomSequenceAsync(
                                    credential.AutoTypeSequence,
                                    credential.Username ?? string.Empty,
                                    credential.Password ?? string.Empty);

                            _credentialProvider.UpdateLastUsed(bestMatch.CredentialId);
                            state = State.Done;
                            break;
                        }

                        if (policy.Behavior == AutoInjectBehavior.Prompt)
                        {
                            Log.Information(
                                "[AutoFill] Prompt mode active for {CredentialId} — skipping silent fill; awaiting user confirmation",
                                bestMatch.CredentialId);
                            state = State.Done;
                            break;
                        }

                        state = State.FillCredential;
                        break;
                    }

                    case State.FillCredential:
                    {
                        var username = credential!.Username ?? string.Empty;
                        var password = credential.Password ?? string.Empty;
                        bool filled = false;

                        if (!isBrowser && nativeContext is not null)
                        {

                            filled = await _windowDetector.TryFillNativeLoginAsync(nativeContext, username, password);
                        }

                        if (!filled)
                        {

                            if (!string.IsNullOrEmpty(credential.AutoTypeSequence))
                                await _autoTypeService.TypeCustomSequenceAsync(
                                    credential.AutoTypeSequence, username, password);
                            else
                                await _autoTypeService.TypeCredentialsAsync(username, password, submit: false);
                        }

                        _credentialProvider!.UpdateLastUsed(bestMatch!.CredentialId);
                        Log.Information("[AutoFill] Credential filled for {Title}", bestMatch.CredentialId);

                        var settings = SettingsService.Load();

                        // The seed may be held in a TOTP section (inline or linked to a
                        // separate authenticator entry) rather than on the entry itself.
                        effectiveTotp = CredentialTotpResolver.Resolve(credential, LookupCredentialById);

                        state = (settings.AutoFillAutoInputTotp && effectiveTotp is not null)
                            ? State.WaitForTotp
                            : State.Done;
                        break;
                    }

                    case State.WaitForTotp:
                    {
                        var settings = SettingsService.Load();
                        await Task.Delay(settings.AutoFillTotpPollDelayMs, ct);

                        Log.Debug("[AutoFill] Polling for TOTP field (timeout={Timeout}ms)", settings.AutoFillTotpPollTimeoutMs);
                        var totpField = await _totpPoller.WaitForTotpFieldAsync(
                            context!,
                            isBrowserContext: isBrowser,
                            timeoutMs: settings.AutoFillTotpPollTimeoutMs,
                            ct: ct);

                        if (totpField is null)
                        {
                            Log.Information("[AutoFill] TOTP field not detected — user fills manually");
                            state = State.Done;
                        }
                        else
                        {
                            state = State.FillTotp;

                            if (!string.IsNullOrEmpty(totpField.NativeAutomationId) && nativeContext is not null)
                                nativeContext.TotpAutomationId = totpField.NativeAutomationId;
                        }
                        break;
                    }

                    case State.FillTotp:
                    {
                        var code = _totpService.GenerateCode(
                            effectiveTotp!.Secret,
                            effectiveTotp.ParsedAlgorithm,
                            DateTimeOffset.UtcNow,
                            effectiveTotp.Digits,
                            effectiveTotp.Period);
                        bool filled = false;

                        if (!isBrowser && nativeContext?.TotpAutomationId is not null)
                            filled = await _nativeDetector.TryFillTotpAsync(nativeContext, code);

                        if (!filled)
                            await _autoTypeService.TypeTextAsync(code);

                        Log.Information("[AutoFill] TOTP code filled");
                        state = State.Done;
                        break;
                    }

                    case State.ShowNoMatchDialog:
                    {
                        var settings = SettingsService.Load();
                        if (!settings.AutoFillShowNewEntryOnNoMatch)
                        {
                            state = State.Done;
                            break;
                        }

                        var portalId = context?.Domain
                            ?? context?.WindowTitle
                            ?? context?.ProcessName
                            ?? "Unknown Portal";

                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var vm = new NoMatchFoundViewModel(
                                portalId,
                                _integratedAttestorService.IsAvailable,
                                _integratedAttestorService.AvailabilityMessage);
                            var dialog = new NoMatchFoundWindow { DataContext = vm };
                            var result = await dialog.ShowAsync();
                            if (result == NoMatchResult.CreatePasskey)
                            {
                                _integratedAttestorService.TryLaunch(out _);
                            }
                        });

                        state = State.Done;
                        break;
                    }
                }
            }

            Log.Information("[AutoFill] Flow complete");
        }

        private static string ExtractDomain(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }
    }
}

