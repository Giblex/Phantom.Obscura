using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PhantomVault.Core.Services.Network;
using PhantomVault.UI.Views.Dialogs;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// UI-backed <see cref="IInternetConsentProvider"/>. Prompts the user with a
    /// modal confirmation dialog every time a feature wants to open an internet
    /// access grant. There is no silent default — denial is the bias.
    /// </summary>
    public sealed class DialogConsentProvider : IInternetConsentProvider
    {
        public DialogConsentProvider(DialogService dialogService)
        {
            ArgumentNullException.ThrowIfNull(dialogService);
        }

        public async Task<InternetConsentDecision> RequestConsentAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var owner = TryGetActiveWindow();
            if (owner is null)
                return new InternetConsentDecision(InternetConsentChoice.Deny, "No active window available for consent.");

            var allowed = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new InternetConsentWindow(request.AllowedHosts, request.Ttl);
                await dialog.ShowDialog(owner);
                return dialog.Allowed;
            }).ConfigureAwait(false);

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
