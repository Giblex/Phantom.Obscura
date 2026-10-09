using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Security;
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
        private readonly AttestorCredentialBrokerClient _attestorBroker;

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
        IntegratedAttestorService integratedAttestorService,
        AttestorCredentialBrokerClient attestorBroker)
    {
        _windowDetector = windowDetector;
        _matchingEngine = matchingEngine;
        _policyEngine = policyEngine;
        _autoTypeService = autoTypeService;
        _totpPoller = totpPoller;
        _totpService = totpService;
        _nativeDetector = nativeDetector;
        _integratedAttestorService = integratedAttestorService;
        _attestorBroker = attestorBroker;
    }

        /// <inheritdoc />
        public bool IsVaultReady
        {
            get
            {
                try { return _credentialProvider is not null && _credentialProvider.IsVaultUnlocked(); }
                catch { return false; }
            }
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
            string? attestorTotpReference = null;
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
                            // "Enable browser auto-fill" — the companion to the desktop-apps
                            // switch below. Both were saved and never read, so auto-fill ran in
                            // browsers and native windows alike whatever the user had chosen.
                            if (!SettingsService.Load().EnableAutoFill)
                            {
                                Log.Debug("[AutoFill] Browser auto-fill is disabled in settings — stopping");
                                state = State.Done;
                                break;
                            }

                            var url = _windowDetector.TryGetBrowserUrl();
                            if (!string.IsNullOrEmpty(url))
                            {
                                context.Url = url;
                                context.Domain = ExtractDomain(url);
                            }
                            Log.Debug("[AutoFill] Browser detected — domain: {Domain}", context.Domain);

                            // The whitelist was saved in settings but never consulted, so it
                            // restricted nothing. An empty list still means "no restriction";
                            // a non-empty one now actually limits where secrets are typed.
                            if (!IsDomainAllowed(context.Domain))
                            {
                                Log.Information("[AutoFill] Domain {Domain} is not in the auto-fill whitelist — stopping", context.Domain);
                                state = State.Done;
                                break;
                            }

                            state = string.IsNullOrEmpty(context.Domain)
                                ? State.ShowNoMatchDialog
                                : State.ResolveCredential;
                        }
                        else
                        {

                            // Typing a vault secret into an arbitrary desktop application is a
                            // bigger step than filling a browser form — there is no domain to
                            // check it against. The switch for it existed in settings but was
                            // never read, so native auto-fill happened regardless of the choice.
                            if (!SettingsService.Load().AutoFillDesktopApps)
                            {
                                Log.Debug("[AutoFill] Desktop-app auto-fill is disabled — not inspecting the native window");
                                state = State.Done;
                                break;
                            }

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
                        var fillSettings = SettingsService.Load();

                        // These three switches exist in Auto-fill settings and were previously
                        // saved but never read: the flow filled both fields and never submitted,
                        // whatever the user had chosen. A setting that does nothing is worse than
                        // no setting, because it tells the user they are in control when they are
                        // not.
                        var username = fillSettings.AutoFillInjectUsername
                            ? (credential!.Username ?? string.Empty)
                            : string.Empty;

                        var password = fillSettings.AutoFillInjectPassword
                            ? (credential!.Password ?? string.Empty)
                            : string.Empty;

                        bool submitAfterFill = fillSettings.AutoFillAutoSubmit;
                        bool filled = false;

                        if (username.Length == 0 && password.Length == 0)
                        {
                            Log.Information("[AutoFill] Both username and password injection are disabled — nothing to fill");
                            state = State.Done;
                            break;
                        }

                        if (!isBrowser && nativeContext is not null)
                        {

                            filled = await _windowDetector.TryFillNativeLoginAsync(nativeContext, username, password);
                        }

                        if (!filled)
                        {

                            if (!string.IsNullOrEmpty(credential!.AutoTypeSequence))
                                await _autoTypeService.TypeCustomSequenceAsync(
                                    credential.AutoTypeSequence, username, password);
                            else
                                await _autoTypeService.TypeCredentialsAsync(username, password, submit: submitAfterFill);
                        }

                        _credentialProvider!.UpdateLastUsed(bestMatch!.CredentialId);
                        Log.Information("[AutoFill] Credential filled for {Title}", bestMatch.CredentialId);

                        var settings = SettingsService.Load();

                        // The seed may be held in a TOTP section (inline or linked to a
                        // separate authenticator entry) rather than on the entry itself.
                        effectiveTotp = CredentialTotpResolver.Resolve(credential!, LookupCredentialById);
                        attestorTotpReference = credential!.AttestorTotpReference;

                        state = (settings.AutoFillAutoInputTotp &&
                                 (effectiveTotp is not null || !string.IsNullOrWhiteSpace(attestorTotpReference)))
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
                        string code;
                        if (!string.IsNullOrWhiteSpace(attestorTotpReference))
                        {
                            var snapshot = await _attestorBroker.GetTotpCodeAsync(attestorTotpReference, ct);
                            if (snapshot == null)
                            {
                                Log.Warning("[AutoFill] Attestor did not release a TOTP code");
                                state = State.Done;
                                break;
                            }
                            code = snapshot.Code;
                        }
                        else
                        {
                            code = _totpService.GenerateCode(
                                effectiveTotp!.Secret,
                                effectiveTotp.ParsedAlgorithm,
                                DateTimeOffset.UtcNow,
                                effectiveTotp.Digits,
                                effectiveTotp.Period);
                        }
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
                            else if (result == NoMatchResult.GeneratePassword)
                            {
                                await GenerateFillAndOfferToSaveAsync(context, nativeContext, portalId);
                            }
                        });

                        state = State.Done;
                        break;
                    }
                }
            }

            Log.Information("[AutoFill] Flow complete");
        }

        /// <summary>
        /// Raised after a generated password has been typed into a form, so the vault can offer to
        /// save it. The orchestrator deliberately does not write to the vault itself: saving goes
        /// through the same confirm-then-save path as a captured password, so there is one place
        /// that decides what gets stored and the user always sees it before it lands.
        /// </summary>
        public event EventHandler<GeneratedCredentialEventArgs>? GeneratedCredentialFilled;

        /// <summary>
        /// Generates a strong password, types it into the form that had no match, and hands it to
        /// the vault to offer saving.
        ///
        /// No username is invented here. The field that triggered this may or may not have one
        /// filled in already, and guessing would quietly save a wrong account name against a real
        /// password — worse than leaving it blank for the user to complete in the save prompt.
        /// </summary>
        private async Task GenerateFillAndOfferToSaveAsync(
            AutoInjectContext? context,
            NativeLoginContext? nativeContext,
            string portalId)
        {
            string generated;
            try
            {
                generated = PasswordGenerator.Generate();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AutoFill] Password generation failed");
                return;
            }

            try
            {
                bool filled = false;
                if (nativeContext is not null)
                    filled = await _windowDetector.TryFillNativeLoginAsync(nativeContext, string.Empty, generated);

                if (!filled)
                    await _autoTypeService.TypeCredentialsAsync(string.Empty, generated, submit: false);

                Log.Information("[AutoFill] Generated password filled for {Portal}", portalId);

                GeneratedCredentialFilled?.Invoke(this, new GeneratedCredentialEventArgs(
                    portalId,
                    context?.Url ?? string.Empty,
                    context?.Domain ?? portalId,
                    generated));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AutoFill] Filling the generated password failed");
            }
        }

        /// <summary>
        /// Whether auto-fill is permitted on this domain.
        ///
        /// An empty whitelist means no restriction, which is the default and must stay that way —
        /// treating empty as "allow nothing" would silently disable auto-fill for everyone. A
        /// configured list is matched on the registrable domain, so listing "example.com" also
        /// covers its subdomains without the user having to enumerate them.
        /// </summary>
        private static bool IsDomainAllowed(string? domain)
        {
            try { return IsDomainAllowed(domain, SettingsService.Load().AutoFillDomainWhitelist); }
            catch { return true; }
        }

        /// <summary>
        /// The whitelist rule itself, separated from loading settings so it can be tested directly.
        /// </summary>
        public static bool IsDomainAllowed(string? domain, string? whitelist)
        {
            var raw = whitelist ?? string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return true;

            if (string.IsNullOrWhiteSpace(domain))
                return false;

            var candidate = domain.Trim().TrimEnd('.').ToLowerInvariant();

            foreach (var entry in raw.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var allowed = entry.Trim().TrimStart('*', '.').TrimEnd('.').ToLowerInvariant();
                if (allowed.Length == 0)
                    continue;

                if (candidate == allowed || candidate.EndsWith("." + allowed, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string ExtractDomain(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }
    }
}

