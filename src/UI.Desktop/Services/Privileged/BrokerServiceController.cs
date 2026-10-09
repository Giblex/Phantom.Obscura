using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.UI.Services.Privileged
{
    /// <summary>
    /// Detects and (one-time, elevated) installs the Phantom Obscura privileged
    /// helper Windows service. The single UAC prompt the user ever sees happens
    /// here — when the helper is first installed. After that the non-elevated app
    /// talks to the always-running service over a named pipe with no further prompts.
    /// </summary>
    /// <summary>
    /// What the privileged helper is actually doing right now. The activation UI needs to tell
    /// these apart, because they call for different wording and different remedies: a stopped
    /// service is started, a disabled one has to be re-enabled first, a missing registration is
    /// installed, and a missing binary cannot be fixed from inside the app at all.
    /// </summary>
    public enum PrivilegedHelperState
    {
        /// <summary>Registered and running. Nothing to do.</summary>
        Running,

        /// <summary>Registered but not running — the ordinary "someone stopped it" case.</summary>
        Stopped,

        /// <summary>Registered with start type Disabled; it cannot be started until re-enabled.</summary>
        Disabled,

        /// <summary>Not registered with the SCM, but the binary is present so it can be installed.</summary>
        NotInstalled,

        /// <summary>The helper executable is not in the installation folder; the app cannot repair this.</summary>
        BinaryMissing
    }

    public sealed class BrokerServiceController
    {
        private const string BrokerExeName = "PhantomVault.PrivilegedBroker.exe";

        // Serializes concurrent "enable helper" attempts so several failing privileged
        // calls only ever raise a single confirmation + elevation prompt.
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>True when the service is registered with the SCM.</summary>
        public bool IsInstalled() => QueryState() is not null;

        /// <summary>True when the service is registered and currently running.</summary>
        public bool IsRunning() => string.Equals(QueryState(), "RUNNING", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Ensures the helper is installed and running. If it is missing this raises
        /// a single elevation prompt to install it. Returns false if the helper
        /// binary cannot be found or the user declines elevation.
        /// </summary>
        public async Task<bool> EnsureInstalledAsync()
        {
            if (IsRunning())
                return true;

            string? brokerExe = ResolveBrokerExe();
            if (brokerExe is null)
                return false;

            string? uiExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(uiExe))
                return false;

            try
            {
                var psi = new ProcessStartInfo(brokerExe, $"--install \"{uiExe}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas", // single one-time UAC prompt
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return false;

                await process.WaitForExitAsync().ConfigureAwait(false);
                return process.ExitCode == 0 && IsRunning();
            }
            catch (Win32Exception)
            {
                // User cancelled the UAC elevation prompt.
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts an already-installed helper without rewriting its registration or
        /// changing its allow-listed client. Returns false when the service is absent,
        /// elevation is declined, or SCM cannot start it.
        /// </summary>
        public async Task<bool> StartInstalledAsync()
        {
            if (IsRunning())
                return true;
            if (!IsInstalled())
                return false;

            string? brokerExe = ResolveBrokerExe();
            if (brokerExe is null)
                return false;

            try
            {
                var psi = new ProcessStartInfo(brokerExe, "--start")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return false;

                await process.WaitForExitAsync().ConfigureAwait(false);
                // SCM can report "already running" if another caller won the race;
                // the observed final service state is authoritative.
                return IsRunning();
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Serialized, retry-friendly "enable the helper" flow used when a privileged
        /// call fails because the broker isn't running. Concurrent callers collapse onto
        /// a single confirmation + elevation prompt. <paramref name="confirmAsync"/> is
        /// the UI confirmation (e.g. a dialog); it is only shown when the helper is
        /// actually missing. Returns true if the helper is running afterwards.
        /// </summary>
        public async Task<bool> PromptAndInstallAsync(Func<Task<bool>> confirmAsync)
        {
            if (confirmAsync is null)
                throw new ArgumentNullException(nameof(confirmAsync));

            if (IsRunning())
                return true;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another waiter may have completed the install while we were queued.
                if (IsRunning())
                    return true;

                if (!await confirmAsync().ConfigureAwait(false))
                    return false;

                return await EnsureInstalledAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// The helper's current state. Checked before showing any activation dialogue so the
        /// wording matches reality rather than guessing between "not installed" and "not running".
        /// </summary>
        public PrivilegedHelperState GetState()
        {
            string? state = QueryState();

            if (state is null)
            {
                // Not registered. Whether that is fixable depends on the binary being present.
                return ResolveBrokerExe() is null
                    ? PrivilegedHelperState.BinaryMissing
                    : PrivilegedHelperState.NotInstalled;
            }

            if (string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase))
                return PrivilegedHelperState.Running;

            if (ResolveBrokerExe() is null)
                return PrivilegedHelperState.BinaryMissing;

            return IsStartTypeDisabled() ? PrivilegedHelperState.Disabled : PrivilegedHelperState.Stopped;
        }

        /// <summary>
        /// Brings the helper back to running from whatever state it is in, raising at most one
        /// elevation prompt. Returns true only when the service is genuinely running afterwards —
        /// never on a "probably worked".
        /// </summary>
        public async Task<bool> ActivateAsync()
        {
            if (IsRunning())
                return true;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another caller may have fixed it while we queued behind the gate.
                if (IsRunning())
                    return true;

                switch (GetState())
                {
                    case PrivilegedHelperState.Running:
                        return true;

                    case PrivilegedHelperState.BinaryMissing:
                        // Nothing the app can do: the helper is not part of this installation.
                        return false;

                    case PrivilegedHelperState.NotInstalled:
                        return await EnsureInstalledAsync().ConfigureAwait(false);

                    default:
                        // Stopped or Disabled. "--start" now re-enables a disabled service before
                        // starting it. If it still will not come up, the registration itself is
                        // suspect, so fall back to a reinstall — which deletes and recreates the
                        // service as start= auto and starts it.
                        if (await StartInstalledAsync().ConfigureAwait(false))
                            return true;

                        return await EnsureInstalledAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// True when the registered service's start type is Disabled. Read with "sc qc", which
        /// needs no elevation.
        /// </summary>
        private static bool IsStartTypeDisabled()
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", $"qc {BrokerProtocol.ServiceName}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return false;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10_000);
                if (process.ExitCode != 0)
                    return false;

                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    // e.g. "START_TYPE         : 4   DISABLED"
                    if (line.StartsWith("START_TYPE", StringComparison.OrdinalIgnoreCase))
                        return line.Contains("DISABLED", StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Resolves the helper executable shipped alongside the app.</summary>
        public static string? ResolveBrokerExe()
        {
            string deployed = Path.Combine(AppContext.BaseDirectory, "Broker", BrokerExeName);
            if (File.Exists(deployed))
                return deployed;

            string alongside = Path.Combine(AppContext.BaseDirectory, BrokerExeName);
            return File.Exists(alongside) ? alongside : null;
        }

        /// <summary>Returns the SCM state string (e.g. "RUNNING", "STOPPED") or null if not installed.</summary>
        private static string? QueryState()
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", $"query {BrokerProtocol.ServiceName}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10_000);

                if (process.ExitCode != 0)
                    return null; // 1060 = service does not exist

                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                    {
                        // e.g. "STATE              : 4  RUNNING"
                        int idx = line.IndexOf(':');
                        if (idx >= 0)
                        {
                            var parts = line[(idx + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                                return parts[1];
                        }
                    }
                }

                return "UNKNOWN";
            }
            catch
            {
                return null;
            }
        }
    }
}
