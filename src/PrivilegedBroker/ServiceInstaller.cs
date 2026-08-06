using System;
using System.Diagnostics;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Installs / removes the privileged broker as a LocalSystem, auto-start
    /// Windows service via <c>sc.exe</c>. Must be invoked elevated (the UI launches
    /// this once with the "runas" verb — the single, one-time UAC prompt).
    /// </summary>
    internal static class ServiceInstaller
    {
        public static int Install(string? allowedClientExePath)
        {
            string? brokerExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(brokerExe))
            {
                Log("Could not resolve broker executable path.");
                return 3;
            }

            if (string.IsNullOrWhiteSpace(allowedClientExePath))
            {
                Log("Install requires the allow-listed UI executable path.");
                return 4;
            }

            Log($"Install starting. broker='{brokerExe}' client='{allowedClientExePath}'");
            BrokerConfig.SaveAllowedClientPath(allowedClientExePath);

            // Remove any stale registration first so re-install is idempotent.
            RunSc($"stop {BrokerProtocol.ServiceName}", ignoreFailure: true);
            RunSc($"delete {BrokerProtocol.ServiceName}", ignoreFailure: true);

            // sc.exe is picky: a space is required after each '='.
            string binPath = $"\\\"{brokerExe}\\\" --run";
            int create = RunSc(
                $"create {BrokerProtocol.ServiceName} binPath= \"{binPath}\" start= auto obj= LocalSystem DisplayName= \"{BrokerProtocol.ServiceDisplayName}\"");
            if (create != 0)
            {
                Log($"Service creation failed (sc create exit {create}). Aborting install.");
                return create;
            }

            RunSc($"description {BrokerProtocol.ServiceName} \"Performs USB write-protection and raw-volume operations for Phantom Obscura so the app can run without an administrator prompt.\"", ignoreFailure: true);
            // Restart on failure (1s), keep trying.
            RunSc($"failure {BrokerProtocol.ServiceName} reset= 86400 actions= restart/1000/restart/1000/restart/1000", ignoreFailure: true);

            // Start the service now. SCM occasionally needs a moment between create
            // and a successful start, so retry a few times before giving up. Each
            // attempt's sc output is written to the broker log so an elevated install
            // failure is diagnosable (the elevated console is hidden from the user).
            int start = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                start = RunSc($"start {BrokerProtocol.ServiceName}", ignoreFailure: true);
                if (start == 0)
                    break;

                Log($"sc start attempt {attempt} failed (exit {start}). Retrying...");
                System.Threading.Thread.Sleep(1000);
            }

            if (start == 0)
            {
                Log("Privileged helper installed and started.");
                return 0;
            }

            // The service is registered (start= auto) but could not be started right
            // now. Surface this as a failure so the caller does not report success when
            // the helper is not actually running.
            Log($"Privileged helper installed but could not be started (last sc start exit {start}).");
            return 1067; // ERROR_PROCESS_ABORTED-ish marker: created-but-not-started
        }

        public static int Uninstall()
        {
            RunSc($"stop {BrokerProtocol.ServiceName}", ignoreFailure: true);
            int delete = RunSc($"delete {BrokerProtocol.ServiceName}", ignoreFailure: true);
            Log("Phantom Obscura privileged helper removed.");
            return delete;
        }

        private static int RunSc(string arguments, bool ignoreFailure = false)
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                Log($"sc {arguments} -> failed to start sc.exe");
                return ignoreFailure ? 0 : 5;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);

            string combined = $"{output} {error}".Trim();
            if (process.ExitCode != 0 && !ignoreFailure)
            {
                Log($"sc {arguments} -> FAILED ({process.ExitCode}): {combined}");
            }
            else
            {
                Log($"sc {arguments} -> exit {process.ExitCode}{(string.IsNullOrWhiteSpace(combined) ? string.Empty : $": {combined}")}");
            }

            return process.ExitCode;
        }

        private static void Log(string message)
        {
            // Mirror to the broker log file (diagnosable after an elevated run) and to
            // the console for --console / attended installs.
            Program.TryLog($"[install] {message}");
            Console.WriteLine(message);
        }
    }
}
