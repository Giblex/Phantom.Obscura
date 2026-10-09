using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using PhantomVault.UI.Services;
using Serilog;

namespace PhantomVault.UI.Services.Privileged
{
    /// <summary>
    /// The one place that tells the user the privileged helper is not available and offers to fix
    /// it. Every entry point — app start, the setup wizard, vault unlock — goes through here, so
    /// the wording and the remedy stay consistent no matter where the problem is noticed.
    ///
    /// The helper can be stopped or disabled from outside the app at any time (Services.msc, a
    /// cleanup tool, a policy), so "it was installed once" is not something the app can assume on
    /// any later launch. Each check reads the live state rather than a remembered one.
    /// </summary>
    public sealed class PrivilegedHelperActivator
    {
        private readonly BrokerServiceController _controller;
        private readonly DialogService _dialogs;

        public PrivilegedHelperActivator(BrokerServiceController controller, DialogService dialogs)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        }

        /// <summary>
        /// Checks the helper and, when it is not running, offers to activate it. Returns true when
        /// the helper is running by the time this returns.
        /// </summary>
        /// <param name="owner">Window to parent the dialogue to, when there is one.</param>
        /// <param name="context">
        /// Short phrase naming what the helper is needed for, e.g. "to create a vault". Appears in
        /// the dialogue so the interruption is explained rather than arbitrary.
        /// </param>
        public async Task<bool> EnsureActiveAsync(Window? owner, string context)
        {
            PrivilegedHelperState state;
            try
            {
                state = _controller.GetState();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PrivilegedHelper] Could not determine helper state");
                return false;
            }

            // The service is up, yet the caller could not use it. Starting it again would change
            // nothing. The helper accepts connections from exactly one allow-listed executable
            // path, recorded when it was installed, so this is almost always a copy of the app
            // running from a different folder than the registered one.
            //
            // Saying "not running" here would be wrong and would send the user off restarting a
            // service that is already running.
            if (state == PrivilegedHelperState.Running)
            {
                Log.Warning(
                    "[PrivilegedHelper] Service is running but was unreachable from {Exe} — likely not the allow-listed client",
                    Environment.ProcessPath ?? "<unknown>");

                await _dialogs.ShowWarningAsync(
                    "Privileged helper refused this copy of the app",
                    "The Phantom Obscura privileged helper is installed and running, but it did not accept a "
                    + $"connection from this copy of the application, so it is unavailable {context}.\n\n"
                    + "The helper only accepts the exact application it was installed for. This usually means "
                    + "the app is being run from a different folder than the one it was registered with.\n\n"
                    + $"This copy is running from:\n{Environment.ProcessPath ?? "an unknown location"}",
                    owner);

                return false;
            }

            Log.Information("[PrivilegedHelper] Not available at check time: {State}", state);

            // The one state the app cannot repair: the executable is not part of this install.
            // Offering an "Activate now" button here would be a button that cannot work.
            if (state == PrivilegedHelperState.BinaryMissing)
            {
                await _dialogs.ShowErrorAsync(
                    "Privileged helper is missing",
                    "The Phantom Obscura privileged helper is not present in this installation, so it "
                    + $"cannot be started {context}.\n\n"
                    + "Reinstalling or repairing Phantom Obscura will restore it.",
                    owner);
                return false;
            }

            var confirmed = await _dialogs.ShowConfirmationAsync(
                Title(state),
                Message(state, context),
                confirmText: "Activate now",
                cancelText: "Not now",
                owner: owner);

            if (!confirmed)
                return false;

            bool running;
            try
            {
                running = await _controller.ActivateAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PrivilegedHelper] Activation threw");
                running = false;
            }

            if (running)
            {
                Log.Information("[PrivilegedHelper] Activated from state {State}", state);
                RememberConsent();
                return true;
            }

            await _dialogs.ShowErrorAsync(
                "Could not activate the helper",
                "The Phantom Obscura privileged helper could not be started. The most common reasons are "
                + "that the Windows permission prompt was declined, or that the service is blocked by "
                + "policy on this machine.\n\n"
                + "You can try again, or start \"Phantom Obscura Privileged Helper\" from Windows Services.",
                owner);

            return false;
        }

        private static string Title(PrivilegedHelperState state) => state switch
        {
            PrivilegedHelperState.Disabled => "Privileged helper is disabled",
            PrivilegedHelperState.NotInstalled => "Privileged helper is not installed",
            _ => "Privileged helper is not running"
        };

        private static string Message(PrivilegedHelperState state, string context) => state switch
        {
            PrivilegedHelperState.Disabled =>
                "The Phantom Obscura privileged helper is installed but has been disabled, so it cannot "
                + $"start on its own and is unavailable {context}.\n\n"
                + "Activating it will re-enable the service and start it. Windows will ask for permission.",

            PrivilegedHelperState.NotInstalled =>
                "The Phantom Obscura privileged helper is not installed, so privileged operations are "
                + $"unavailable {context}.\n\n"
                + "Activating it installs the helper once. Windows will ask for permission, and you "
                + "won't be asked again.",

            _ =>
                "The Phantom Obscura privileged helper is installed but has been stopped, so privileged "
                + $"operations are unavailable {context}.\n\n"
                + "Activating it will start the service again. Windows will ask for permission."
        };

        /// <summary>
        /// Activating is consent: later privileged calls install or restart silently instead of
        /// asking again, and an earlier decline is cleared.
        /// </summary>
        private static void RememberConsent()
        {
            try
            {
                var settings = SettingsService.Load();
                settings.PrivilegedBrokerAutoInstall = true;
                settings.PrivilegedBrokerDeclined = false;
                SettingsService.Save(settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PrivilegedHelper] Could not persist activation consent");
            }
        }
    }
}
