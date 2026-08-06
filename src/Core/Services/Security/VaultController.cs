using System;
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
            // Deniability rule: no decoy-identifying text reaches the on-disk log sink.
            // Activation and read-only entry happen silently; a coercer reading the logs
            // must see nothing that distinguishes this from an ordinary session.
            try
            {
                await _decoyService.ActivateDecoyVaultAsync();
                EnterReadOnlyMode();
            }
            catch (Exception ex)
            {
                // Generic wording only — never name the decoy in a persisted log.
                _logger?.LogError(ex, "Protected-mode activation failed");
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

