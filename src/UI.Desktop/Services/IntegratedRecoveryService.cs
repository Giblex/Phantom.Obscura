using System;
using System.Diagnostics;
using System.IO;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Locates and launches the suite-bundled PhantomRecovery application.
    /// </summary>
    public class IntegratedRecoveryService
    {
        private readonly SuiteWorkspaceService _workspaceService;

        public IntegratedRecoveryService()
            : this(new SuiteWorkspaceService())
        {
        }

        public IntegratedRecoveryService(SuiteWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        }

        public string? RecoveryExecutablePath => _workspaceService.ResolveRecoveryExecutablePath();

        public bool IsAvailable => !string.IsNullOrWhiteSpace(RecoveryExecutablePath);

        public string AvailabilityMessage => IsAvailable
            ? $"PhantomRecovery is available at {RecoveryExecutablePath}."
            : "PhantomRecovery could not be located in this suite build output.";

        public bool CanLaunch(string? vaultPath, out string message)
        {
            if (!IsAvailable)
            {
                message = AvailabilityMessage;
                return false;
            }

            if (string.IsNullOrWhiteSpace(vaultPath))
            {
                message = "No recovery vault path could be resolved from the current Obscura vault.";
                return false;
            }

            var headerPath = Path.Combine(vaultPath, "header.json");
            message = File.Exists(headerPath)
                ? $"Recovery vault is available at {vaultPath}."
                : $"Recovery workspace resolved at {vaultPath}. PhantomRecovery will initialize it on first open.";

            return true;
        }

        public bool TryLaunch(string vaultPath, out string? errorMessage)
        {
            errorMessage = null;

            if (!CanLaunch(vaultPath, out var availabilityMessage))
            {
                errorMessage = availabilityMessage;
                return false;
            }

            try
            {
                Directory.CreateDirectory(vaultPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = RecoveryExecutablePath!,
                    Arguments = $"--vault \"{vaultPath}\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(RecoveryExecutablePath)!
                });

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to launch PhantomRecovery: {ex.Message}";
                return false;
            }
        }
    }
}
