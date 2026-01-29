using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Placeholder implementation of IAuthController.
    /// TODO: Wire this to actual authentication flow in MainViewModel/VaultViewModel.
    /// </summary>
    public sealed class AuthController : IAuthController
    {
        private readonly ILogger<AuthController>? _logger;
        private bool _requirePhantomKeyNextUnlock;

        public AuthController(ILogger<AuthController>? logger = null)
        {
            _logger = logger;
        }

        public async Task AddAuthenticationDelayAsync(TimeSpan delay)
        {
            _logger?.LogInformation("Adding authentication delay: {Delay}", delay);
            await Task.Delay(delay);
        }

        public async Task EnforceTempLockoutAsync(TimeSpan lockoutDuration)
        {
            _logger?.LogWarning("Temporary lockout enforced: {Duration}", lockoutDuration);
            // TODO: Integrate with authentication flow to block unlock attempts
            // For now, just log the lockout
            await Task.CompletedTask;
        }

        public void RequirePhantomKeyForNextUnlock()
        {
            _logger?.LogWarning("PhantomKey will be required for next unlock");
            _requirePhantomKeyNextUnlock = true;
            // TODO: Integrate with MainViewModel to enforce keyfile requirement
        }

        /// <summary>
        /// Check if PhantomKey is required and reset the flag after checking.
        /// Called by authentication flow before unlock.
        /// </summary>
        public bool CheckAndResetPhantomKeyRequirement()
        {
            var wasRequired = _requirePhantomKeyNextUnlock;
            _requirePhantomKeyNextUnlock = false;
            return wasRequired;
        }

        public void RequireReauthentication(string reason)
        {
            _logger?.LogWarning("Re-authentication required: {Reason}", reason);
            // TODO: Integrate with MainViewModel to trigger lock and show reason
            // For now, log the requirement. The calling code should also lock the vault.
        }
    }
}
