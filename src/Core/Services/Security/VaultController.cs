using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{

    public sealed class VaultController : IVaultController
    {
        private readonly ILogger<VaultController>? _logger;
        private readonly DecoyVaultService _decoyService;
        private bool _isReadOnly;

        public bool IsReadOnly => _isReadOnly;
        public bool IsDecoyActive => _decoyService.IsDecoyActive;
        public VaultDatabase? ActiveDecoyDatabase => _decoyService.DecoyDatabase;

        public VaultController(ILogger<VaultController>? logger = null, DecoyVaultOptions? decoyOptions = null)
        {
            _logger = logger;
            _decoyService = new DecoyVaultService(decoyOptions, logger as ILogger<DecoyVaultService>);
        }

        public async Task SwitchToDecoyVaultAsync()
        {
            _logger?.LogCritical("SWITCHING TO DECOY VAULT - Suspected security compromise");

            try
            {

                var decoyDatabase = await _decoyService.ActivateDecoyVaultAsync();

                EnterReadOnlyMode();

                int totalCredentials = decoyDatabase.Groups?.Sum(g => g.Entries?.Count ?? 0) ?? 0;
                _logger?.LogCritical(
                    "Decoy vault activated successfully with {CredentialCount} fake credentials. " +
                    "Real vault data is protected. This is a critical security event.",
                    totalCredentials);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to activate decoy vault");
                throw;
            }
        }

        public void EnterReadOnlyMode()
        {
            _logger?.LogWarning("Entering read-only mode");
            _isReadOnly = true;
        }

        public void ExitReadOnlyMode()
        {
            _logger?.LogInformation("Exiting read-only mode");
            _isReadOnly = false;
        }
    }
}

