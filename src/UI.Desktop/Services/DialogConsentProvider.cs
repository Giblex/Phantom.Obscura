using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PhantomVault.Core.Services.Network;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// UI-backed <see cref="IInternetConsentProvider"/>. Prompts the user with a
    /// modal confirmation dialog every time a feature wants to open an internet
    /// access grant. There is no silent default — denial is the bias.
    /// </summary>
    public sealed class DialogConsentProvider : IInternetConsentProvider
    {
        private readonly DialogService _dialogService;

        public DialogConsentProvider(DialogService dialogService)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        public async Task<InternetConsentDecision> RequestConsentAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hosts = request.AllowedHosts is { Count: > 0 }
                ? string.Join("\n  • ", request.AllowedHosts)
                : "(none)";

            var ttlMinutes = (int)Math.Round(request.Ttl.TotalMinutes);
            var sessionNote = request.AllowSessionGrant
                ? "\n\nYou can revoke this grant at any time from Settings → Privacy → Network Activity."
                : string.Empty;

            var message =
                $"\"{request.UserVisibleReason}\"\n\n" +
                $"Feature: {request.FeatureId}\n" +
                $"Hosts it will reach:\n  • {hosts}\n\n" +
                $"Grant duration: {ttlMinutes} minute(s)\n" +
                $"All traffic is HTTPS, certificate-pinned, and logged." +
                sessionNote;

            var owner = TryGetActiveWindow();

            var allowed = await Dispatcher.UIThread.InvokeAsync(() =>
                _dialogService.ShowConfirmationAsync(
                    title: "Allow Internet Access?",
                    message: message,
                    owner: owner)).ConfigureAwait(false);

            if (!allowed)
                return new InternetConsentDecision(InternetConsentChoice.Deny, "User denied.");

            // The current confirmation dialog only offers binary yes/no. Always
            // returning GrantOnce keeps grants minimal until a richer prompt UI
            // surfaces the "Grant for session" option explicitly.
            return new InternetConsentDecision(InternetConsentChoice.GrantOnce, "User granted (one-shot).");
        }

        private static Window? TryGetActiveWindow()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;

            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
    }
}
