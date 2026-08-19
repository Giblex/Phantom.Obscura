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
