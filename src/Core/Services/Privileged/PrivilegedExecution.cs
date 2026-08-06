using System;

namespace PhantomVault.Core.Services.Privileged
{
    /// <summary>
    /// Ambient routing switch for privileged volume operations. The desktop app
    /// (shipped <c>asInvoker</c>, i.e. non-elevated) sets <see cref="Broker"/> to a
    /// named-pipe client during startup; the privileged Core services then forward
    /// admin-only primitives to the elevated broker service instead of attempting
    /// them in-process (which would fail without elevation).
    ///
    /// The broker process itself sets <see cref="ForceInProcess"/> so it always
    /// executes the real implementation and never tries to broker back to itself.
    /// </summary>
    public static class PrivilegedExecution
    {
        /// <summary>
        /// UI-side transport to the elevated broker. Null when no broker is wired
        /// (e.g. in the broker process, in tests, or before first-run install).
        /// </summary>
        public static IPrivilegedVolumeOperations? Broker { get; set; }

        /// <summary>
        /// When true, privileged services always run in-process regardless of
        /// elevation state. Set by the broker host and by any deliberately elevated
        /// launch.
        /// </summary>
        public static bool ForceInProcess { get; set; }

        /// <summary>True if the current Windows process holds the Administrator role.</summary>
        public static bool IsProcessElevated()
        {
            if (!OperatingSystem.IsWindows())
                return false;
            try
            {
#pragma warning disable CA1416
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when privileged calls should be forwarded to the broker.</summary>
        public static bool ShouldBroker => !ForceInProcess && Broker != null && !IsProcessElevated();

        /// <summary>
        /// True when a privileged call cannot run: not elevated, not forced
        /// in-process, and no broker is configured. Callers should surface a
        /// "install the privileged helper" prompt.
        /// </summary>
        public static bool RequiresBrokerButMissing => !ForceInProcess && Broker == null && !IsProcessElevated();
    }
}
