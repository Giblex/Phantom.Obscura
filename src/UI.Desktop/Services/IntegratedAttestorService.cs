using System;
using System.Diagnostics;
using System.IO;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Locates and launches the suite-bundled PhantomAttestor application.
    /// </summary>
    public sealed class IntegratedAttestorService
    {
        private readonly SuiteWorkspaceService _workspaceService;

        public IntegratedAttestorService()
            : this(new SuiteWorkspaceService())
        {
        }

        public IntegratedAttestorService(SuiteWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        }

        public string? AttestorExecutablePath => _workspaceService.ResolveAttestorExecutablePath();

        public bool IsAvailable => !string.IsNullOrWhiteSpace(AttestorExecutablePath);

        public string AvailabilityMessage => IsAvailable
            ? $"PhantomAttestor is available at {AttestorExecutablePath}."
            : "PhantomAttestor could not be located in this suite build output.";

        public bool TryLaunch(out string? errorMessage)
        {
            errorMessage = null;

            if (!IsAvailable)
            {
                errorMessage = AvailabilityMessage;
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AttestorExecutablePath!,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(AttestorExecutablePath!)!
                });

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to launch PhantomAttestor: {ex.Message}";
                return false;
            }
        }
    }
}
